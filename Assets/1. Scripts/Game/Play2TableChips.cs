using System;
using System.Collections.Generic;
using Poker.Application;
using Poker.Foundation;
using UnityEngine;

// Only confirmed public contributions trigger motion. Piles are symbolic; the HUD owns the exact amount.
internal sealed class Play2TableChips : MonoBehaviour
{
    private readonly Transform[] pile = new Transform[18];
    private readonly Transform[] moving = new Transform[4];
    private readonly Play2BettingHand[] hands = new Play2BettingHand[4];
    private readonly Queue<int> bets = new();
    internal bool IsAnimating => activeSeat >= 0 || bets.Count > 0;
    internal bool IsBetting(int index) => activeSeat == index || bets.Contains(index);
    internal Play2BettingHand FreeHand(int index) => hands[index] ??=
        new Play2BettingHand(players[index], index + 1 == viewer ? viewCamera : null);
    internal void RestHand(int index) => hands[index]?.Rest();
    private static readonly Vector3 PotPosition = new(-.28f, .014f, .27f);
    private Transform[] eyes;
    private GameObject[] players;
    private Transform viewCamera;
    private Play2TableAudio sound;
    private Material material;
    private int viewer, activeSeat = -1, targetCount;
    private float began;
    private bool landed;
    private Vector3 flightStart, flightEnd;

    public void Initialize(Transform[] seatEyes, Material chipMaterial, GameObject[] actors,
        Transform view, Play2TableAudio audio)
    {
        eyes = seatEyes;
        material = chipMaterial;
        players = actors;
        viewCamera = view;
        sound = audio;
        for (int i = 0; i < pile.Length; i++)
        {
            pile[i] = MakeChip("Pot chip", i / 6 == 1 ? Play2UGui.Hex("405D71") : Play2UGui.Hex("C05C42"));
            pile[i].localPosition = StackPosition(i);
        }
        for (int i = 0; i < moving.Length; i++)
            moving[i] = MakeChip("Bet from seat " + (i + 1), Play2UGui.Hex("C05C42"));
    }

    private static Vector3 StackPosition(int i) => PotPosition
        + new Vector3((i / 6) * .071f, (i % 6) * .008f, (i / 6 == 1 ? -.025f : 0));

    private Transform MakeChip(string label, Color color)
    {
        var chip = new GameObject(label).transform;
        chip.SetParent(transform, false);
        Disc(chip, "Edge", 0, .062f, .0035f, color);
        Disc(chip, "Ivory inset", .0036f, .047f, .0005f, Play2UGui.Cream);
        Disc(chip, "Face", .0044f, .036f, .0004f, color);
        for (int i = 0; i < 6; i++)
        {
            float angle = i * Mathf.PI / 3;
            var marker = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(marker.GetComponent<Collider>());
            marker.name = "Edge inlay";
            marker.transform.SetParent(chip, false);
            marker.transform.localPosition = new Vector3(Mathf.Sin(angle) * .026f, .0038f, Mathf.Cos(angle) * .026f);
            marker.transform.localScale = new Vector3(.006f, .0008f, .009f);
            marker.transform.localRotation = Quaternion.Euler(0, i * 60, 0);
            Tint(marker.GetComponent<Renderer>(), Play2UGui.Cream);
        }
        chip.gameObject.SetActive(false);
        return chip;
    }

    private void Disc(Transform chip, string label, float y, float size, float height, Color color)
    {
        var disc = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        disc.name = label;
        Destroy(disc.GetComponent<Collider>());
        disc.transform.SetParent(chip, false);
        disc.transform.localScale = new Vector3(size, height, size);
        disc.transform.localPosition = Vector3.up * y;
        Tint(disc.GetComponent<Renderer>(), color);
    }

    private void Tint(Renderer renderer, Color color)
    {
        var tint = new MaterialPropertyBlock();
        tint.SetColor("_BaseColor", color);
        tint.SetColor("_Color", color);
        renderer.sharedMaterial = material;
        renderer.SetPropertyBlock(tint);
    }

    public void Present(HoldemRoomPacket previous, HoldemRoomPacket next)
    {
        var game = next?.hasGame == true ? next.game : null;
        var old = previous?.hasGame == true ? previous.game : null;
        targetCount = game == null || game.pot <= 0 ? 0 : Mathf.Clamp(1 + (int)Math.Log(game.pot, 1.6), 1, pile.Length);
        if (game == null || old == null || game.handId != old.handId || viewer != next.viewerSeat)
        {
            ResetHands();
            viewer = next?.viewerSeat ?? 1;
            ShowPile(targetCount);
            return;
        }
        var history = next.history;
        if (history?.entries == null || previous.history?.entries == null) return;
        long previousCount = previous.history.omittedCount + previous.history.entries.Length;
        int first = (int)Math.Max(0, Math.Min(history.entries.Length, previousCount - history.omittedCount));
        for (int i = first; i < history.entries.Length; i++)
        {
            var entry = history.entries[i];
            if (entry.seat < 1 || entry.seat > moving.Length) continue;
            if (entry.hasAction && entry.action == (int)BettingActionKind.Check) sound.Check();
            if (entry.hasAction && entry.action == (int)BettingActionKind.Fold
                || entry.kind == (int)HoldemHistoryKind.FixedFold) sound.Fold();
            if (!entry.hasAction || entry.amount <= 0) continue;
            if (entry.action == (int)BettingActionKind.Call || entry.action == (int)BettingActionKind.BetTo
                || entry.action == (int)BettingActionKind.RaiseTo)
                bets.Enqueue(entry.seat - 1);
        }
        if (activeSeat < 0 && bets.Count == 0) ShowPile(targetCount);
    }

    // Called after the card-holding arm has been posed, so the free hand cannot fight the grip animation.
    internal void UpdateHands()
    {
        if (activeSeat < 0 && bets.Count > 0) StartBet();
        if (activeSeat < 0) return;
        int index = activeSeat;
        float time = Time.time - began;
        var chip = moving[index];
        hands[index].Pose(time);
        if (time < .26f)
        {
            chip.position = hands[index].Contact;
            chip.rotation = Quaternion.Euler(0, eyes[index].eulerAngles.y, 0);
            flightStart = chip.localPosition;
        }
        else
        {
            float t = Mathf.Clamp01((time - .26f) / .34f);
            chip.localPosition = Vector3.Lerp(flightStart, flightEnd, t)
                + Vector3.up * (.11f * 4f * t * (1 - t));
            chip.localRotation = Quaternion.Euler(18f * Mathf.Sin(t * Mathf.PI), 230f * t, 0);
            if (t >= 1)
            {
                if (!landed) { landed = true; sound.ChipLand(); }
                float bounce = Mathf.Clamp01((time - .60f) / .16f);
                chip.localPosition = flightEnd + Vector3.up * (.015f * Mathf.Sin(bounce * Mathf.PI));
                chip.localRotation = Quaternion.Euler(7f * Mathf.Sin(bounce * Mathf.PI * 2) * (1 - bounce), 230, 0);
            }
        }
        if (time < .76f) return;
        chip.gameObject.SetActive(false);
        hands[index].Rest();
        activeSeat = -1;
        ShowPile(targetCount);
    }

    private void StartBet()
    {
        activeSeat = bets.Dequeue();
        began = Time.time;
        landed = false;
        FreeHand(activeSeat);
        Vector3 seat = transform.InverseTransformPoint(eyes[activeSeat].position);
        Vector3 outward = new Vector3(seat.x, 0, seat.z).normalized;
        Vector3 side = activeSeat + 1 == viewer
            ? transform.InverseTransformDirection(viewCamera.right)
            : Vector3.Cross(Vector3.up, outward);
        var start = outward * .54f + side * .15f + Vector3.up * .08f;
        var release = outward * .40f + side * .11f + Vector3.up * .11f;
        hands[activeSeat].Begin(transform.TransformPoint(start), transform.TransformPoint(release));
        flightEnd = StackPosition(Mathf.Max(0, targetCount - 1));
        moving[activeSeat].gameObject.SetActive(true);
    }

    private void ShowPile(int count)
    {
        for (int i = 0; i < pile.Length; i++) pile[i].gameObject.SetActive(i < count);
    }

    private void ResetHands()
    {
        bets.Clear();
        activeSeat = -1;
        for (int i = 0; i < hands.Length; i++)
        {
            hands[i]?.Release();
            hands[i] = null;
            if (moving[i] != null) moving[i].gameObject.SetActive(false);
        }
    }

    private void OnDestroy() => ResetHands();
}
