using System.Collections.Generic;
using UnityEngine;

internal sealed class Play2CardGrip
{
    private const float Flight = 0.36f;
    private const float Lift = 0.53f;
    private const float GripHeight = 0.044f;
    private const float ViewWristBend = 40f * Mathf.Deg2Rad;
    private const float PutDown = 0.50f;
    private const float LowerArm = 0.30f;
    private const float FoldedScale = 1.10f;
    private readonly Transform upperArm, forearm, wrist, camera;
    private readonly Play2FingerRig fingers;
    private readonly Pose contact;
    private readonly Quaternion wristRest;
    private readonly Vector3 outward, lateral, deck, tableCenter, foldPile;
    private readonly Quaternion deckRotation, foldRotation;
    private readonly float firstLaunch, gap, heldCardScale;
    private readonly bool openingPeek;
    private readonly System.Action peekSound;
    private bool heardPeek;
    private readonly CardVisual first, second;
    private readonly GameObject viewModel;
    private float foldStarted = -1f;
    private bool folded, revealed;
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

    internal static float Duration(float gap) => gap + Flight + Lift;

    internal Play2CardGrip(GameObject player, Transform view, Transform table, Transform eye, Vector3 dealOrigin,
        CardVisual first, CardVisual second, float delay, float gap, bool openingPeek, System.Action peekSound = null)
    {
        camera = view;
        this.first = first;
        this.second = second;
        heldCardScale = first.transform.localScale.x;
        this.gap = gap;
        this.openingPeek = openingPeek;
        this.peekSound = peekSound;
        firstLaunch = Time.time + delay;
        outward = Vector3.ProjectOnPlane(eye.position - table.position, Vector3.up).normalized;
        lateral = Vector3.Cross(outward, Vector3.up);
        tableCenter = table.position + Vector3.up * 0.045f;
        deck = dealOrigin;
        deckRotation = Quaternion.LookRotation(-outward) * Quaternion.Euler(0f, 0f, 180f);
        foldRotation = deckRotation * Quaternion.AngleAxis(90f, Vector3.up);
        foldPile = table.position + outward * 0.48f + lateral * 0.08f + Vector3.up * 0.006f;

        bool left = camera != null;
        if (left)
        {
            viewModel = Object.Instantiate(player, camera, false);
            if (player.TryGetComponent<Play2FingerRig>(out var originalRig))
                viewModel.GetComponent<Play2FingerRig>().BindViewCopy(originalRig);
            viewModel.name = "First Person Left Arm";
            viewModel.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            viewModel.transform.localScale = player.transform.lossyScale;
            viewModel.SetActive(true);
            player = viewModel;
        }
        var animator = player.GetComponent<Animator>();
        upperArm = animator.GetBoneTransform(left ? HumanBodyBones.LeftUpperArm : HumanBodyBones.RightUpperArm);
        forearm = animator.GetBoneTransform(left ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm);
        wrist = animator.GetBoneTransform(left ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
        wristRest = wrist.localRotation;
        fingers = Play2FingerRig.Attach(animator);
        fingers.SetCardGrip(left, 1f);
        contact = fingers.CardContact(left, wrist);
        fingers.SetCardGrip(left, 0f);

        if (left)
        {
            animator.enabled = false;
            ShowArmOnly(player.GetComponentInChildren<SkinnedMeshRenderer>(), upperArm);
            viewModel.SetActive(false);
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
        bool left = camera != null;
        bool visible = time >= 0f && (left || wrist.gameObject.activeInHierarchy);
        if (viewModel != null) viewModel.SetActive(visible);
        first.gameObject.SetActive(visible);
        second.gameObject.SetActive(visible && time >= gap);
        if (!visible) return;

        float reach = Ease(time / Flight);
        float opening = Mathf.Clamp01((time - gap - Flight) / Lift);
        float lift = Ease(opening);
        float curl = Ease((time - Flight + 0.11f) / 0.20f);
        // The thumb relaxes just enough to accept the second card.
        float secondCatch = 1f - Mathf.Abs((time - gap - Flight) / 0.12f);
        float thumbSlip = 0f;
        if (openingPeek && left && opening > 0f && opening < 1f)
        {
            float release = Mathf.Clamp01((opening - .34f) / .26f);
            thumbSlip = .48f * Mathf.Sin(release * Mathf.PI);
            if (!heardPeek && release >= .5f) { heardPeek = true; peekSound?.Invoke(); }
        }
        fingers.SetCardGrip(left, curl, Mathf.Max(thumbSlip, .22f * Ease(secondCatch)));

        Vector3 gripPoint;
        Quaternion cardRotation;
        if (left)
        {
            Vector3 receive = Vector3.Lerp(new Vector3(-0.155f, -0.275f, 0.43f),
                new Vector3(-0.128f, -0.198f, 0.57f), reach);
            Vector3 resting = new(-.097f, -.219f, .46f);
            Vector3 reading = new(-.055f, -.190f, .43f);
            gripPoint = camera.TransformPoint(Vector3.Lerp(receive, Vector3.Lerp(resting, reading, Ease(inspection)), lift));
            float receivePitch = openingPeek ? -30f : -53f;
            cardRotation = camera.rotation * Quaternion.Euler(Mathf.Lerp(receivePitch, Mathf.Lerp(-86f, -89f, inspection), lift), -2f, 0f);
        }
        else
        {
            Vector3 catchPoint = tableCenter + outward * 0.49f + lateral * 0.14f;
            Vector3 held = tableCenter + outward * 0.48f + lateral * 0.16f + Vector3.up * (0.058f + standingRise * .62f);
            gripPoint = Vector3.Lerp(Vector3.Lerp(wrist.TransformPoint(contact.position), catchPoint, reach), held, lift);
            Vector3 face = Vector3.Lerp(outward * 0.55f + Vector3.up * 0.83f, outward + Vector3.up * 0.22f, lift);
            cardRotation = Quaternion.LookRotation(Vector3.ProjectOnPlane(Vector3.up, face), face);
        }

        Quaternion wristRotation = cardRotation * Quaternion.Inverse(contact.rotation);
        if (!left) wristRotation = Quaternion.Slerp(wrist.rotation, wristRotation, reach);
        Vector3 wristTarget = gripPoint - wristRotation * Vector3.Scale(contact.position, wrist.lossyScale);
        PoseArm(wristTarget, wristRotation, reach, lift);

        gripPoint = wrist.TransformPoint(contact.position);
        Quaternion gripRotation = wrist.rotation * contact.rotation;
        MoveCard(first.transform, 0, time, opening, lift, gripPoint, gripRotation);
        if (time >= gap) MoveCard(second.transform, 1, time - gap, opening, lift, gripPoint, gripRotation);
        if (pushTime >= 0f)
        {
            float lower = Ease(pushTime / .15f);
            float liftBack = Ease((pushTime - .74f) / .22f);
            float down = lower * (1f - liftBack);
            // Rest the still-private cards at the seat while the open palm pushes. This is not a fold.
            RestForPush(first.transform, 0, down);
            RestForPush(second.transform, 1, down);
            float palmWeight = Ease((pushTime - .12f) / .10f) * (1f - liftBack);
            var palmRotation = fingers.PointingRotation(left, -outward);
            var palmTarget = pushPalm - palmRotation * Vector3.Scale(contact.position, wrist.lossyScale);
            fingers.SetCardGrip(left, Mathf.Lerp(curl, .14f, palmWeight));
            PoseArm(Vector3.Lerp(wrist.position, palmTarget, palmWeight), Quaternion.Slerp(wrist.rotation, palmRotation, palmWeight), 1, lift, true);
        }
    }

    private void RestForPush(Transform card, int index, float down)
    {
        var rest = FoldedCardPose(index);
        Vector3 position = rest.position + outward * .09f;
        card.SetPositionAndRotation(Vector3.Lerp(card.position, position, down),
            Quaternion.Slerp(card.rotation, rest.rotation, down));
    }

    private void PoseArm(Vector3 wristTarget, Quaternion wristRotation, float reach, float lift, bool push = false)
    {
        Quaternion neutralForearm = wristRotation * Quaternion.Inverse(wristRest);
        Vector3 neutralDirection = neutralForearm * wrist.localPosition.normalized;
        if (viewModel != null)
        {
            float elbowHeight = Mathf.Lerp(-0.58f, -0.40f, lift);
            Vector3 towardGrip = wristTarget - camera.TransformPoint(new Vector3(-0.34f, elbowHeight, 0.20f));
            Vector3 armDirection = push ? towardGrip.normalized : Vector3.RotateTowards(neutralDirection, towardGrip.normalized, ViewWristBend, 0f);
            Quaternion armRotation = Quaternion.FromToRotation(neutralDirection, armDirection) * neutralForearm;
            // Move the whole skeleton so sleeve vertices blended with the upper arm stay with the forearm.
            Transform body = viewModel.transform;
            body.rotation = armRotation * Quaternion.Inverse(forearm.rotation) * body.rotation;
            body.position += wristTarget - wrist.position;
        }
        else
        {
            Play2TableArm.Reach(upperArm, forearm, wrist, wristTarget);
        }
        wrist.rotation = wristRotation;
    }

    internal void Fold()
    {
        if (folded || foldStarted >= 0f) return;
        if (!first.gameObject.activeSelf || !second.gameObject.activeSelf)
        {
            FinishFold();
            return;
        }

        foldStarted = Time.time;
        foldFrom = new Pose(wrist.TransformPoint(contact.position), wrist.rotation * contact.rotation);
        firstInGrip = RelativeToFoldGrip(first.transform);
        secondInGrip = RelativeToFoldGrip(second.transform);
    }

    internal void Reveal(int firstId, int secondId)
    {
        if (revealed || folded || foldStarted >= 0f) return;
        revealed = true;
        Play2TablePreview.SetCardValue(first, firstId);
        Play2TablePreview.SetCardValue(second, secondId);
        first.transform.Find("Front").gameObject.SetActive(true);
        second.transform.Find("Front").gameObject.SetActive(true);
        Fold();
    }

    private Pose RelativeToFoldGrip(Transform card)
    {
        Quaternion inverse = Quaternion.Inverse(foldFrom.rotation);
        return new Pose(inverse * (card.position - foldFrom.position), inverse * card.rotation);
    }

    private void UpdateFold(float time)
    {
        bool left = viewModel != null;
        if (!wrist.gameObject.activeInHierarchy || time >= PutDown + LowerArm)
        {
            FinishFold();
            return;
        }

        float down = Ease(time / PutDown);
        float release = Ease((time - 0.34f) / (PutDown - 0.34f));
        float retreat = Ease((time - PutDown) / LowerArm);
        Vector3 placeGrip = foldPile + outward * 0.035f + Vector3.up * 0.032f;
        Quaternion placedRotation = revealed ? Quaternion.LookRotation(-outward) : foldRotation;
        Quaternion gripRotation = Quaternion.Slerp(foldFrom.rotation, placedRotation, down);
        Quaternion wristRotation = gripRotation * Quaternion.Inverse(contact.rotation);
        Vector3 gripPoint = Vector3.Lerp(foldFrom.position, placeGrip, down);
        Vector3 wristTarget = gripPoint - wristRotation * Vector3.Scale(contact.position, wrist.lossyScale);
        Vector3 idle = left ? camera.TransformPoint(new Vector3(-0.16f, -0.43f, 0.35f)) : wrist.position;
        Quaternion idleRotation = left ? foldFrom.rotation * Quaternion.Inverse(contact.rotation) : wrist.rotation;

        fingers.SetCardGrip(left, 1f - release);
        PoseArm(Vector3.Lerp(wristTarget, idle, retreat), Quaternion.Slerp(wristRotation, idleRotation, retreat),
            1f - retreat, 1f - down);

        Vector3 actualGrip = wrist.TransformPoint(contact.position);
        UpdateFoldingCard(first, firstInGrip, 0, actualGrip, gripRotation, release);
        UpdateFoldingCard(second, secondInGrip, 1, actualGrip, gripRotation, release);
    }

    private void UpdateFoldingCard(CardVisual card, Pose inGrip, int index, Vector3 grip, Quaternion rotation, float release)
    {
        Pose placed = FoldedCardPose(index);
        Vector3 held = grip + rotation * inGrip.position;
        card.transform.SetPositionAndRotation(Vector3.Lerp(held, placed.position, release),
            Quaternion.Slerp(rotation * inGrip.rotation, placed.rotation, release));
        card.transform.localScale = Vector3.one * Mathf.Lerp(heldCardScale, FoldedScale, release);
        if (release >= 1f) card.transform.Find("Front").gameObject.SetActive(revealed);
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
        Vector3 position = foldPile + lateral * (index == 0 ? -0.044f : 0.044f)
            + (outward * 0.006f + Vector3.up * 0.0045f) * index;
        Quaternion rotation = foldRotation * Quaternion.AngleAxis(index == 0 ? -8f : 7f, Vector3.up);
        return new Pose(position, rotation);
    }

    private void FinishFold()
    {
        PutCardFaceDown(first, 0);
        PutCardFaceDown(second, 1);
        fingers.SetCardGrip(viewModel != null, 0f);
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
        float spread = camera != null ? Mathf.Lerp(12f, 18f, inspection) : 18f;
        float angle = index == 0 ? -spread : spread;
        Quaternion rotation = facing * Quaternion.AngleAxis(angle * fan, Vector3.up);
        if (openingPeek && opening < 1f)
        {
            // Both cards hinge at the pinch. The second corner follows the first under the thumb.
            float start = .12f + index * .28f;
            float turn = Mathf.Clamp01((opening - start) / (1f - start));
            float hinge = Ease(turn);
            // The thumb holds the corner down, then releases it with a short paper snap.
            float pitch = 112f * (1f - hinge) - 5f * Mathf.Sin(Mathf.PI * turn) * hinge;
            rotation *= Quaternion.AngleAxis(pitch, Vector3.right);
        }
        Vector3 held = gripPoint + rotation * new Vector3(0f, index * 0.0035f - 0.0015f, GripHeight * card.localScale.z);
        float separation = camera != null ? .014f : .010f;
        held += facing * Vector3.right * ((index == 0 ? -separation : separation) * fan);
        Vector3 flight = Vector3.Lerp(deck, held, travel) + Vector3.up * (Mathf.Sin(travel * Mathf.PI) * 0.075f);
        card.SetPositionAndRotation(flight, Quaternion.Slerp(deckRotation, rotation, travel));
    }

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
        if (viewModel != null) Object.Destroy(viewModel);
        else fingers.SetCardGrip(false, 0f);
    }
}
