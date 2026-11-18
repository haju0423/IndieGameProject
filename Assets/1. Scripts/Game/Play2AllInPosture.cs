using System;
using UnityEngine;

// The shipped avatars have a seated clip. Blend only the lower body towards
// each avatar's own standing bind pose, with its feet planted by the chair.
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
    private bool modified;
    private float progress;

    internal bool AllIn { get; set; }
    internal float Weight => Mathf.SmoothStep(0f, 1f, progress);
    internal float Rise { get; private set; }

    internal Play2AllInPosture(Animator animator)
    {
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
        for (int i = 0; i < joints.Length; i++)
            if (joints[i] != null) joints[i].localRotation = animated[i];
        modified = false;
    }

    internal void Animate(bool firstPerson)
    {
        progress = Mathf.MoveTowards(progress, AllIn ? 1f : 0f, Time.deltaTime / .68f);
        Rise = 0f;
        if (progress <= 0f || firstPerson || !hips.gameObject.activeInHierarchy) return;
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
