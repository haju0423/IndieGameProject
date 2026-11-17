using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;

[DisallowMultipleComponent]
public sealed partial class Play2FingerRig : MonoBehaviour
{
    private const int MaxSkinWeights = 4;
    private Mesh riggedMesh;
    private Finger[][] hands;

    private sealed class Finger
    {
        public Vector3 Base, Bend, Tip, BaseAxis, BendAxis;
        public Transform Proximal, Distal;
        public int BoneIndex;
        public bool Thumb;

        public Finger ForBones(Transform[] bones)
        {
            var copy = (Finger)MemberwiseClone();
            copy.Proximal = bones[BoneIndex];
            copy.Distal = bones[BoneIndex + 1];
            return copy;
        }

        public void Curl(float amount)
        {
            Proximal.localRotation = Quaternion.AngleAxis(amount * (Thumb ? 18f : 32f), BaseAxis);
            Distal.localRotation = Quaternion.AngleAxis(amount * (Thumb ? 14f : 26f), BendAxis);
        }
    }

    internal static Play2FingerRig Attach(Animator animator)
    {
        if (animator.TryGetComponent(out Play2FingerRig rig)) return rig;
        var skin = animator.GetComponentInChildren<SkinnedMeshRenderer>(true);
        if (skin == null || !TryGetHands(skin.sharedMesh.name, out var left, out var right)) return null;
        rig = animator.gameObject.AddComponent<Play2FingerRig>();
        rig.Bind(animator, skin, left, right);
        return rig;
    }

    // A first-person copy can originate from a seat that already has runtime finger joints.
    // Unity remaps the renderer's bones but does not copy the nonserialized finger bindings.
    internal void BindViewCopy(Play2FingerRig original)
    {
        var skin = GetComponentInChildren<SkinnedMeshRenderer>(true);
        var bones = skin.bones;
        hands = Array.ConvertAll(original.hands, hand => Array.ConvertAll(hand, finger => finger.ForBones(bones)));
        riggedMesh = Instantiate(skin.sharedMesh);
        skin.sharedMesh = riggedMesh;
    }

    public int FingerCount(bool left) => hands[left ? 0 : 1].Length;

    public void SetFingerCurl(bool left, int finger, float curl)
    {
        hands[left ? 0 : 1][finger].Curl(Mathf.Clamp01(curl));
    }

    internal void SetGrip(float curl)
    {
        for (int i = 0; i < FingerCount(false); i++)
            SetFingerCurl(false, i, curl * (i == 1 ? 0.65f : 1f));
    }

    internal void SetCardGrip(bool left, float amount, float thumbRelease = 0f)
    {
        float thumb = Mathf.SmoothStep(0f, 0.65f, (amount - 0.25f) / 0.75f);
        SetFingerCurl(left, 0, thumb * (1f - thumbRelease));
        SetFingerCurl(left, 1, 0.27f * amount);
        for (int i = 2; i < FingerCount(left); i++)
            SetFingerCurl(left, i, (0.55f + (i - 2) * 0.21f) * amount);
    }

    internal Pose CardContact(bool left, Transform wrist)
    {
        var fingers = hands[left ? 0 : 1];
        Vector3 thumb = Pad(fingers[0]);
        Vector3 index = Pad(fingers[1]);
        Vector3 front = thumb - index;
        Vector3 length = wrist.TransformDirection(fingers[2].Tip - fingers[2].Base);
        Vector3 center = Vector3.Lerp(thumb, index, 0.52f);
        Quaternion rotation = Quaternion.LookRotation(Vector3.ProjectOnPlane(length, front), front);
        return new Pose(wrist.InverseTransformPoint(center), Quaternion.Inverse(wrist.rotation) * rotation);
    }

    private static Vector3 Pad(Finger finger) =>
        finger.Distal.TransformPoint((finger.Tip - finger.Bend) * 0.62f);

    private void Bind(Animator animator, SkinnedMeshRenderer skin, Finger[] left, Finger[] right)
    {
        hands = new[] { left, right };
        var source = skin.sharedMesh;
        var bones = new List<Transform>(skin.bones);
        var bindPoses = new List<Matrix4x4>(source.bindposes);
        var wrists = new int[2];
        for (int side = 0; side < hands.Length; side++)
        {
            var wrist = animator.GetBoneTransform(side == 0 ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
            int wristIndex = wrists[side] = bones.IndexOf(wrist);
            var wristBind = bindPoses[wristIndex];
            for (int i = 0; i < hands[side].Length; i++)
            {
                var finger = hands[side][i];
                string label = i == 0 ? "Thumb" : "Finger" + i;
                finger.BoneIndex = bones.Count;
                finger.Proximal = CreateJoint(wrist, label + " Proximal", finger.Base);
                finger.Distal = CreateJoint(finger.Proximal, label + " Distal", finger.Bend - finger.Base);
                bones.Add(finger.Proximal);
                bones.Add(finger.Distal);
                bindPoses.Add(Matrix4x4.Translate(-finger.Base) * wristBind);
                bindPoses.Add(Matrix4x4.Translate(-finger.Bend) * wristBind);
            }
        }

        riggedMesh = Instantiate(source);
        riggedMesh.name = source.name + " (Finger Rig)";
        riggedMesh.bindposes = bindPoses.ToArray();
        Reweight(source, wrists, bindPoses);
        skin.bones = bones.ToArray();
        skin.sharedMesh = riggedMesh;
    }

    private static Transform CreateJoint(Transform parent, string label, Vector3 offset)
    {
        var joint = new GameObject(label).transform;
        joint.SetParent(parent, false);
        joint.localPosition = offset;
        return joint;
    }

    private void Reweight(Mesh source, int[] wrists, List<Matrix4x4> bindPoses)
    {
        var vertices = source.vertices;
        var counts = source.GetBonesPerVertex();
        var sourceWeights = source.GetAllBoneWeights();
        var newCounts = new byte[vertices.Length];
        var newWeights = new List<BoneWeight1>(sourceWeights.Length);
        // Only redistribute the wrist's share. The shoulder, forearm and all body weights stay intact.
        var vertexWeights = new BoneWeight1[MaxSkinWeights + 2];
        int offset = 0;
        for (int vertex = 0; vertex < vertices.Length; vertex++)
        {
            int count = counts[vertex];
            int written = 0;
            for (int i = 0; i < count; i++)
            {
                var weight = sourceWeights[offset++];
                int side = Array.IndexOf(wrists, weight.boneIndex);
                if (side < 0 || !FindFinger(hands[side], bindPoses[weight.boneIndex].MultiplyPoint3x4(vertices[vertex]),
                        weight.boneIndex, out int first, out int second, out float blend))
                {
                    vertexWeights[written++] = weight;
                    continue;
                }

                if (count == MaxSkinWeights)
                    vertexWeights[written++] = new BoneWeight1 { boneIndex = blend < 0.5f ? first : second, weight = weight.weight };
                else
                {
                    if (blend < 1f) vertexWeights[written++] = new BoneWeight1 { boneIndex = first, weight = weight.weight * (1f - blend) };
                    if (blend > 0f) vertexWeights[written++] = new BoneWeight1 { boneIndex = second, weight = weight.weight * blend };
                }
            }

            // Unity expects each vertex's most influential bone first.
            for (int i = 1; i < written; i++)
            {
                var weight = vertexWeights[i];
                int j = i;
                while (j > 0 && vertexWeights[j - 1].weight < weight.weight)
                {
                    vertexWeights[j] = vertexWeights[j - 1];
                    j--;
                }
                vertexWeights[j] = weight;
            }
            newCounts[vertex] = (byte)written;
            for (int i = 0; i < written; i++) newWeights.Add(vertexWeights[i]);
        }

        using var vertexCounts = new NativeArray<byte>(newCounts, Allocator.Temp);
        using var weights = new NativeArray<BoneWeight1>(newWeights.ToArray(), Allocator.Temp);
        riggedMesh.SetBoneWeights(vertexCounts, weights);
    }

    private static bool FindFinger(Finger[] fingers, Vector3 point, int wrist,
        out int first, out int second, out float blend)
    {
        first = second = wrist;
        blend = 0f;
        Finger nearest = null;
        float distance = float.PositiveInfinity;
        foreach (var finger in fingers)
        {
            float candidate = Mathf.Min(SegmentDistance(point, finger.Base, finger.Bend),
                SegmentDistance(point, finger.Bend, finger.Tip));
            if (candidate >= distance) continue;
            nearest = finger;
            distance = candidate;
        }

        Vector3 along = (nearest.Tip - nearest.Base).normalized;
        float length = Vector3.Dot(point - nearest.Base, along);
        float middle = Vector3.Dot(nearest.Bend - nearest.Base, along);
        float transition = middle * 0.45f;
        if (length <= -transition || distance > 0.033f * 0.033f) return false;
        if (length < middle - transition)
        {
            second = nearest.BoneIndex;
            blend = Mathf.SmoothStep(0f, 1f, (length + transition) / (middle * 0.90f));
        }
        else
        {
            first = nearest.BoneIndex;
            second = first + 1;
            blend = Mathf.SmoothStep(0f, 1f, (length - middle + transition) / (2f * transition));
        }
        return true;
    }

    private static float SegmentDistance(Vector3 point, Vector3 from, Vector3 to)
    {
        Vector3 axis = to - from;
        return (point - from - axis * Mathf.Clamp01(Vector3.Dot(point - from, axis) / axis.sqrMagnitude)).sqrMagnitude;
    }

    private void OnDestroy()
    {
        if (riggedMesh != null) Destroy(riggedMesh);
    }
}
