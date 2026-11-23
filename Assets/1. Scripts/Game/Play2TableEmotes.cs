using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

internal sealed class Play2TableEmotes : IDisposable
{
    private sealed class Pose
    {
        internal GameObject sample;
        internal Transform sampleShoulder;
        internal Transform middleBase, middleTip, indexBase, pinkyBase;
        internal Transform[] source, target;
        internal Quaternion[] previous;
        internal Mesh armMesh;
        internal SkinnedMeshRenderer skin;
        internal AnimationClip clip;
        internal AnimationClip[] clips;
        internal float started;
        internal bool applied;
        internal bool middleFinger;
    }

    private readonly Pose[] poses;
    private readonly GameObject[] actors, sources;
    private readonly Transform[] eyes;
    private readonly Transform root, camera;
    private readonly Play2EmotionWheel wheel;

    internal Play2TableEmotes(GameObject[] actors, GameObject[] sources, Transform[] eyes, Transform root, Transform camera,
        Canvas canvas, TMP_FontAsset font, Action<int> confirm)
    {
        this.actors = actors;
        this.sources = sources;
        this.eyes = eyes;
        this.root = root;
        this.camera = camera;
        poses = new Pose[actors.Length];
        wheel = new Play2EmotionWheel(canvas.transform, canvas, font, confirm);
    }

    internal bool TickWheel(bool allowed) => wheel.Tick(allowed);

    internal void Play(int seat, int emote, int viewer)
    {
        int index = seat - 1;
        if (index < 0 || index >= actors.Length || emote < 1 || emote > Play2EmotionWheel.Motions.Length) return;
        var pose = poses[index] ??= CreatePose(index, seat == viewer);
        string motion = Play2EmotionWheel.Motions[emote - 1];
        pose.clip = Array.Find(pose.clips, clip => clip.name.EndsWith("_" + motion,
            StringComparison.Ordinal));
        if (pose.clip == null) return;
        pose.started = Time.unscaledTime;
        pose.middleFinger = motion == Play2EmotionWheel.MiddleFingerMotion;
    }

    private Pose CreatePose(int index, bool own)
    {
        var sample = UnityEngine.Object.Instantiate(sources[index], root);
        sample.name = own ? "First Person Emote Arm" : "Emote Pose Source";
        var animator = sample.GetComponent<Animator>();
        animator.enabled = false;
        var skin = sample.GetComponentInChildren<SkinnedMeshRenderer>(true);
        var referenceBones = sample.GetComponentsInChildren<Transform>(true);
        var shoulder = Array.Find(referenceBones, bone => bone.name == "mixamorig:LeftShoulder");
        var pose = new Pose { sample = sample, sampleShoulder = shoulder, skin = skin,
            clips = animator.runtimeAnimatorController.animationClips };
        skin.enabled = false;
        if (own)
        {
            pose.armMesh = UnityEngine.Object.Instantiate(skin.sharedMesh);
            skin.sharedMesh = pose.armMesh;
            Play2CardGrip.ShowArmOnly(skin, Array.Find(referenceBones, bone => bone.name == "mixamorig:LeftArm"));
        }
        else
        {
            var targetBones = actors[index].GetComponentsInChildren<Transform>(true);
            var sourcesByName = new List<Transform>();
            var targetsByName = new List<Transform>();
            foreach (var source in shoulder.GetComponentsInChildren<Transform>(true))
            {
                var target = Array.Find(targetBones, bone => bone != null && bone.name == source.name);
                if (target == null) continue;
                sourcesByName.Add(source);
                targetsByName.Add(target);
            }
            pose.source = sourcesByName.ToArray();
            pose.target = targetsByName.ToArray();
            pose.previous = new Quaternion[pose.target.Length];
            Transform FindFinger(string name) => Array.Find(pose.target, bone => bone.name == "mixamorig:LeftHand" + name);
            pose.middleBase = FindFinger("Middle1");
            pose.middleTip = FindFinger("Middle3");
            pose.indexBase = FindFinger("Index1");
            pose.pinkyBase = FindFinger("Pinky1");
        }
        sample.SetActive(true);
        return pose;
    }

    internal void Restore()
    {
        foreach (var pose in poses)
        {
            if (pose?.applied != true) continue;
            for (int i = 0; i < pose.target.Length; i++)
                if (pose.target[i] != null) pose.target[i].localRotation = pose.previous[i];
            pose.applied = false;
        }
    }

    internal void Animate(int viewer, float[] seatLookYaw, Play2TableChips chips, Play2TableReactions reactions)
    {
        for (int i = 0; i < poses.Length; i++)
        {
            var pose = poses[i];
            if (pose?.clip == null) continue;
            float time = Time.unscaledTime - pose.started;
            if (time >= pose.clip.length || chips.IsBetting(i) || reactions.PlayingReaction(i)
                || (i + 1 != viewer && !actors[i].activeInHierarchy))
            {
                Stop(pose);
                continue;
            }
            float blend = Mathf.Clamp01(Mathf.Min(time, pose.clip.length - time) / .12f);
            pose.clip.SampleAnimation(pose.sample, time);
            var turn = Quaternion.AngleAxis(seatLookYaw[i] * blend, Vector3.up);
            if (i + 1 == viewer)
            {
                var body = actors[i].transform;
                pose.sample.transform.SetPositionAndRotation(body.position + camera.TransformVector(new Vector3(-.115f, .23f, .43f)),
                    body.rotation);
                pose.sample.transform.localScale = body.lossyScale;
                pose.sampleShoulder.rotation = turn * pose.sampleShoulder.rotation;
                pose.skin.enabled = true;
                continue;
            }
            for (int bone = 0; bone < pose.target.Length; bone++)
            {
                var target = pose.target[bone];
                pose.previous[bone] = target.localRotation;
                target.localRotation = Quaternion.Slerp(target.localRotation, pose.source[bone].localRotation, blend);
            }
            if (pose.target.Length > 0) pose.target[0].rotation = turn * pose.target[0].rotation;
            if (pose.middleFinger)
                AlignMiddleFinger(pose, turn * eyes[i].forward, blend);
            pose.applied = true;
        }
    }

    private static void AlignMiddleFinger(Pose pose, Vector3 gaze, float blend)
    {
        if (pose.middleBase == null || pose.middleTip == null || pose.indexBase == null || pose.pinkyBase == null) return;
        Vector3 finger = pose.middleTip.position - pose.middleBase.position;
        Vector3 palm = Vector3.Cross(finger, pose.pinkyBase.position - pose.indexBase.position);
        var wrist = pose.middleBase.parent;
        // Match the left palm convention used by Play2FingerRig. The back faces the gaze.
        Quaternion current = Quaternion.LookRotation(finger.normalized, palm);
        Quaternion upright = Quaternion.LookRotation(Vector3.up, -gaze);
        wrist.rotation = Quaternion.Slerp(Quaternion.identity, upright * Quaternion.Inverse(current), blend) * wrist.rotation;
    }

    private static void Stop(Pose pose)
    {
        pose.clip = null;
        if (pose.armMesh != null && pose.skin != null) pose.skin.enabled = false;
    }

    internal void Reset()
    {
        wheel.Cancel();
        Restore();
        foreach (var pose in poses)
            if (pose != null) Stop(pose);
    }

    public void Dispose()
    {
        Reset();
        wheel.Dispose();
        foreach (var pose in poses)
        {
            if (pose == null) continue;
            if (pose.armMesh != null) UnityEngine.Object.Destroy(pose.armMesh);
            UnityEngine.Object.Destroy(pose.sample);
        }
    }
}
