using System;
using System.Linq;
using Poker.Application;
using UnityEngine;

// The hand has already paid out. Keep this shot separate from betting and the player's eye camera.
internal sealed class Play2MatchFinale
{
    internal enum Beat { None, LastHand, Pullback, Portrait, Recap }
    internal enum Pose { Default, Cheer, Dismay }
    private const float ReadTime = 1.1f, TravelTime = 1.55f, PortraitTime = 2.1f;
    private const string PhotoMark = "[기념사진]";
    private readonly Animator[] actors;
    private readonly Camera camera;
    private readonly Transform table;
    private readonly Transform[] eyes;
    private readonly int[] layers;
    private readonly Pose[] choices;
    private readonly Transform[] chests, heads;
    private readonly Quaternion[] chestPose, headPose;
    private bool gazeApplied;
    private Vector3 start;
    private Quaternion startRotation;
    private float startFov, started, nextDance;
    private bool tookCamera, posed;
    private HoldemRoomPacket state;
    private HoldemRoomPacket posePacket;
    internal bool Active => state != null;
    internal bool InGroup => Stage >= Beat.Pullback;
    internal Beat Stage
    {
        get
        {
            if (!Active) return Beat.None;
            float t = Time.unscaledTime - started - ReadTime;
            if (t < 0f) return Beat.LastHand;
            if (t < TravelTime) return Beat.Pullback;
            return t < TravelTime + PortraitTime ? Beat.Portrait : Beat.Recap;
        }
    }

    internal Play2MatchFinale(GameObject[] players, Transform[] eyes, Transform table, Camera camera)
    {
        this.eyes = eyes;
        this.table = table;
        this.camera = camera;
        actors = players.Select(p => p.GetComponent<Animator>()).ToArray();
        layers = actors.Select(a => a.GetLayerIndex("Table Result")).ToArray();
        choices = new Pose[actors.Length];
        chests = actors.Select(a => a.GetBoneTransform(HumanBodyBones.Chest)).ToArray();
        heads = actors.Select(a => a.GetBoneTransform(HumanBodyBones.Head)).ToArray();
        chestPose = new Quaternion[actors.Length];
        headPose = new Quaternion[actors.Length];
    }

    internal void Begin(HoldemRoomPacket packet, bool live)
    {
        if (Active) return;
        state = packet;
        started = Time.unscaledTime - (live ? 0f : ReadTime + TravelTime + PortraitTime + .01f);
    }

    internal void Reset()
    {
        RestoreGaze();
        if (tookCamera)
            for (int i = 0; i < actors.Length; i++)
            {
                var a = actors[i];
                if (!a.isActiveAndEnabled) continue;
                if (layers[i] >= 0) a.SetLayerWeight(layers[i], 0);
                a.SetBool("AtTable", true);
                a.SetBool("Ready", false);
                a.Play("Base Layer.Seated", 0, 0f);
            }
        state = posePacket = null;
        tookCamera = posed = false;
        nextDance = 0f;
        Array.Clear(choices, 0, choices.Length);
    }

    internal void Frame(HoldemRoomPacket packet)
    {
        if (!tookCamera)
        {
            tookCamera = true;
            start = camera.transform.position;
            startRotation = camera.transform.rotation;
            startFov = camera.fieldOfView;
        }
        var center = table.position;
        Vector3 toward = Vector3.ProjectOnPlane(center - eyes[state.game.sessionWinner - 1].position, Vector3.up).normalized;
        Vector3 endDir = Quaternion.AngleAxis(42f, Vector3.up) * toward;
        Vector3 from = Vector3.ProjectOnPlane(start - center, Vector3.up);
        float elapsed = Time.unscaledTime - started - ReadTime;
        float t = Mathf.SmoothStep(0, 1, Mathf.Clamp01(elapsed / TravelTime));
        float yaw = Vector3.SignedAngle(from, endDir, Vector3.up);
        Vector3 dir = Quaternion.AngleAxis(t * yaw, Vector3.up) * from.normalized;
        float distance = Mathf.Lerp(from.magnitude, 3.00f, t);
        Vector3 position = center + dir * distance + Vector3.up * Mathf.Lerp(start.y - center.y, 1.48f, t);
        var look = Quaternion.LookRotation(center + Vector3.up * .18f - position);
        camera.transform.SetPositionAndRotation(position, Quaternion.Slerp(startRotation, look, t));
        camera.fieldOfView = Mathf.Lerp(startFov, Camera.HorizontalToVerticalFieldOfView(59f, Mathf.Min(camera.aspect, 1.65f)), t);
        if (distance < 1.35f) return;
        if (!posed)
        {
            posed = true;
            foreach (var seat in packet.game.seats)
            {
                int i = seat.seat - 1;
                actors[i].gameObject.SetActive(true);
                ApplyPose(i);
            }
        }
        int win = state.game.sessionWinner - 1;
        var winner = actors[win];
        if (choices[win] == Pose.Default && !winner.IsInTransition(0) && Time.unscaledTime >= nextDance
            && winner.GetCurrentAnimatorStateInfo(0).IsName("Base Layer.Standing"))
        {
            // All-in winners can reach Standing before the result. That state has only a sit-down exit.
            winner.CrossFadeInFixedTime("Base Layer.Dance", .22f);
            nextDance = Time.unscaledTime + 1.6f;
        }
        TurnForPhoto(Mathf.SmoothStep(0f, 1f, (t - .36f) / .64f));
        if (ReferenceEquals(posePacket, packet)) return;
        posePacket = packet;
        var entries = packet.chat?.entries;
        if (entries == null) return;
        foreach (var seat in packet.game.seats)
        {
            var line = entries.LastOrDefault(e => e.seat == seat.seat && TryPose(e.text, packet.game.handId, out _));
            if (line == null || !TryPose(line.text, packet.game.handId, out var choice) || choices[seat.seat - 1] == choice) continue;
            choices[seat.seat - 1] = choice;
            ApplyPose(seat.seat - 1);
        }
    }

    // Layer a small seated turn onto the artist's pose while the camera settles.
    // Restore before Animator evaluation so a held pose cannot accumulate this rotation.
    internal void RestoreGaze()
    {
        if (!gazeApplied) return;
        for (int i = 0; i < actors.Length; i++)
        {
            if (chests[i] != null) chests[i].localRotation = chestPose[i];
            if (heads[i] != null) heads[i].localRotation = headPose[i];
        }
        gazeApplied = false;
    }

    private void TurnForPhoto(float weight)
    {
        for (int i = 0; i < actors.Length; i++)
        {
            var chest = chests[i];
            var head = heads[i];
            if (chest != null) chestPose[i] = chest.localRotation;
            if (head != null) headPose[i] = head.localRotation;
            if (head == null || i + 1 == state.game.sessionWinner && choices[i] == Pose.Default) continue;
            Vector3 forward = Vector3.ProjectOnPlane(table.position - eyes[i].position, Vector3.up);
            Vector3 toCamera = Vector3.ProjectOnPlane(camera.transform.position - head.position, Vector3.up);
            float angle = Vector3.SignedAngle(forward, toCamera, Vector3.up);
            float chestYaw = chest != null ? Mathf.Clamp(angle * .28f, -22f, 22f) : 0f;
            if (chest != null) chest.rotation = Quaternion.AngleAxis(chestYaw * weight, Vector3.up) * chest.rotation;
            float headYaw = Mathf.Clamp(angle - chestYaw, -39f, 39f);
            head.rotation = Quaternion.AngleAxis(headYaw * weight, Vector3.up) * head.rotation;
        }
        gazeApplied = true;
    }

    private void ApplyPose(int i)
    {
        var a = actors[i];
        bool dance = i + 1 == state.game.sessionWinner && choices[i] == Pose.Default;
        a.SetBool("AtTable", !dance);
        a.SetBool("Ready", dance);
        if (layers[i] < 0) return;
        a.SetLayerWeight(layers[i], dance ? 0f : 1f);
        if (!dance)
        {
            bool cheer = choices[i] == Pose.Cheer || choices[i] == Pose.Default && (i + 1) % 2 == 0;
            a.CrossFadeInFixedTime(cheer ? "Table Result.Win" : "Table Result.Loss", .22f, layers[i]);
        }
    }

    internal string NextPoseMessage(int seat)
    {
        var next = (Pose)(((int)choices[seat - 1] + 1) % 3);
        return PhotoMark + state.game.handId + (int)next;
    }

    internal static bool TryPose(string message, string hand, out Pose pose)
    {
        pose = Pose.Default;
        if (hand == null || message == null || message.Length != PhotoMark.Length + hand.Length + 1 ||
            !message.StartsWith(PhotoMark, StringComparison.Ordinal) ||
            string.CompareOrdinal(message, PhotoMark.Length, hand, 0, hand.Length) != 0) return false;
        int choice = message[message.Length - 1] - '0';
        if (choice < 0 || choice > 2) return false;
        pose = (Pose)choice;
        return true;
    }

    internal static string DescribePose(string text)
    {
        if (text?.Length != PhotoMark.Length + 33 || !text.StartsWith(PhotoMark, StringComparison.Ordinal)) return null;
        string hand = text.Substring(PhotoMark.Length, 32);
        return Guid.TryParseExact(hand, "N", out _) && TryPose(text, hand, out var pose) ? "기념사진 포즈  " + PoseName(pose) : null;
    }

    internal static string PoseName(Pose pose) => pose == Pose.Cheer ? "환호" : pose == Pose.Dismay ? "좌절" : "기본";
}
