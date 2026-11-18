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
    private readonly Transform hips, leftFoot, rightFoot;
    private Vector3 hipLocal;
    private static readonly int AtTable = Animator.StringToHash("AtTable");
    private static readonly int Ready = Animator.StringToHash("Ready");
    private readonly Animator animator;
    private readonly bool hasStandClip;
    private float seatedHipHeight, plantedHeight;
    private bool modified, standCycle;
    private float progress;

    internal bool AllIn { get; set; }
    internal float Weight => Mathf.SmoothStep(0f, 1f, progress);
    internal float Rise { get; private set; }

    internal Play2AllInPosture(Animator animator)
    {
        this.animator = animator;
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
        leftFoot = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
        rightFoot = animator.GetBoneTransform(HumanBodyBones.RightFoot);
    }

    // Restore before the animator evaluates. A culled animator can otherwise
    // inherit last frame's procedural pose and apply the same offset twice.
    internal void Restore()
    {
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
            float feet = (leftFoot.position.y + rightFoot.position.y) * .5f;
            bool wasStanding = animator.GetBool(Ready);
            if (standingAtTable && !wasStanding)
            {
                standCycle = true;
                seatedHipHeight = hips.position.y;
                plantedHeight = feet;
            }
            animator.SetBool(Ready, standingAtTable);
            if (!standingAtTable && !animator.IsInTransition(0) && animator.GetCurrentAnimatorStateInfo(0).IsName("Seated")) standCycle = false;
            if (standCycle)
            {
                hipLocal = hips.localPosition;
                hips.position += Vector3.up * (plantedHeight - feet);
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
