using System.Collections.Generic;
using UnityEngine;

internal sealed class Play2CardGrip
{
    private const float Flight = 0.36f;
    private const float Lift = 0.40f;
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
    private readonly CardVisual first, second;
    private readonly GameObject viewModel;
    private float foldStarted = -1f;
    private bool folded;
    private Pose foldFrom, firstInGrip, secondInGrip;

    internal static float Duration(float gap) => gap + Flight + Lift;

    internal Play2CardGrip(GameObject player, Transform view, Transform table, Transform eye, Vector3 dealOrigin,
        CardVisual first, CardVisual second, float delay, float gap)
    {
        camera = view;
        this.first = first;
        this.second = second;
        heldCardScale = first.transform.localScale.x;
        this.gap = gap;
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

    internal void UpdatePose()
    {
        if (folded) return;
        if (foldStarted >= 0f)
        {
            UpdateFold(Time.time - foldStarted);
            return;
        }

        float time = Time.time - firstLaunch;
        bool left = camera != null;
        bool visible = time >= 0f && (left || wrist.gameObject.activeInHierarchy);
        if (viewModel != null) viewModel.SetActive(visible);
        first.gameObject.SetActive(visible);
        second.gameObject.SetActive(visible && time >= gap);
        if (!visible) return;

        float reach = Ease(time / Flight);
        float lift = Ease((time - gap - Flight) / Lift);
        float curl = Ease((time - Flight + 0.11f) / 0.20f);
        // The thumb relaxes just enough to accept the second card.
        float secondCatch = 1f - Mathf.Abs((time - gap - Flight) / 0.12f);
        fingers.SetCardGrip(left, curl, 0.22f * Ease(secondCatch));

        Vector3 gripPoint;
        Quaternion cardRotation;
        if (left)
        {
            Vector3 receive = Vector3.Lerp(new Vector3(-0.155f, -0.275f, 0.43f),
                new Vector3(-0.128f, -0.198f, 0.57f), reach);
            gripPoint = camera.TransformPoint(Vector3.Lerp(receive, new Vector3(-0.075f, -0.177f, 0.46f), lift));
            cardRotation = camera.rotation * Quaternion.Euler(Mathf.Lerp(-53f, -78f, lift), -7f, 7f);
        }
        else
        {
            Vector3 catchPoint = tableCenter + outward * 0.54f + lateral * 0.12f;
            Vector3 held = tableCenter + outward * 0.60f + lateral * 0.14f + Vector3.up * 0.065f;
            gripPoint = Vector3.Lerp(Vector3.Lerp(wrist.TransformPoint(contact.position), catchPoint, reach), held, lift);
            Vector3 face = Vector3.Lerp(outward * 0.55f + Vector3.up * 0.83f, outward + Vector3.up * 0.58f, lift);
            cardRotation = Quaternion.LookRotation(Vector3.ProjectOnPlane(Vector3.up, face), face);
        }

        Quaternion wristRotation = cardRotation * Quaternion.Inverse(contact.rotation);
        if (!left) wristRotation = Quaternion.Slerp(wrist.rotation, wristRotation, reach);
        Vector3 wristTarget = gripPoint - wristRotation * Vector3.Scale(contact.position, wrist.lossyScale);
        PoseArm(wristTarget, wristRotation, reach, lift);

        gripPoint = wrist.TransformPoint(contact.position);
        MoveCard(first.transform, 0, time, lift, gripPoint, cardRotation);
        if (time >= gap) MoveCard(second.transform, 1, time - gap, lift, gripPoint, cardRotation);
    }

    private void PoseArm(Vector3 wristTarget, Quaternion wristRotation, float reach, float lift)
    {
        Quaternion neutralForearm = wristRotation * Quaternion.Inverse(wristRest);
        Vector3 neutralDirection = neutralForearm * wrist.localPosition.normalized;
        if (viewModel != null)
        {
            float elbowHeight = Mathf.Lerp(-0.58f, -0.40f, lift);
            Vector3 towardGrip = wristTarget - camera.TransformPoint(new Vector3(-0.34f, elbowHeight, 0.20f));
            Vector3 armDirection = Vector3.RotateTowards(neutralDirection, towardGrip.normalized, ViewWristBend, 0f);
            Quaternion armRotation = Quaternion.FromToRotation(neutralDirection, armDirection) * neutralForearm;
            // Move the whole skeleton so sleeve vertices blended with the upper arm stay with the forearm.
            Transform body = viewModel.transform;
            body.rotation = armRotation * Quaternion.Inverse(forearm.rotation) * body.rotation;
            body.position += wristTarget - wrist.position;
        }
        else
        {
            Play2TableArm.Reach(upperArm, forearm, wrist, wristTarget);
            // Share the palm turn with the forearm while keeping both ends of the solved arm in place.
            Vector3 armDirection = wrist.position - forearm.position;
            Quaternion armRotation = Quaternion.FromToRotation(neutralDirection, armDirection) * neutralForearm;
            forearm.rotation = Quaternion.Slerp(forearm.rotation, armRotation, 0.55f * reach);
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
        Quaternion gripRotation = Quaternion.Slerp(foldFrom.rotation, foldRotation, down);
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
        if (release >= 1f) card.transform.Find("Front").gameObject.SetActive(false);
    }

    private Pose FoldedCardPose(int index)
    {
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
        card.transform.Find("Front").gameObject.SetActive(false);
        card.gameObject.SetActive(true);
    }

    private void MoveCard(Transform card, int index, float time, float fan, Vector3 gripPoint, Quaternion facing)
    {
        float travel = Ease(time / Flight);
        float angle = index == 0 ? -18f : 18f;
        Quaternion rotation = facing * Quaternion.AngleAxis(angle * fan, Vector3.up);
        Vector3 held = gripPoint + rotation * new Vector3(0f, index * 0.0035f - 0.0015f, GripHeight * card.localScale.z);
        Vector3 flight = Vector3.Lerp(deck, held, travel) + Vector3.up * (Mathf.Sin(travel * Mathf.PI) * 0.075f);
        card.SetPositionAndRotation(flight, Quaternion.Slerp(deckRotation, rotation, travel));
    }

    private static float Ease(float value) => Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(value));

    private static void ShowArmOnly(SkinnedMeshRenderer skin, Transform upperArm)
    {
        var mesh = skin.sharedMesh;
        var bones = skin.bones;
        var arm = new bool[bones.Length];
        for (int i = 0; i < arm.Length; i++)
            arm[i] = bones[i].IsChildOf(upperArm);

        var counts = mesh.GetBonesPerVertex();
        var weights = mesh.GetAllBoneWeights();
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
