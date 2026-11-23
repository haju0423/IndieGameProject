using System;
using System.Collections;
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
    private readonly Queue<(HoldemHistoryEntryPacket bet, int sweep, int chips)> beats = new();
    private Play2WagerFelt felt;
    private int sweeping, sweepCount;
    private float sweepStart;
    internal int RequestedSweep { get; private set; }
    internal int FinishedSweep { get; private set; }
    private int cheekSeat = -1;
    private float cheekStarted;
    private readonly Transform[] allInStack = new Transform[12];
    internal const float PushBegin = .30f, PushEnd = .68f;
    private int pushChips;
    private float reachablePush = 1f;
    private readonly long[] paidBySeat = new long[4];
    private long handPot, paidTotal;
    private int matchWinner;
    private readonly Transform[] heads = new Transform[4];
    private Play2FaceContact[] faces;
    private Vector3 strikeAnchor, rubAnchor;
    private HoldemHistoryEntryPacket action;
    private bool struck;
    private Quaternion beforeFlinch;
    private Transform flinching;
    private Vector3 hitPoint, dropPoint, pushStart, pushStop, pushSide;
    internal float PushTime(int index) => activeSeat == index && action?.allIn == true ? Time.time - began : -1;
    internal bool WaitToStand(int index)
    {
        if (action?.allIn == true && activeSeat == index) return true;
        foreach (var beat in beats) if (beat.bet?.seat == index + 1 && beat.bet.allIn) return true;
        return false;
    }
    internal bool IsAnimating => activeSeat >= 0 || beats.Count > 0 || sweeping != 0;
    internal bool IsBetting(int index)
    {
        if (activeSeat == index) return true;
        foreach (var beat in beats) if (beat.bet?.seat == index + 1) return true;
        return false;
    }
    internal Play2BettingHand FreeHand(int index) => hands[index] ??=
        new Play2BettingHand(players[index], index + 1 == viewer ? viewCamera : null);
    internal void RestHand(int index) => hands[index]?.Rest();
    private static readonly Vector3 PotPosition = new(-.28f, .014f, .27f);
    private Transform[] eyes;
    private GameObject[] players;
    private Transform viewCamera;
    private Play2TableAudio sound;
    private Material material;
    private Mesh chipMesh;
    private int viewer, activeSeat = -1, targetCount;
    private float began;
    private bool landed, released;
    private Vector3 flightStart, flightEnd;
    private float releaseYaw;
    private Quaternion releaseTilt;

    public void Initialize(Transform[] seatEyes, Material chipMaterial, GameObject[] actors,
        Play2FaceContact[] faceContacts, Transform view, Play2TableAudio audio)
    {
        faces = faceContacts;
        eyes = seatEyes;
        material = chipMaterial;
        tint = new MaterialPropertyBlock();
        chipMesh = Resources.Load<Mesh>("TablePresentation/PokerChip");
        felt = new Play2WagerFelt(i => MakeChip("Wager on felt " + (i + 1), StackColor(i % 12)), chipMesh.bounds, audio);
        players = actors;
        viewCamera = view;
        sound = audio;
        for (int i = 0; i < pile.Length; i++)
        {
            pile[i] = MakeChip("Pot chip", StackColor(i));
            pile[i].localPosition = StackPosition(i);
            pile[i].localRotation = Quaternion.Euler(0, StackYaw(i), 0);
        }
        for (int i = 0; i < moving.Length; i++)
        {
            moving[i] = MakeChip("Bet from seat " + (i + 1), StackColor(0));
            heads[i] = actors[i].GetComponent<Animator>().GetBoneTransform(HumanBodyBones.Head);
        }
        for (int i = 0; i < allInStack.Length; i++)
            allInStack[i] = MakeChip("All-in chip " + (i + 1), StackColor(i % 2 == 0 ? 0 : 6));
    }

    private static Vector3 StackPosition(int i) => PotPosition
        + new Vector3((i / 6) * .071f, (i % 6) * .0062f, (i / 6 == 1 ? -.025f : 0));

    private static Color StackColor(int index) => index / 6 == 1 ? Play2UGui.Hex("405D71") : Play2UGui.Hex("AE5042");
    private static float StackYaw(int index) => (index * 47 + 9) % 360;

    private Transform MakeChip(string label, Color color)
    {
        var chip = new GameObject(label).transform;
        chip.SetParent(transform, false);
        chip.gameObject.AddComponent<MeshFilter>().sharedMesh = chipMesh;
        var renderer = chip.gameObject.AddComponent<MeshRenderer>();
        renderer.sharedMaterials = new[] { material, material };
        Tint(renderer, 0, color);
        Tint(renderer, 1, Play2UGui.Cream);
        chip.gameObject.SetActive(false);
        return chip;
    }

    private MaterialPropertyBlock tint;

    private void Tint(Renderer renderer, int slot, Color color)
    {
        tint.SetColor("_BaseColor", color);
        tint.SetFloat("_Smoothness", .24f);
        tint.SetFloat("_Metallic", 0);
        renderer.SetPropertyBlock(tint, slot);
    }

    public void Present(HoldemRoomPacket previous, HoldemRoomPacket next)
    {
        var game = next?.hasGame == true ? next.game : null;
        var old = previous?.hasGame == true ? previous.game : null;
        handPot = game?.pot ?? 0;
        matchWinner = game?.isOver == true ? game.sessionWinner : 0;
        if (game == null || old == null || previous.sessionId != next.sessionId || game.handId != old.handId || viewer != next.viewerSeat)
        {
            ResetHands();
            paidTotal = 0;
            Array.Clear(paidBySeat, 0, paidBySeat.Length);
            targetCount = PileSize(handPot);
            viewer = next?.viewerSeat ?? 1;
            ShowPile(targetCount);
            return;
        }
        targetCount = PileSize(handPot - paidTotal);
        var history = next.history;
        if (history?.entries == null || previous.history?.entries == null)
        {
            QueueSweepOnTransition(old, game);
            return;
        }
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
                beats.Enqueue((entry, 0, 0));
        }
        QueueSweepOnTransition(old, game);
    }

    private void QueueSweepOnTransition(HoldemGamePacket before, HoldemGamePacket after)
    {
        if (after.board.Length > before.board.Length || after.hasResult && !before.hasResult)
            beats.Enqueue((null, ++RequestedSweep, targetCount));
    }

    private void FixedUpdate()
    {
        if (sweeping == 0) felt?.Step(Time.fixedDeltaTime);
    }

    // Called after the card-holding arm has been posed, so the free hand cannot fight the grip animation.
    internal void UpdateHands()
    {
        if (activeSeat < 0 && sweeping == 0 && beats.Count > 0)
        {
            var beat = beats.Dequeue();
            if (beat.bet != null) StartBet(beat.bet);
            else
            {
                sweeping = beat.sweep;
                sweepCount = beat.chips;
                sweepStart = Time.time;
                felt.BeginSweep();
            }
        }
        if (sweeping != 0)
        {
            float sweep = (Time.time - sweepStart) / .46f;
            felt.Sweep(sweep, sweepCount, StackPosition);
            if (sweep >= 1f || felt.Count == 0)
            {
                felt.Clear();
                ShowPile(sweepCount);
                FinishedSweep = sweeping;
                sweeping = 0;
            }
        }
        if (cheekSeat >= 0)
        {
            float elapsed = Time.time - cheekStarted;
            if (activeSeat == cheekSeat || elapsed >= .89f)
            {
                if (activeSeat != cheekSeat) hands[cheekSeat].Rest();
                cheekSeat = -1;
            }
            else FreeHand(cheekSeat).RubCheek(FaceFrame(cheekSeat).TransformPoint(rubAnchor), eyes[cheekSeat], elapsed);
        }
        if (activeSeat < 0) return;
        int index = activeSeat;
        float time = Time.time - began;
        var chip = moving[index];
        if (action.allIn)
        {
            AnimatePush(time);
            if (time >= 1.30f) FinishBet();
            return;
        }
        var hand = hands[index];
        bool aimed = action.chipAimSeat >= 1 && action.chipAimSeat <= eyes.Length && action.chipAimSeat != activeSeat + 1;
        float airTime = time - Play2BettingHand.ReleaseTime;
        if (airTime >= 0f && !released)
        {
            // Sample the exact point of release even when a slow frame skips across it.
            hand.Pose(Play2BettingHand.ReleaseTime, aimed);
            flightStart = transform.InverseTransformPoint(hand.Contact);
            releaseTilt = Quaternion.Inverse(transform.rotation * Quaternion.Euler(0f, releaseYaw, 0f))
                * hand.PinchRotation;
            if (aimed) LocateStrike(action.chipAimSeat - 1);
            released = true;
        }
        if (time < Play2BettingHand.TossEnd || !released) hand.Pose(time, aimed);
        else if (aimed && time - Time.deltaTime < Play2BettingHand.TossEnd) hand.Rest();
        if (time < Play2BettingHand.PickupTime) return;
        if (!released)
        {
            chip.SetPositionAndRotation(hand.Contact, hand.PinchRotation);
            return;
        }
        if (aimed)
        {
            AnimateTargeted(chip, airTime);
            if (airTime < 1.02f) return;
        }
        else
        {
            if (!landed)
            {
                Vector3 goal = Vector3.Lerp(flightStart, Vector3.zero, .36f);
                Vector3 impulse = Vector3.ProjectOnPlane(goal - flightStart, Vector3.up) * 3.3f + Vector3.up * .34f;
                felt.Throw(flightStart, chip.localRotation, impulse, new Vector3(3.1f, 15f, -1.8f));
                chip.gameObject.SetActive(false);
                landed = true;
            }
        }
        if (time < Play2BettingHand.TossEnd) return;
        FinishBet();
    }

    private void FinishBet()
    {
        moving[activeSeat].gameObject.SetActive(false);
        if (action.allIn)
            foreach (var token in allInStack) token.gameObject.SetActive(false);
        hands[activeSeat].Rest();
        activeSeat = -1;
    }

    private void StartBet(HoldemHistoryEntryPacket entry)
    {
        action = entry;
        activeSeat = action.seat - 1;
        began = Time.time;
        landed = struck = released = false;
        FreeHand(activeSeat);
        Vector3 seat = transform.InverseTransformPoint(eyes[activeSeat].position);
        Vector3 outward = new Vector3(seat.x, 0, seat.z).normalized;
        Vector3 side = activeSeat + 1 == viewer
            ? transform.InverseTransformDirection(-viewCamera.right)
            : Vector3.Cross(Vector3.up, outward);
        int landingSlot = Mathf.Max(0, targetCount - 1);
        flightEnd = StackPosition(landingSlot);
        Vector3 pickup = outward * .54f + side * .15f + Vector3.up * PotPosition.y;
        bool aimed = action.chipAimSeat >= 1 && action.chipAimSeat <= eyes.Length && action.chipAimSeat != activeSeat + 1;
        Vector3 target = aimed ? transform.InverseTransformPoint(eyes[action.chipAimSeat - 1].position) : flightEnd;
        Vector3 direction = Vector3.ProjectOnPlane(target - pickup, Vector3.up).normalized;
        Vector3 release = pickup + direction * (aimed ? .13f : .11f) + Vector3.up * (aimed ? .125f : .067f);
        releaseYaw = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;
        Tint(moving[activeSeat].GetComponent<Renderer>(), 0, StackColor(landingSlot));
        if (action.allIn)
        {
            // Both hands straddle the columns, including on a one-chip all-in.
            pushChips = Mathf.Clamp(1 + (int)Math.Log(Math.Max(1L, action.amount), 1.5), 1, allInStack.Length);
            reachablePush = 1f;
            pushSide = side;
            pushStart = outward * .58f + Vector3.up * PotPosition.y;
            pushStop = Vector3.MoveTowards(pushStart, new Vector3(flightEnd.x, pushStart.y, flightEnd.z), .14f);
            hands[activeSeat].Begin(PushPalm(0f, false), PushPalm(PushEnd, false));
            for (int i = 0; i < allInStack.Length; i++) allInStack[i].gameObject.SetActive(i < pushChips);
        }
        else
        {
            hands[activeSeat].Begin(transform.TransformPoint(pickup), transform.TransformPoint(release));
            var chip = moving[activeSeat];
            chip.SetLocalPositionAndRotation(pickup, Quaternion.Euler(0f, releaseYaw, 0f));
            chip.gameObject.SetActive(true);
        }
    }

    private Transform FaceFrame(int target) => target + 1 == viewer ? viewCamera : heads[target];

    private void LocateStrike(int target)
    {
        if (target + 1 == viewer)
        {
            // The cheek is just beside the lens. Let the chip reach the edge of
            // the view and let only part of the reacting hand enter that same edge.
            var camera = viewCamera.GetComponent<Camera>();
            strikeAnchor = viewCamera.InverseTransformPoint(camera.ViewportToWorldPoint(new Vector3(.04f, .38f, .22f)));
            rubAnchor = viewCamera.InverseTransformPoint(camera.ViewportToWorldPoint(new Vector3(-.09f, .32f, .24f)));
            return;
        }
        var head = heads[target];
        Vector3 forward = Vector3.ProjectOnPlane(eyes[target].forward, Vector3.up).normalized;
        Vector3 left = Vector3.Cross(forward, Vector3.up);
        Vector3 center = head.position + Vector3.up * .042f;
        Vector3 side = (left + forward * .66f).normalized;
        Vector3 cheek = faces[target].Touch(center + side * .50f, center, center + side * .13f, .002f);
        Vector3 aim = Vector3.Lerp(cheek, center, .24f);
        Vector3 touch = faces[target].Touch(transform.TransformPoint(flightStart), aim, cheek, .013f);
        strikeAnchor = head.InverseTransformPoint(touch);
        rubAnchor = head.InverseTransformPoint(cheek);
    }

    // The same wager chip strikes the chosen seat, loses height, then is raked into the pot.
    private void AnimateTargeted(Transform chip, float airTime)
    {
        int target = action.chipAimSeat - 1;
        if (!struck)
        {
            var head = heads[target];
            Vector3 face = transform.InverseTransformPoint(head.position);
            hitPoint = transform.InverseTransformPoint(FaceFrame(target).TransformPoint(strikeAnchor));
            dropPoint = Vector3.ProjectOnPlane(face, Vector3.up).normalized * .48f + Vector3.up * PotPosition.y;
        }
        float approach = Mathf.Clamp01(airTime / .31f);
        if (approach < 1)
        {
            chip.localPosition = Vector3.Lerp(flightStart, hitPoint, approach)
                + Vector3.up * (.075f * 4f * approach * (1f - approach));
            chip.localRotation = Quaternion.Euler(42f * approach, releaseYaw + 150f * approach, 12f * approach)
                * Quaternion.Slerp(releaseTilt, Quaternion.identity, approach);
            return;
        }
        if (!struck)
        {
            struck = true;
            if (cheekSeat >= 0) hands[cheekSeat]?.Rest();
            cheekSeat = target;
            cheekStarted = Time.time;
            FreeHand(target).BeginCheek();
            Vector3 v = Vector3.ProjectOnPlane(dropPoint - hitPoint, Vector3.up) * 3.3f;
            felt.Throw(hitPoint, chip.localRotation, v + Vector3.up * .09f, new Vector3(5.5f, -9f, 3.5f));
            chip.gameObject.SetActive(false);
        }
        float kick = Mathf.Clamp01((airTime - .31f) / .32f);
        float weight = Mathf.Sin(kick * Mathf.PI) * (1f - kick);
        if (weight <= .001f) return;
        if (target + 1 == viewer)
        {
            flinching = viewCamera;
            beforeFlinch = flinching.localRotation;
            flinching.localRotation *= Quaternion.Euler(-2f * weight, 0, 2f * weight);
        }
        else
        {
            flinching = heads[target];
            beforeFlinch = flinching.localRotation;
            float sign = Vector3.Dot(eyes[target].right, eyes[activeSeat].position - eyes[target].position) < 0 ? -1f : 1f;
            flinching.localRotation *= Quaternion.Euler(-15f * weight, -6f * sign * weight, 8f * sign * weight);
        }
    }

    internal void RestoreFlinch()
    {
        if (flinching == null) return;
        flinching.localRotation = beforeFlinch;
        flinching = null;
    }

    internal void FitPushToArms(Play2CardGrip[] grips)
    {
        if (activeSeat < 0 || action?.allIn != true || activeSeat + 1 == viewer || landed) return;
        Vector3 travel = transform.TransformVector(pushStop - pushStart);
        reachablePush = hands[activeSeat].ReachablePush(PushPalm(0f, false), travel);
        if (grips[activeSeat] != null)
            reachablePush = Mathf.Min(reachablePush, grips[activeSeat].ReachablePush(PushPalm(0f, true), travel));
    }

    internal Vector3 PushPalm(float time, bool cardHand)
    {
        Vector3 center = Vector3.Lerp(pushStart, pushStop, PushProgress(time));
        Vector3 outward = Vector3.ProjectOnPlane(pushStart, Vector3.up).normalized;
        return transform.TransformPoint(center + pushSide * ((cardHand ? -1f : 1f) * (PushColumnOffset + .020f))
            + outward * .044f + Vector3.up * .012f);
    }

    private float PushColumnOffset => pushChips == 1 ? 0f : .054f;
    private float PushProgress(float time) => reachablePush * Mathf.SmoothStep(0f, 1f, (time - PushBegin) / (PushEnd - PushBegin));

    private void AnimatePush(float time)
    {
        float progress = PushProgress(time);
        Vector3 center = Vector3.Lerp(pushStart, pushStop, progress);
        if (!landed)
        {
            for (int i = 0; i < pushChips; i++)
            {
                var token = allInStack[i];
                Vector3 offset = pushSide * (i % 2 == 0 ? -PushColumnOffset : PushColumnOffset) + Vector3.up * (i / 2 * .0062f);
                token.SetLocalPositionAndRotation(center + offset, Quaternion.Euler(0f, StackYaw(i), 0f));
                if (time < PushEnd) continue;
                Vector3 direction = (pushStop - pushStart).normalized;
                float drift = Mathf.Sin(i * 2.4f + activeSeat);
                felt.Throw(token.localPosition, token.localRotation,
                    direction * (.35f + .04f * (i % 4)) + pushSide * (.115f * drift), Vector3.up * (drift * 9f));
                token.gameObject.SetActive(false);
            }
            if (time >= PushEnd) landed = true;
        }
        if (time <= PushEnd + .33f) hands[activeSeat].Push(PushPalm(time, false), time);
    }

    private int PileSize(long chips) => chips <= 0 ? 0 : Mathf.Clamp(1 + (int)Math.Log(chips, 1.6), 1, pile.Length);

    // A tie can show several hands for the same pot. Only a newly credited amount moves.
    internal void ShowPayout(int seat, long amount, bool animate)
    {
        int index = seat - 1;
        long increase = amount - paidBySeat[index];
        if (increase <= 0) return;
        paidBySeat[index] = amount;
        paidTotal += increase;
        int previousCount = targetCount;
        targetCount = PileSize(handPot - paidTotal);
        if (seat == matchWinner && paidTotal == handPot)
        {
            StartCoroutine(GatherLastPot(index, animate));
            return;
        }
        ShowPile(targetCount);
        if (animate) StartCoroutine(SlidePayout(index, Mathf.Clamp(previousCount - targetCount, 1, 3)));
    }

    private IEnumerator GatherLastPot(int index, bool animate)
    {
        Vector3 outward = Vector3.ProjectOnPlane(transform.InverseTransformPoint(eyes[index].position), Vector3.up).normalized;
        Vector3 side = Vector3.Cross(Vector3.up, outward);
        Vector3 edge = outward * .42f - side * .19f;
        int count = PileSize(handPot);
        int settledColumns = 0;
        ShowPile(count);
        float beganAt = Time.unscaledTime;
        float elapsed;
        do
        {
            elapsed = animate ? Time.unscaledTime - beganAt : 1f;
            for (int i = 0; i < count; i++)
            {
                float t = Mathf.SmoothStep(0, 1, (elapsed - (i / 6) * .12f) / .52f);
                Vector3 goal = edge + side * (i / 6 * .064f) + Vector3.up * StackPosition(i).y;
                pile[i].localPosition = Vector3.Lerp(StackPosition(i), goal, t);
            }
            // A stack makes one quiet contact when it reaches the winner's rail.
            if (animate && settledColumns * 6 < count && elapsed >= .52f + settledColumns * .12f)
            {
                sound.ChipLand(.72f);
                settledColumns++;
            }
            if (elapsed < .77f) yield return null;
        } while (elapsed < .77f);
    }

    private IEnumerator SlidePayout(int index, int count)
    {
        Vector3 outward = Vector3.ProjectOnPlane(transform.InverseTransformPoint(eyes[index].position), Vector3.up).normalized;
        Vector3 side = Vector3.Cross(Vector3.up, outward);
        Vector3 destination = outward * .56f + side * .10f;
        int first = index * 3;
        for (int i = 0; i < count; i++) allInStack[first + i].gameObject.SetActive(true);
        float start = Time.unscaledTime;
        float t;
        do
        {
            t = Mathf.Clamp01((Time.unscaledTime - start) / .58f);
            float slide = Mathf.SmoothStep(0f, 1f, t);
            for (int i = 0; i < count; i++)
            {
                var token = allInStack[first + i];
                Vector3 from = StackPosition(i);
                Vector3 to = destination + side * (i * .015f) + Vector3.up * from.y;
                token.localPosition = Vector3.Lerp(from, to, slide);
                token.localRotation = Quaternion.Euler(0, StackYaw(i) + slide * (7f + i * 4f), 0);
            }
            yield return null;
        } while (t < 1f);
        sound.ChipLand(.55f);
        for (int i = 0; i < count; i++) allInStack[first + i].gameObject.SetActive(false);
    }

    private void ShowPile(int count)
    {
        for (int i = 0; i < pile.Length; i++) pile[i].gameObject.SetActive(i < count);
    }

    private void ResetHands()
    {
        StopAllCoroutines();
        for (int i = 0; i < pile.Length; i++)
            if (pile[i] != null) pile[i].localPosition = StackPosition(i);
        RestoreFlinch();
        foreach (var token in allInStack) if (token != null) token.gameObject.SetActive(false);
        beats.Clear();
        felt?.Clear();
        cheekSeat = -1;
        FinishedSweep = RequestedSweep;
        sweeping = 0;
        activeSeat = -1;
        for (int i = 0; i < hands.Length; i++)
        {
            hands[i]?.Release();
            hands[i] = null;
            if (moving[i] != null) moving[i].gameObject.SetActive(false);
        }
    }

    private void OnDestroy()
    {
        // Either scene can unload first. Only release the objects this component owns.
        RestoreFlinch();
        foreach (var hand in hands) hand?.Release();
        felt?.Release();
    }
}
