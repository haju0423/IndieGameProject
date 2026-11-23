using System.Collections.Generic;
using UnityEngine;

internal sealed class Play2CardGrip
{
    internal const bool UsesLeftHand = false;
    private const float Flight = 0.36f;
    private const float Lift = .58f;
    private const float Peek = 1.47f;
    private const float SwitchCorner = .62f;
    private readonly Play2CardPeel[] peels;
    private const float Settle = .16f;
    private readonly Vector3 pocket;
    private const float GripHeight = 0.028f;
    private const float ViewWristBend = 53f * Mathf.Deg2Rad;
    private const float PutDown = 0.50f;
    private const float LowerArm = 0.30f;
    private const float FoldedScale = 1.10f;
    private readonly Transform upperArm, forearm, wrist, camera;
    private readonly Play2FingerRig fingers;
    private readonly Play2RightHandGrip heldGrip;
    private readonly Pose contact;
    private Pose activeContact;
    private float heldWeight;
    private float foldScale;
    private readonly Quaternion wristRest, upperRest, forearmRest;
    private readonly Vector3 shoulderLocal, elbowLocal;
    private readonly Vector3 outward, lateral, deck, tableCenter, foldPile;
    private readonly Quaternion deckRotation;
    private readonly float firstLaunch, gap, heldCardScale;
    private readonly bool openingPeek;
    private readonly System.Action peekSound;
    private bool heardPeek;
    private readonly CardVisual first, second;
    private readonly GameObject[] fronts;
    private readonly GameObject viewModel;
    private float foldStarted = -1f;
    private bool folded, revealed, waitingToMuck;
    private bool inspect;
    private float inspection;
    internal bool IsInspecting => inspect;
    internal bool RestingFaceUp => revealed && folded;
    internal void AddRevealedCards(Play2ShowdownCards display, int[] ids)
    {
        display.Add(first, ids[0]);
        display.Add(second, ids[1]);
    }

    internal void Inspect(bool value) => inspect = value && !folded && foldStarted < 0;

    internal bool Contains(Vector2 point, Camera view)
    {
        return ContainsCard(first, point, view) || ContainsCard(second, point, view);
    }

    private static bool ContainsCard(CardVisual card, Vector2 point, Camera view)
    {
        if (!card.gameObject.activeInHierarchy) return false;
        Vector2 min = new(float.PositiveInfinity, float.PositiveInfinity);
        Vector2 max = new(float.NegativeInfinity, float.NegativeInfinity);
        for (int i = 0; i < 4; i++)
        {
            Vector3 local = new((i % 2 == 0 ? -1 : 1) * .047f, 0, (i < 2 ? -1 : 1) * .066f);
            Vector3 projected = view.WorldToScreenPoint(card.transform.TransformPoint(local));
            if (projected.z <= 0) return false;
            min = Vector2.Min(min, projected);
            max = Vector2.Max(max, projected);
        }
        return Rect.MinMaxRect(min.x, min.y, max.x, max.y).Contains(point);
    }
    private Pose foldFrom, firstInGrip, secondInGrip;
    private Quaternion muckFacing, fanInGrip;
    private float viewSupport;
    private Vector2 bendAtRelease;

    internal static float Duration(float gap, bool peek) => gap + Flight + Settle + (peek ? Peek : 0f) + Lift;

    internal Play2CardGrip(GameObject player, Transform view, Transform table, Transform eye, Vector3 dealOrigin,
        CardVisual first, CardVisual second, float delay, float gap, bool openingPeek, Material curvedPaper, System.Action peekSound = null)
    {
        camera = view;
        this.first = first;
        this.second = second;
        fronts = new[] { first.transform.Find("Front").gameObject, second.transform.Find("Front").gameObject };
        heldCardScale = first.transform.localScale.x;
        this.gap = gap;
        this.openingPeek = openingPeek;
        this.peekSound = peekSound;
        if (openingPeek && view != null)
            peels = new[] { new Play2CardPeel(first.transform, curvedPaper), new Play2CardPeel(second.transform, curvedPaper) };
        firstLaunch = Time.time + delay;
        outward = Vector3.ProjectOnPlane(eye.position - table.position, Vector3.up).normalized;
        lateral = Vector3.Cross(outward, Vector3.up);
        tableCenter = table.position + Vector3.up * 0.045f;
        deck = dealOrigin;
        deckRotation = Quaternion.LookRotation(-outward) * (openingPeek
            ? Quaternion.AngleAxis(180f, Vector3.right) : Quaternion.AngleAxis(180f, Vector3.forward));
        muckFacing = deckRotation;
        pocket = table.position + outward * (openingPeek ? .42f : .50f) + lateral * (view != null ? .07f : .125f) + Vector3.up * .013f;
        foldPile = table.position + outward * 0.48f + lateral * 0.08f + Vector3.up * 0.006f;

        bool firstPerson = camera != null;
        if (firstPerson)
        {
            viewModel = Object.Instantiate(player, camera, false);
            if (player.TryGetComponent<Play2FingerRig>(out var originalRig))
                viewModel.GetComponent<Play2FingerRig>().BindViewCopy(originalRig);
            viewModel.name = "First Person Right Arm";
            viewModel.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            viewModel.transform.localScale = player.transform.lossyScale * .86f;
            viewModel.SetActive(true);
            player = viewModel;
        }
        var animator = player.GetComponent<Animator>();
        upperArm = animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
        forearm = animator.GetBoneTransform(HumanBodyBones.RightLowerArm);
        wrist = animator.GetBoneTransform(HumanBodyBones.RightHand);
        wristRest = wrist.localRotation;
        shoulderLocal = upperArm.localPosition;
        elbowLocal = forearm.localPosition;
        upperRest = upperArm.localRotation;
        forearmRest = forearm.localRotation;
        fingers = Play2FingerRig.Attach(animator);
        fingers.SetCardGrip(UsesLeftHand, 1f);
        Pose openPinch = fingers.CardContact(UsesLeftHand, wrist);
        fingers.PinchPacket(UsesLeftHand, wrist, 1f, heldCardScale);
        // Once the pads touch, their tiny separation is an unstable axis for the wrist.
        contact = new Pose(fingers.CardContact(UsesLeftHand, wrist).position, openPinch.rotation);
        activeContact = contact;
        fingers.SetCardGrip(UsesLeftHand, 0f);
        heldGrip = new Play2RightHandGrip(player, usePreviewPose: true);

        if (firstPerson)
        {
            animator.enabled = false;
            var armSkin = player.GetComponentInChildren<SkinnedMeshRenderer>();
            fingers.PrepareViewMesh(armSkin);
            ShowArmOnly(armSkin, upperArm);
            viewModel.SetActive(false);
        }
    }

    internal float PeekFocus
    {
        get
        {
            if (!openingPeek || folded || foldStarted >= 0f) return 0f;
            float atPocket = Time.time - firstLaunch - gap - Flight;
            return Ease(atPocket / .29f) * (1f - Ease((atPocket - Peek - Settle) / Lift));
        }
    }

    internal float ReachOverTable
    {
        get
        {
            if (folded) return 0f;
            if (foldStarted >= 0f)
            {
                float t = Time.time - foldStarted;
                return (revealed ? 1f : .32f) * Ease(t / .28f) * (1f - Ease((t - .45f) / .35f));
            }
            float dealt = Time.time - firstLaunch;
            return Ease(dealt / Flight) * (1f - Ease((dealt - gap - Flight - Settle - (openingPeek ? Peek : 0f)) / Lift));
        }
    }

    internal void UpdatePose(float standingRise = 0f, float pushTime = -1f, Vector3 pushPalm = default)
    {
        if (folded) return;
        if (foldStarted >= 0f)
        {
            UpdateFold(Time.time - foldStarted);
            return;
        }

        inspection = Mathf.MoveTowards(inspection, inspect ? 1 : 0, Time.deltaTime / .24f);
        float time = Time.time - firstLaunch;
        bool firstPerson = camera != null;
        bool visible = time >= 0f && (firstPerson || wrist.gameObject.activeInHierarchy);
        if (viewModel != null) viewModel.SetActive(visible);
        first.gameObject.SetActive(visible);
        second.gameObject.SetActive(visible && time >= gap);
        if (!visible) return;

        float reach = Ease(time / Flight);
        float atPocket = time - gap - Flight - Settle;
        float opening = Mathf.Clamp01((atPocket - (openingPeek ? Peek : 0f)) / Lift);
        float lift = Ease(opening);
        float curl = Ease((time - gap - Flight + .03f) / (Settle + .05f));
        // The thumb relaxes just enough to accept the second card.
        float secondCatch = 1f - Mathf.Abs((time - gap - Flight) / 0.12f);
        float thumbSlip = 0f;
        if (openingPeek && firstPerson && atPocket > 0f && atPocket < Peek)
        {
            float release = Mathf.Clamp01(atPocket / .34f);
            thumbSlip = .48f * Mathf.Sin(release * Mathf.PI);
            if (!heardPeek && release >= .5f) { heardPeek = true; peekSound?.Invoke(); }
        }
        fingers.SetCardGrip(UsesLeftHand, curl, Mathf.Max(thumbSlip, .22f * Ease(secondCatch)));

        float gathered = openingPeek ? Gather(atPocket) : 0f;
        fingers.PinchPacket(UsesLeftHand, wrist, firstPerson ? curl * Mathf.Max(gathered, lift) : curl, heldCardScale);
        float lower = pushTime >= 0f ? Ease(pushTime / .20f) * (1f - Ease((pushTime - 1.04f) / .26f)) : 0f;
        heldWeight = Ease((opening - .55f) / .45f) * (1f - lower);
        activeContact = new Pose(Vector3.Lerp(contact.position, heldGrip.Contact.position, heldWeight),
            Quaternion.Slerp(contact.rotation, heldGrip.Contact.rotation, heldWeight));
        heldGrip.BlendHeldPose(heldWeight);

        Vector3 gripPoint;
        Quaternion cardRotation;
        if (firstPerson)
        {
            Vector3 receive = pocket + outward * .051f + Vector3.up * .015f;
            if (openingPeek)
            {
                float follow = Ease((atPocket - SwitchCorner) / .26f);
                var nearEdge = new Vector3(.048f, 0f, .026f);
                Vector3 firstPad = PeekContact(0, nearEdge, atPocket, true);
                Vector3 secondPad = PeekContact(1, nearEdge, atPocket, true);
                receive = Vector3.Lerp(Vector3.Lerp(firstPad, secondPad, follow), TablePinch, gathered);
            }
            receive = Vector3.Lerp(camera.TransformPoint(new Vector3(.22f, -.37f, .33f)), receive, reach);
            Vector3 resting = Vector3.Lerp(new Vector3(.130f, -.241f, .415f), Play2RightHandGrip.PreviewViewPoint, heldWeight);
            Vector3 reading = new(.072f, -.166f, .364f);
            gripPoint = Vector3.Lerp(receive, camera.TransformPoint(Vector3.Lerp(resting, reading, Ease(inspection))), lift);
            float receivePitch = openingPeek ? -30f : -53f;
            Quaternion presented = Quaternion.Slerp(Quaternion.Euler(-86f, 2f, 0f), Play2RightHandGrip.PreviewViewFacing, heldWeight);
            Quaternion viewing = Quaternion.Slerp(presented, Quaternion.Euler(-89f, 2f, 0f), Ease(inspection));
            cardRotation = camera.rotation * Quaternion.Slerp(Quaternion.Euler(receivePitch, 2f, 0f), viewing, lift);
        }
        else
        {
            Vector3 catchPoint = pocket + outward * .05f + Vector3.up * .012f;
            Vector3 normalGrip = outward * .48f + lateral * .16f + Vector3.up * .058f;
            Vector3 previewGrip = outward * .60f + lateral * .14f + Vector3.up * .065f;
            Vector3 held = tableCenter + Vector3.Lerp(normalGrip, previewGrip, heldWeight) + Vector3.up * (standingRise * .62f);
            gripPoint = Vector3.Lerp(Vector3.Lerp(wrist.TransformPoint(activeContact.position), catchPoint, reach), held, lift);
            // The same seated player holds the fan toward their own chest in every view.
            Vector3 cover = Vector3.Lerp(outward - lateral * .06f + Vector3.up * .17f, outward + Vector3.up * .58f, heldWeight).normalized;
            Vector3 face = Vector3.Lerp(outward * .55f + Vector3.up * .83f, cover, lift);
            cardRotation = Quaternion.LookRotation(Vector3.ProjectOnPlane(Vector3.up, face), face);
        }

        // Turn the wrist edge-on to the fan so the knuckles wrap behind it.
        float wristTurn = lift * (1f - heldWeight);
        Quaternion wristCardFrame = firstPerson
            ? cardRotation * Quaternion.Euler(0f, -58f * wristTurn, -18f * wristTurn)
            : cardRotation;
        Quaternion wristRotation = wristCardFrame * Quaternion.Inverse(activeContact.rotation);
        if (!firstPerson) wristRotation = Quaternion.Slerp(wrist.rotation, wristRotation, reach);
        Vector3 pad = activeContact.position;
        if (openingPeek && firstPerson && lift < 1f)
        {
            // A side-on thumb can lift the paper without covering the printed corner.
            var shielding = fingers.PalmRotation(UsesLeftHand, -outward - lateral * .34f, Vector3.down + lateral * .15f);
            wristRotation = Quaternion.Slerp(shielding, wristRotation, lift);
            pad = Vector3.Lerp(fingers.FingerPad(UsesLeftHand, 0, wrist), activeContact.position, Mathf.Max(lift, gathered));
        }
        Vector3 wristTarget = gripPoint - wristRotation * Vector3.Scale(pad, wrist.lossyScale);
        PoseArm(wristTarget, wristRotation, reach, lift);

        if (openingPeek && firstPerson && time >= gap + Flight && lift < 1f)
        {
            float follow = Ease((atPocket - SwitchCorner) / .26f);
            // The index stays on the back of the sheet as the thumb turns its corner.
            var press = new Vector3(.016f, 0f, .005f);
            Vector3 support = Vector3.Lerp(PeekContact(0, press, atPocket, false),
                PeekContact(1, press, atPocket, false), follow);
            fingers.PressCardBack(UsesLeftHand, support, (1f - gathered) * Ease((time - gap - Flight) / Settle));
        }
        gripPoint = wrist.TransformPoint(activeContact.position);
        viewSupport = firstPerson ? lift * (1f - heldWeight) : 0f;
        if (viewSupport > 0f && pushTime < 0f)
            fingers.SupportFan(UsesLeftHand, gripPoint, cardRotation, heldCardScale, viewSupport);
        Quaternion gripRotation = firstPerson ? cardRotation : wrist.rotation * activeContact.rotation;
        MoveCard(first.transform, 0, time, opening, lift, gripPoint, gripRotation);
        if (time >= gap) MoveCard(second.transform, 1, time - gap, opening, lift, gripPoint, gripRotation);
        if (waitingToMuck && time >= gap + Flight) { BeginPutDown(); return; }
        if (pushTime >= 0f)
        {
            Quaternion down = fingers.PalmRotation(UsesLeftHand, -outward, Vector3.down);
            Quaternion heldWrist = wrist.rotation;
            Vector3 heldTarget = wrist.position;
            Vector3 cardPad = RestForPush(first.transform, 0, lower);
            RestForPush(second.transform, 1, lower);
            float atChips = Ease((pushTime - .18f) / .12f) * (1f - Ease((pushTime - .80f) / .21f));
            Vector3 pushPad = Vector3.Lerp(cardPad + outward * .025f + Vector3.up * .016f, pushPalm, atChips);
            Vector3 openTarget = pushPad - down * Vector3.Scale(fingers.PushContact(UsesLeftHand), wrist.lossyScale);
            fingers.SetCardGrip(UsesLeftHand, Mathf.Lerp(curl, .32f, lower));
            fingers.PinchPacket(UsesLeftHand, wrist, curl * (1f - lower), heldCardScale);
            heldGrip.BlendHeldPose(heldWeight);
            PoseArm(Vector3.Lerp(heldTarget, openTarget, lower), Quaternion.Slerp(heldWrist, down, lower), 1, lift, true);
            viewSupport *= 1f - lower;
            if (viewSupport > 0f)
                fingers.SupportFan(UsesLeftHand, wrist.TransformPoint(activeContact.position), cardRotation, heldCardScale, viewSupport);
        }
    }

    internal float ReachablePush(Vector3 startPad, Vector3 travel)
    {
        Quaternion down = fingers.PalmRotation(UsesLeftHand, -outward, Vector3.down);
        Vector3 offset = down * Vector3.Scale(fingers.PushContact(UsesLeftHand), wrist.lossyScale);
        return Play2TableArm.ReachablePush(upperArm, forearm, wrist, startPad, travel, offset);
    }

    private Vector3 RestForPush(Transform card, int index, float down)
    {
        var rest = FoldedCardPose(index);
        Vector3 position = rest.position + outward * .025f;
        card.SetPositionAndRotation(Vector3.Lerp(card.position, position, down),
            Quaternion.Slerp(card.rotation, rest.rotation, down));
        return position;
    }

    private void PoseArm(Vector3 wristTarget, Quaternion wristRotation, float reach, float lift, bool push = false)
    {
        Quaternion neutralForearm = wristRotation * Quaternion.Inverse(wristRest);
        Vector3 neutralDirection = neutralForearm * wrist.localPosition.normalized;
        if (viewModel != null)
        {
            upperArm.SetLocalPositionAndRotation(shoulderLocal, upperRest);
            forearm.SetLocalPositionAndRotation(elbowLocal, forearmRest);
            float elbowHeight = Mathf.Lerp(-0.58f, -0.40f, lift);
            Vector3 towardGrip = wristTarget - camera.TransformPoint(new Vector3(0.34f, elbowHeight, 0.20f));
            float bend = Mathf.Lerp(ViewWristBend, 40f * Mathf.Deg2Rad, heldWeight);
            Vector3 armDirection = push ? towardGrip.normalized : Vector3.RotateTowards(neutralDirection, towardGrip.normalized, bend, 0f);
            Quaternion armRotation = Quaternion.FromToRotation(neutralDirection, armDirection) * neutralForearm;
            // Move the whole skeleton so sleeve vertices blended with the upper arm stay with the forearm.
            Transform body = viewModel.transform;
            body.rotation = armRotation * Quaternion.Inverse(forearm.rotation) * body.rotation;
            body.position += wristTarget - wrist.position;
            Play2TableArm.AnchorViewSleeve(upperArm, forearm, camera, UsesLeftHand);
        }
        else
        {
            Play2TableArm.Reach(upperArm, forearm, wrist, wristTarget);
        }
        wrist.rotation = wristRotation;
    }

    internal void Fold(bool animate = true)
    {
        if (folded || foldStarted >= 0f || waitingToMuck) return;
        if (!animate || (viewModel == null && !wrist.gameObject.activeInHierarchy))
        {
            FinishFold();
            return;
        }
        if (Time.time < firstLaunch + gap + Flight || !second.gameObject.activeSelf)
        {
            waitingToMuck = true;
            return;
        }
        BeginPutDown();
    }

    private void BeginPutDown()
    {
        if (peels != null) bendAtRelease = new Vector2(peels[0].Bend, peels[1].Bend);
        waitingToMuck = false;
        foldStarted = Time.time;
        foldScale = first.transform.localScale.x;
        foldFrom = new Pose(wrist.TransformPoint(activeContact.position), wrist.rotation * activeContact.rotation);
        firstInGrip = RelativeToFoldGrip(first.transform);
        secondInGrip = RelativeToFoldGrip(second.transform);
        fanInGrip = Quaternion.Slerp(firstInGrip.rotation, secondInGrip.rotation, .5f);
        var heading = Vector3.ProjectOnPlane(first.transform.forward, Vector3.up);
        if (heading.sqrMagnitude < .12f) heading = -outward;
        muckFacing = Quaternion.LookRotation(heading.normalized, Vector3.down);
    }

    internal void Reveal(int firstId, int secondId, bool animate = true)
    {
        if (revealed || folded || foldStarted >= 0f) return;
        revealed = true;
        Play2TablePreview.SetCardValue(first, firstId);
        Play2TablePreview.SetCardValue(second, secondId);
        first.transform.Find("Front").gameObject.SetActive(true);
        second.transform.Find("Front").gameObject.SetActive(true);
        Fold(animate);
    }

    private Pose RelativeToFoldGrip(Transform card)
    {
        Quaternion inverse = Quaternion.Inverse(foldFrom.rotation);
        return new Pose(inverse * (card.position - foldFrom.position), inverse * card.rotation);
    }

    private void UpdateFold(float time)
    {
        bool firstPerson = viewModel != null;
        if (!wrist.gameObject.activeInHierarchy || time >= PutDown + LowerArm)
        {
            FinishFold();
            return;
        }

        if (bendAtRelease != Vector2.zero)
        {
            float remaining = 1f - Ease(time / .18f);
            peels[0].SetBend(bendAtRelease.x * remaining);
            peels[1].SetBend(bendAtRelease.y * remaining);
            if (remaining <= 0f) bendAtRelease = Vector2.zero;
        }

        if (!revealed)
        {
            Muck(time, firstPerson);
            return;
        }
        float down = Ease(time / PutDown);
        float release = Ease((time - .34f) / .16f);
        float retreat = Ease((time - PutDown) / LowerArm);
        Vector3 placeGrip = foldPile + outward * .035f + Vector3.up * .032f;
        Quaternion gripRotation = Quaternion.Slerp(foldFrom.rotation, Quaternion.LookRotation(-outward), down);
        Quaternion wristRotation = gripRotation * Quaternion.Inverse(activeContact.rotation);
        Vector3 wristTarget = Vector3.Lerp(foldFrom.position, placeGrip, down) -
            wristRotation * Vector3.Scale(activeContact.position, wrist.lossyScale);
        Vector3 idle = firstPerson ? camera.TransformPoint(new Vector3(.16f, -.43f, .35f)) : wrist.position;
        Quaternion idleRotation = firstPerson ? foldFrom.rotation * Quaternion.Inverse(activeContact.rotation) : wrist.rotation;
        fingers.SetCardGrip(UsesLeftHand, 1f - release);
        fingers.PinchPacket(UsesLeftHand, wrist, 1f - release, heldCardScale);
        heldGrip.BlendHeldPose(heldWeight * (1f - release));
        PoseArm(Vector3.Lerp(wristTarget, idle, retreat), Quaternion.Slerp(wristRotation, idleRotation, retreat),
            1f - retreat, 1f - down);
        Vector3 actualGrip = wrist.TransformPoint(activeContact.position);
        Quaternion actualRotation = wrist.rotation * activeContact.rotation;
        if (viewSupport > 0f && release < 1f)
            fingers.SupportFan(UsesLeftHand, actualGrip, actualRotation * fanInGrip, heldCardScale, viewSupport * (1f - release));
        UpdateFoldingCard(first, firstInGrip, 0, actualGrip, actualRotation, release);
        UpdateFoldingCard(second, secondInGrip, 1, actualGrip, actualRotation, release);
    }

    private void Muck(float time, bool firstPerson)
    {
        float lower = Ease(time / .32f);
        float push = Mathf.Clamp01((time - .32f) / .24f);
        // The fingers accelerate the packet. The last few centimetres slide freely on the felt.
        float slide = push < .5f ? 2f * push * push : 1f - 2f * (1f - push) * (1f - push);
        float release = Ease((time - .36f) / .11f);
        float retreat = Ease((time - .44f) / .31f);
        Pose top = FoldedCardPose(1);
        Vector3 approach = top.position + outward * .085f;
        Vector3 fingerPad = approach - outward * (.085f * Mathf.Min(slide, .5f))
            + outward * .040f + Vector3.up * .012f;
        Quaternion heldWrist = foldFrom.rotation * Quaternion.Inverse(activeContact.rotation);
        Quaternion flatWrist = fingers.PalmRotation(UsesLeftHand, -outward, Vector3.down);
        Quaternion wristRotation = Quaternion.Slerp(heldWrist, flatWrist, lower);
        Vector3 start = foldFrom.position - heldWrist * Vector3.Scale(activeContact.position, wrist.lossyScale);
        Vector3 atFelt = fingerPad - flatWrist * Vector3.Scale(fingers.PushContact(UsesLeftHand), wrist.lossyScale);
        Vector3 target = Vector3.Lerp(start, atFelt, lower);
        target.y += .022f * Mathf.Sin(Mathf.PI * lower);

        // The animator supplies the remote hand's resting pose each frame.
        Vector3 rest = firstPerson ? camera.TransformPoint(new Vector3(.24f, -.43f, .16f)) : wrist.position;
        Quaternion restRotation = firstPerson ? flatWrist : wrist.rotation;
        Vector3 leaving = Vector3.Lerp(target, rest, retreat);
        leaving.y += .020f * Mathf.Sin(Mathf.PI * retreat);
        PoseArm(leaving, Quaternion.Slerp(wristRotation, restRotation, retreat), 1f - retreat, 1f - lower, true);
        fingers.SetCardGrip(UsesLeftHand, (1f - .65f * lower) * (1f - release));
        fingers.PinchPacket(UsesLeftHand, wrist, 1f - lower, heldCardScale);
        heldGrip.BlendHeldPose(heldWeight * (1f - lower));
        Vector3 pinch = wrist.TransformPoint(activeContact.position);
        Quaternion facing = wrist.rotation * activeContact.rotation;
        if (viewSupport > 0f && lower < 1f)
            fingers.SupportFan(UsesLeftHand, pinch, facing * fanInGrip, heldCardScale, viewSupport * (1f - lower));
        PlaceMuckedCard(first, firstInGrip, 0, pinch, facing, lower, slide);
        PlaceMuckedCard(second, secondInGrip, 1, pinch, facing, lower, slide);
    }

    private void PlaceMuckedCard(CardVisual card, Pose held, int index, Vector3 pinch, Quaternion rotation, float lower, float slide)
    {
        Pose rest = FoldedCardPose(index);
        Quaternion face = Quaternion.Slerp(rotation * held.rotation, rest.rotation, lower);
        Vector3 center = Vector3.Lerp(pinch + rotation * held.position, rest.position + outward * (.085f * (1f - slide)), lower);
        float scale = Mathf.Lerp(foldScale, FoldedScale, lower);
        float cornerHeight = scale * (.047f * Mathf.Abs((face * Vector3.right).y) + .066f * Mathf.Abs((face * Vector3.forward).y));
        center.y = Mathf.Max(center.y, rest.position.y + cornerHeight);
        card.transform.SetPositionAndRotation(center, face);
        card.transform.localScale = Vector3.one * scale;
        if (lower >= 1f) fronts[index].SetActive(false);
    }

    private void UpdateFoldingCard(CardVisual card, Pose inGrip, int index, Vector3 grip, Quaternion rotation, float release)
    {
        Pose placed = FoldedCardPose(index);
        Vector3 held = grip + rotation * inGrip.position;
        card.transform.SetPositionAndRotation(Vector3.Lerp(held, placed.position, release),
            Quaternion.Slerp(rotation * inGrip.rotation, placed.rotation, release));
        card.transform.localScale = Vector3.one * Mathf.Lerp(foldScale, FoldedScale, release);
        if (revealed || release >= .10f) card.transform.Find("Front").gameObject.SetActive(revealed);
    }

    private Pose FoldedCardPose(int index)
    {
        if (revealed)
        {
            Vector3 shown = foldPile - lateral * .08f + outward * .027f
                + lateral * (index == 0 ? -.053f : .053f) + Vector3.up * .002f;
            return new Pose(shown, Quaternion.LookRotation(-outward));
        }
        // Offset the top card just enough for both backs to read as separate cards.
        float spread = camera != null ? .025f : .036f;
        Vector3 position = foldPile + lateral * ((camera != null ? 0f : .02f) + index * spread)
            + (outward * .005f + Vector3.up * .0045f) * index;
        Quaternion rotation = muckFacing * Quaternion.AngleAxis(index == 0 ? -4f : 5f, Vector3.up);
        return new Pose(position, rotation);
    }

    private void FinishFold()
    {
        if (peels != null) foreach (var peel in peels) peel.SetBend(0f);
        PutCardFaceDown(first, 0);
        PutCardFaceDown(second, 1);
        fingers.SetCardGrip(UsesLeftHand, 0f);
        heldGrip.BlendHeldPose(0f);
        if (viewModel != null) viewModel.SetActive(false);
        folded = true;
    }

    private void PutCardFaceDown(CardVisual card, int index)
    {
        Pose pose = FoldedCardPose(index);
        card.transform.SetPositionAndRotation(pose.position, pose.rotation);
        card.transform.localScale = Vector3.one * FoldedScale;
        card.transform.Find("Front").gameObject.SetActive(revealed);
        card.gameObject.SetActive(true);
    }

    private void MoveCard(Transform card, int index, float time, float opening, float fan, Vector3 gripPoint, Quaternion facing)
    {
        float travel = Ease(time / Flight);
        bool firstPerson = camera != null;
        float layoutWeight = heldWeight * (firstPerson ? 1f - Ease(inspection) : 1f);
        float previewScale = (firstPerson ? Play2RightHandGrip.PreviewViewCardScale : Play2RightHandGrip.PreviewOpponentCardScale) * heldGrip.PreviewScale;
        float scale = Mathf.Lerp(heldCardScale, previewScale, layoutWeight);
        card.localScale = Vector3.one * scale;
        float spread = firstPerson ? Mathf.Lerp(Play2RightHandGrip.PreviewSpread, 18f, inspection) : Mathf.Lerp(11f, Play2RightHandGrip.PreviewSpread, heldWeight);
        int side = index == 0 ? -1 : 1;
        var preview = Play2RightHandGrip.FanCardPose(gripPoint, facing, spread * fan, side, scale, heldGrip.PreviewScale);
        Quaternion rotation = preview.rotation;
        // Both lower corners pivot at the same pinch. Spread the indices at the top of the fan.
        Vector3 anchor = camera != null ? new Vector3(-(index == 0 ? .014f : .032f) * fan, 0f, GripHeight)
            : new Vector3(-.018f, 0f, .049f);
        float depth = (index - .5f) * .0035f;
        Vector3 held = gripPoint + rotation * (anchor * heldCardScale) + facing * Vector3.up * (depth * heldCardScale);
        held = Vector3.Lerp(held, preview.position, heldWeight);
        Vector3 landing = Landing(index);
        Quaternion back = BackFacing(index);
        if (time < Flight)
        {
            Vector3 slide = Vector3.Lerp(deck, landing, travel) + Vector3.up * (Mathf.Sin(travel * Mathf.PI) * .031f);
            card.SetPositionAndRotation(slide, Quaternion.Slerp(deckRotation, back, travel));
            return;
        }
        float pickup = Ease(opening);
        if (peels != null)
        {
            float sinceLanding = Time.time - firstLaunch - gap - Flight - Settle;
            peels[index].SetBend(waitingToMuck || revealed ? 0f : CornerAmount(index, sinceLanding));
        }
        Quaternion turning = Quaternion.Slerp(back, rotation, pickup);
        if (peels != null)
        {
            // Square the bottom edges on the felt before lifting. Once gripped,
            // rotate the pair around the fingers so it cannot lag behind the wrist.
            float gathered = Gather(Time.time - firstLaunch - gap - Flight - Settle);
            Vector3 offset = new Vector3(index == 0 ? -.014f : -.032f, (index - .5f) * .0035f, GripHeight) * heldCardScale;
            Vector3 pivot = opening > 0f ? gripPoint : TablePinch;
            Vector3 mainGrip = pivot + turning * offset;
            card.SetPositionAndRotation(Vector3.Lerp(landing, Vector3.Lerp(mainGrip, preview.position, heldWeight), gathered), turning);
        }
        else card.SetPositionAndRotation(Vector3.Lerp(landing, held, pickup), turning);
        if (camera != null)
        {
            bool readable = !waitingToMuck && opening >= .16f;
            if (fronts[index].activeSelf != readable) fronts[index].SetActive(readable);
        }
    }

    private Vector3 PeekContact(int index, Vector3 point, float elapsed, bool thumb)
    {
        Quaternion facing = BackFacing(index);
        Vector3 bent = Play2CardPeel.BentPoint(point, CornerAmount(index, elapsed), out var normal);
        Vector3 pad = Landing(index) + facing * ((bent + normal * (thumb ? .0055f : -.008f)) * heldCardScale);
        pad.y = Mathf.Max(pad.y, Landing(index).y + .006f);
        return pad;
    }

    private Vector3 TablePinch => pocket + deckRotation * (new Vector3(.023f, 0f, -GripHeight) * heldCardScale)
        + Vector3.up * .003f;
    private static float Gather(float elapsed) => Ease((elapsed - 1.08f) / .36f);

    private static float CornerAmount(int index, float elapsed)
    {
        float lift = Ease((elapsed - index * SwitchCorner) / .29f);
        return lift * (index == 0 ? 1f - Ease((elapsed - SwitchCorner) / .24f) : 1f - Gather(elapsed));
    }

    private Vector3 Landing(int index) => pocket + lateral * ((index == 0 ? -1f : 1f) * (openingPeek ? .057f : .027f))
        + Vector3.up * (.005f * index);
    private Quaternion BackFacing(int index) => deckRotation * Quaternion.AngleAxis((index == 0 ? -1f : 1f) * (openingPeek ? 5f : 7f), Vector3.up);

    private static float Ease(float value) => Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(value));

    internal static void ShowArmOnly(SkinnedMeshRenderer skin, Transform upperArm)
    {
        var mesh = skin.sharedMesh;
        var bones = skin.bones;
        var arm = new bool[bones.Length];
        for (int i = 0; i < arm.Length; i++)
            arm[i] = bones[i].IsChildOf(upperArm);

        var counts = mesh.GetBonesPerVertex().ToArray();
        var weights = mesh.GetAllBoneWeights().ToArray();
        var visible = new bool[mesh.vertexCount];
        int offset = 0;
        for (int i = 0; i < visible.Length; i++)
        {
            float armWeight = 0f;
            for (int j = 0; j < counts[i]; j++)
            {
                var weight = weights[offset++];
                if (arm[weight.boneIndex]) armWeight += weight.weight;
            }
            visible[i] = armWeight > 0.40f;
        }

        for (int submesh = 0; submesh < mesh.subMeshCount; submesh++)
        {
            var indices = mesh.GetTriangles(submesh);
            var kept = new List<int>();
            for (int i = 0; i < indices.Length; i += 3)
            {
                if (!visible[indices[i]] || !visible[indices[i + 1]] || !visible[indices[i + 2]]) continue;
                kept.Add(indices[i]);
                kept.Add(indices[i + 1]);
                kept.Add(indices[i + 2]);
            }
            mesh.SetTriangles(kept, submesh, false);
        }
        skin.updateWhenOffscreen = true;
        skin.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }

    internal void Release()
    {
        if (peels != null) foreach (var peel in peels) peel.Release();
        if (viewModel != null) Object.Destroy(viewModel);
        else
        {
            fingers.SetCardGrip(UsesLeftHand, 0f);
            heldGrip.BlendHeldPose(0f);
        }
    }
}
