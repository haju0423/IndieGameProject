using System;
using UnityEngine;

// Older rigs without table-specific transitions retain the planted-foot fallback.
internal sealed class Play2AllInPosture
{
    private static readonly HumanBodyBones[] StandingBones =
    {
        HumanBodyBones.Hips, HumanBodyBones.Spine, HumanBodyBones.Chest,
        HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot,
        HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot
    };

    private readonly Transform[] joints = new Transform[StandingBones.Length];
    private readonly Quaternion[] standing = new Quaternion[StandingBones.Length];
    private readonly Quaternion[] animated = new Quaternion[StandingBones.Length];
    private readonly Transform hips, spine, leftFoot, rightFoot;
    private Quaternion seatedSpine;
    private bool leaning;
    private readonly Transform[] upperArms = new Transform[2], elbows = new Transform[2], wrists = new Transform[2];
    private Play2FingerRig fingers;
    private float emptyWeight;
    private Vector3 hipLocal;
    private static readonly int AtTable = Animator.StringToHash("AtTable");
    private static readonly int Ready = Animator.StringToHash("Ready");
    private readonly Animator animator;
    private readonly bool hasStandClip;
    private Vector3 plantedFeet;
    private float seatedHipHeight;
    private bool modified, standCycle;
    private float progress;

    internal bool AllIn { get; set; }
    internal float Weight => Mathf.SmoothStep(0f, 1f, progress);
    internal float Rise { get; private set; }

    internal Play2AllInPosture(Animator animator)
    {
        this.animator = animator;
        for (int i = 0; i < 2; i++)
        {
            upperArms[i] = animator.GetBoneTransform(i == 0 ? HumanBodyBones.LeftUpperArm : HumanBodyBones.RightUpperArm);
            elbows[i] = animator.GetBoneTransform(i == 0 ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm);
            wrists[i] = animator.GetBoneTransform(i == 0 ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
        }
        hasStandClip = Array.Exists(animator.parameters, p => p.nameHash == AtTable && p.type == AnimatorControllerParameterType.Bool);
        var rest = animator.avatar.humanDescription.skeleton;
        for (int i = 0; i < joints.Length; i++)
        {
            var bone = animator.GetBoneTransform(StandingBones[i]);
            if (bone == null) continue;
            int index = Array.FindIndex(rest, entry => entry.name == bone.name);
            if (index < 0) continue;
            joints[i] = bone;
            standing[i] = rest[index].rotation;
        }
        hips = animator.GetBoneTransform(HumanBodyBones.Hips);
        spine = animator.GetBoneTransform(HumanBodyBones.Spine);
        leftFoot = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
        rightFoot = animator.GetBoneTransform(HumanBodyBones.RightFoot);
    }

    internal void RestEmptyArms(bool empty, Vector3 tableCenter)
    {
        emptyWeight = Mathf.MoveTowards(emptyWeight, empty && AllIn ? Weight : 0f, Time.deltaTime / .34f);
        if (emptyWeight <= 0 || !animator.isActiveAndEnabled) return;
        fingers ??= Play2FingerRig.Attach(animator);
        Vector3 forward = Vector3.ProjectOnPlane(tableCenter - hips.position, Vector3.up).normalized;
        Vector3 right = Vector3.Cross(Vector3.up, forward);
        for (int i = 0; i < wrists.Length; i++)
        {
            var upper = upperArms[i];
            var elbow = elbows[i];
            var hand = wrists[i];
            var side = right * (i == 0 ? -1f : 1f);
            float length = Vector3.Distance(upper.position, elbow.position) + Vector3.Distance(elbow.position, hand.position);
            Vector3 wristTarget = upper.position - Vector3.up * (length * .85f) + side * (length * .12f)
                + forward * (length * .16f);
            Quaternion relaxed = fingers.PalmRotation(i == 0, Vector3.down + forward * .15f, -side);
            Quaternion rotation = Quaternion.Slerp(hand.rotation, relaxed, emptyWeight);
            Play2TableArm.Reach(upper, elbow, hand, Vector3.Lerp(hand.position, wristTarget, emptyWeight));
            hand.rotation = rotation;
        }
    }

    internal void LeanIntoPush(float time, Vector3 tableCenter)
    {
        if (time < 0f || spine == null || !animator.isActiveAndEnabled) return;
        float reach = Mathf.SmoothStep(0f, 1f, (time - .05f) / .48f);
        float release = Mathf.SmoothStep(0f, 1f, (time - Play2TableChips.PushEnd) / .32f);
        float pitch = 24f * reach * (1f - release);
        if (pitch <= 0f) return;
        Vector3 toward = Vector3.ProjectOnPlane(tableCenter - hips.position, Vector3.up).normalized;
        seatedSpine = spine.localRotation;
        spine.rotation = Quaternion.AngleAxis(pitch, Vector3.Cross(Vector3.up, toward)) * spine.rotation;
        leaning = true;
    }

    // Restore before the animator evaluates. A culled animator can otherwise
    // inherit last frame's procedural pose and apply the same offset twice.
    internal void Restore()
    {
        if (leaning)
        {
            spine.localRotation = seatedSpine;
            leaning = false;
        }
        if (!modified) return;
        hips.localPosition = hipLocal;
        if (!hasStandClip)
            for (int i = 0; i < joints.Length; i++)
                if (joints[i] != null) joints[i].localRotation = animated[i];
        modified = false;
    }

    internal void Animate(bool firstPerson, bool pushing = false)
    {
        progress = Mathf.MoveTowards(progress, AllIn && !pushing ? 1f : 0f, Time.deltaTime / .68f);
        Rise = 0f;
        if (firstPerson || !hips.gameObject.activeInHierarchy) return;
        if (hasStandClip)
        {
            // Unity resets animator parameters when a hidden seat becomes visible again.
            if (!animator.GetBool(AtTable)) animator.SetBool(AtTable, true);
            bool standingAtTable = AllIn && !pushing;
            Vector3 feet = (leftFoot.position + rightFoot.position) * .5f;
            if (standingAtTable && !standCycle)
            {
                standCycle = true;
                seatedHipHeight = hips.position.y;
                plantedFeet = feet;
            }
            animator.SetBool(Ready, standingAtTable);
            if (!standingAtTable && !animator.IsInTransition(0) && animator.GetCurrentAnimatorStateInfo(0).IsName("Seated")) standCycle = false;
            if (standCycle)
            {
                hipLocal = hips.localPosition;
                // In-place rise clips slide the ankles backwards beneath the seat.
                hips.position += plantedFeet - feet;
                Rise = Mathf.Max(0f, hips.position.y - seatedHipHeight);
                modified = true;
            }
            return;
        }
        if (progress <= 0f) return;
        Vector3 feetBefore = (leftFoot.position + rightFoot.position) * .5f;
        hipLocal = hips.localPosition;
        float weight = Weight;
        for (int i = 0; i < joints.Length; i++)
        {
            var bone = joints[i];
            if (bone == null) continue;
            animated[i] = bone.localRotation;
            bone.localRotation = Quaternion.Slerp(animated[i], standing[i], weight);
        }
        Vector3 correction = feetBefore - (leftFoot.position + rightFoot.position) * .5f;
        hips.position += correction;
        Rise = Mathf.Max(0f, correction.y);
        modified = true;
    }
}
