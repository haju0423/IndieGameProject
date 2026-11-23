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
        public int BoneIndex, DistalBoneIndex;
        public Quaternion BaseRest = Quaternion.identity, BendRest = Quaternion.identity;
        public Quaternion BaseInWrist = Quaternion.identity, BendInWrist = Quaternion.identity;
        public Vector3 PadOffset;
        public Quaternion BasePose(Quaternion delta) => BaseRest * Quaternion.Inverse(BaseInWrist) * delta * BaseInWrist;
        public Quaternion BendPose(Quaternion delta) => BendRest * Quaternion.Inverse(BendInWrist) * delta * BendInWrist;
        public bool Thumb;

        public Finger ForBones(Transform[] bones)
        {
            var copy = (Finger)MemberwiseClone();
            copy.Proximal = bones[BoneIndex];
            copy.Distal = bones[DistalBoneIndex];
            return copy;
        }

        public void Curl(float amount)
        {
            Proximal.localRotation = BaseRest * Quaternion.AngleAxis(amount * (Thumb ? 18f : 32f), BaseAxis);
            Distal.localRotation = BendRest * Quaternion.AngleAxis(amount * (Thumb ? 14f : 26f), BendAxis);
        }
    }

    internal static Play2FingerRig Attach(Animator animator)
    {
        // Every procedural hand needs skinning even when its imported animation is culled.
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        foreach (var renderer in animator.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            renderer.updateWhenOffscreen = true;
        if (animator.TryGetComponent(out Play2FingerRig rig)) return rig;
        var skin = animator.GetComponentInChildren<SkinnedMeshRenderer>(true);
        if (skin == null) return null;
        if (TryGetHands(skin.sharedMesh.name, out var left, out var right))
        {
            rig = animator.gameObject.AddComponent<Play2FingerRig>();
            rig.Bind(animator, skin, left, right);
            return rig;
        }
        if (!TryGetAuthoredHands(animator, skin, out var authored)) return null;
        rig = animator.gameObject.AddComponent<Play2FingerRig>();
        rig.hands = authored;
        return rig;
    }

    // A first-person copy can originate from a seat that already has runtime finger joints.
    // Unity remaps the renderer's bones but does not copy the nonserialized finger bindings.
    internal void BindViewCopy(Play2FingerRig original)
    {
        var skin = GetComponentInChildren<SkinnedMeshRenderer>(true);
        var bones = skin.bones;
        hands = Array.ConvertAll(original.hands, hand => Array.ConvertAll(hand, finger => finger.ForBones(bones)));
        PrepareViewMesh(skin);
    }

    // View masking edits triangles, so it needs an independently owned mesh.
    internal void PrepareViewMesh(SkinnedMeshRenderer skin)
    {
        if (riggedMesh != null) return;
        riggedMesh = Instantiate(skin.sharedMesh);
        skin.sharedMesh = riggedMesh;
    }

    public int FingerCount(bool left) => hands[left ? 0 : 1].Length;

    internal Quaternion PalmRotation(bool left, Vector3 direction, Vector3 palmNormal)
    {
        var hand = hands[left ? 0 : 1];
        Vector3 length = hand[2].Bend - hand[2].Base;
        Vector3 width = hand[hand.Length - 1].Base - hand[1].Base;
        Vector3 normal = Vector3.Cross(length, width) * (left ? 1f : -1f);
        return Quaternion.LookRotation(direction, palmNormal)
            * Quaternion.Inverse(Quaternion.LookRotation(length, normal));
    }

    internal Vector3 FingerPad(bool left, int finger, Transform wrist) =>
        wrist.InverseTransformPoint(Pad(hands[left ? 0 : 1][finger]));

    internal Vector3 PushContact(bool left)
    {
        var hand = hands[left ? 0 : 1];
        return (hand[1].Bend + hand[2].Bend + hand[3].Bend) / 3f;
    }

    internal Quaternion ThumbsDownRotation(bool left, Vector3 palmNormal)
    {
        var hand = hands[left ? 0 : 1];
        var thumb = hand[0];
        Vector3 thumbDirection = Vector3.ProjectOnPlane(thumb.Base - hand[2].Base, hand[2].Bend - hand[2].Base);
        Vector3 length = hand[2].Bend - hand[2].Base;
        Vector3 width = hand[hand.Length - 1].Base - hand[1].Base;
        Vector3 normal = Vector3.Cross(length, width) * (left ? 1f : -1f);
        return Quaternion.LookRotation(Vector3.down, palmNormal)
            * Quaternion.Inverse(Quaternion.LookRotation(thumbDirection, normal));
    }

    internal Quaternion PointingRotation(bool left, Vector3 direction, Vector3 backFacing = default)
    {
        var hand = hands[left ? 0 : 1];
        var index = hand[1];
        Vector3 length = index.Bend - index.Base;
        if (backFacing.sqrMagnitude < .0001f)
            return Quaternion.LookRotation(direction, Vector3.up)
                * Quaternion.Inverse(Quaternion.LookRotation(length, hand[0].Base - index.Base));
        Vector3 dorsal = Vector3.Cross(length, hand[hand.Length - 1].Base - index.Base) * (left ? -1f : 1f);
        Vector3 up = Vector3.ProjectOnPlane(backFacing, direction);
        if (up.sqrMagnitude < .0001f) up = Vector3.ProjectOnPlane(Vector3.up, direction);
        return Quaternion.LookRotation(direction, up)
            * Quaternion.Inverse(Quaternion.LookRotation(length, dorsal));
    }

    public void SetFingerCurl(bool left, int finger, float curl)
    {
        hands[left ? 0 : 1][finger].Curl(Mathf.Clamp01(curl));
    }

    internal void SetGestureCurl(bool left, int finger, float curl)
    {
        hands[left ? 0 : 1][finger].Curl(Mathf.Clamp01(curl) * 2f);
    }

    internal void ExtendFinger(bool left, int finger, float amount)
    {
        var digit = hands[left ? 0 : 1][finger];
        var straight = Quaternion.FromToRotation(digit.Tip - digit.Bend, digit.Bend - digit.Base);
        digit.Distal.localRotation = digit.BendPose(Quaternion.Slerp(Quaternion.identity, straight, amount));
    }

    internal void FoldThumb(bool left, float amount, bool tuck = false)
    {
        var hand = hands[left ? 0 : 1];
        var thumb = hand[0];
        Vector3 target = hand[tuck ? 2 : 1].Base;
        var folded = Quaternion.FromToRotation(thumb.Tip - thumb.Base, target - thumb.Base);
        thumb.Proximal.localRotation = thumb.BasePose(Quaternion.RotateTowards(Quaternion.identity, folded, 45f * amount));
        thumb.Distal.localRotation = thumb.BendRest * Quaternion.AngleAxis(25f * amount, thumb.BendAxis);
    }

    internal void ExtendThumb(bool left, float amount)
    {
        var hand = hands[left ? 0 : 1];
        var thumb = hand[0];
        Vector3 lateral = Vector3.ProjectOnPlane(thumb.Base - hand[2].Base, hand[2].Bend - hand[2].Base);
        var rotation = Quaternion.FromToRotation(thumb.Bend - thumb.Base, lateral);
        thumb.Proximal.localRotation = thumb.BasePose(Quaternion.Slerp(Quaternion.identity, rotation, amount));
        ExtendFinger(left, 0, amount);
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

    internal void PinchPacket(bool left, Transform wrist, float weight, float scale)
    {
        // Pad centres sit just outside the two printed faces. The thumb travels further than the index.
        if (weight <= 0f) return;
        var hand = hands[left ? 0 : 1];
        var thumb = hand[0];
        var index = hand[1];
        for (int i = 2; i < hand.Length; i++)
        {
            float heldCurl = left ? 1.06f + (i - 2) * .18f : 1.53f + (i - 2) * .23f;
            hand[i].Curl(Mathf.Lerp(.55f + (i - 2) * .21f, heldCurl, weight));
        }
        Vector3 a = Pad(thumb), b = Pad(index);
        Vector3 axis = a - b;
        float spacing = .015f + .0035f * scale;
        if (axis.sqrMagnitude <= spacing * spacing) return;
        Vector3 middle = Vector3.Lerp(a, b, .66f);
        Vector3 half = axis.normalized * (spacing * .5f);
        // Bring the pinch within reach of both knuckles before bending the two digits.
        // The owl and bull have much wider thumb bases than the other two hands.
        Vector3 thumbBase = thumb.Proximal.position - half;
        Vector3 indexBase = index.Proximal.position + half;
        float thumbReach = PinchReach(thumb, a);
        float indexReach = PinchReach(index, b);
        for (int pass = 0; pass < 3; pass++)
        {
            middle = thumbBase + Vector3.ClampMagnitude(middle - thumbBase, thumbReach);
            middle = indexBase + Vector3.ClampMagnitude(middle - indexBase, indexReach);
        }
        Play2TableArm.ReachPoint(thumb.Proximal, thumb.Distal, a, Vector3.Lerp(a, middle + half, weight));
        Play2TableArm.ReachPoint(index.Proximal, index.Distal, b, Vector3.Lerp(b, middle - half, weight));
        if (!left)
        {
            // Two fingers support the lower back of the fan; the pinky can rest against the palm.
            Vector3 up = Vector3.ProjectOnPlane(wrist.TransformDirection(hand[2].Tip - hand[2].Base), axis).normalized;
            Vector3 across = Vector3.Cross(up, axis.normalized);
            for (int i = 2; i <= 3; i++)
            {
                var digit = hand[i];
                Vector3 pad = Pad(digit);
                Vector3 support = middle - half + scale * (across * (.013f * (i - 1)) + up * (.012f - .009f * (i - 2)));
                Vector3 shift = Vector3.ClampMagnitude(support - pad, .028f) * weight;
                Play2TableArm.ReachPoint(digit.Proximal, digit.Distal, pad, pad + shift);
            }
        }
    }

    internal void PressCardBack(Vector3 target, float weight)
    {
        var index = hands[0][1];
        Vector3 pad = Pad(index);
        Vector3 end = pad + Vector3.ClampMagnitude(target - pad, .055f) * weight;
        Play2TableArm.ReachPoint(index.Proximal, index.Distal, pad, end, Vector3.up, weight);
    }

    internal void SupportFan(Vector3 pinch, Quaternion fan, float scale, float weight)
    {
        Vector3 right = fan * Vector3.right;
        Vector3 normal = fan * Vector3.up;
        Vector3 top = fan * Vector3.forward;
        // Stagger the contact points along the packet. A thumb and index pressed
        // tip to tip at a single point look as if they punch through both cards.
        for (int i = 0; i < hands[0].Length; i++)
        {
            var finger = hands[0][i];
            Vector3 pad = Pad(finger);
            float across, height, depth;
            if (i == 0) { across = .005f; height = .003f; depth = .010f; }
            else
            {
                across = .007f * (i - 1);
                height = .007f - .012f * (i - 1);
                float padLength = Vector3.Distance(finger.Distal.position, pad);
                float clearance = 1.5f * Mathf.Max(0f, padLength - .026f) / scale;
                depth = (i == 1 ? -.016f : -.023f) - clearance;
            }
            Vector3 target = pinch + scale * (right * across + top * height + normal * depth);
            Vector3 shift = Vector3.ClampMagnitude(target - pad, .055f);
            Play2TableArm.ReachPoint(finger.Proximal, finger.Distal, pad, pad + shift * weight, normal, weight);
        }
    }

    private static float PinchReach(Finger digit, Vector3 pad)
    {
        return .95f * (Vector3.Distance(digit.Proximal.position, digit.Distal.position)
            + Vector3.Distance(digit.Distal.position, pad));
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
        finger.Distal.TransformPoint(finger.PadOffset);

    private void Bind(Animator animator, SkinnedMeshRenderer skin, Finger[] left, Finger[] right)
    {
        hands = new[] { left, right };
        var source = skin.sharedMesh;
        var bones = new List<Transform>(skin.bones);
        var bindPoses = new List<Matrix4x4>(source.bindposes);
        var wrists = new int[2];
        var forearms = new int[2];
        for (int side = 0; side < hands.Length; side++)
        {
            var wrist = animator.GetBoneTransform(side == 0 ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
            int wristIndex = wrists[side] = bones.IndexOf(wrist);
            forearms[side] = bones.IndexOf(animator.GetBoneTransform(side == 0 ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm));
            var wristBind = bindPoses[wristIndex];
            for (int i = 0; i < hands[side].Length; i++)
            {
                var finger = hands[side][i];
                string label = i == 0 ? "Thumb" : "Finger" + i;
                finger.BoneIndex = bones.Count;
                finger.DistalBoneIndex = bones.Count + 1;
                finger.PadOffset = (finger.Tip - finger.Bend) * 0.62f;
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
        Reweight(source, wrists, forearms, bindPoses);
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

    private void Reweight(Mesh source, int[] wrists, int[] forearms, List<Matrix4x4> bindPoses)
    {
        var vertices = source.vertices;
        var surfaceMaps = new (int digit, float distance)[hands.Length][];
        for (int side = 0; side < hands.Length; side++)
            surfaceMaps[side] = MapDigits(source, bindPoses[wrists[side]], hands[side]);
        var counts = source.GetBonesPerVertex();
        var sourceWeights = source.GetAllBoneWeights();
        var newCounts = new byte[vertices.Length];
        var newWeights = new List<BoneWeight1>(sourceWeights.Length);
        // Imported fingertips can carry forearm weight. Fade both contributions into the digit joints.
        var vertexWeights = new BoneWeight1[bindPoses.Count];
        int offset = 0;
        for (int vertex = 0; vertex < vertices.Length; vertex++)
        {
            int count = counts[vertex];
            int written = 0;
            for (int i = 0; i < count; i++)
            {
                var weight = sourceWeights[offset++];
                int side = Array.IndexOf(wrists, weight.boneIndex);
                if (side < 0) side = Array.IndexOf(forearms, weight.boneIndex);
                if (side < 0)
                {
                    Append(weight);
                    continue;
                }

                var mapped = surfaceMaps[side][vertex];
                Vector3 point = bindPoses[wrists[side]].MultiplyPoint3x4(vertices[vertex]);
                FindFinger(hands[side], mapped.digit, mapped.distance, point,
                    out int first, out int second, out float blend, out float influence);
                float remaining = weight.weight * (1f - influence);
                if (weight.boneIndex == forearms[side])
                {
                    float palm = Mathf.SmoothStep(0f, 1f, (point.y + .035f) / .055f);
                    Append(new BoneWeight1 { boneIndex = wrists[side], weight = remaining * palm });
                    Append(new BoneWeight1 { boneIndex = forearms[side], weight = remaining * (1f - palm) });
                }
                else Append(new BoneWeight1 { boneIndex = weight.boneIndex, weight = remaining });
                Append(new BoneWeight1 { boneIndex = first, weight = weight.weight * influence * (1f - blend) });
                Append(new BoneWeight1 { boneIndex = second, weight = weight.weight * influence * blend });
            }

            void Append(BoneWeight1 weight)
            {
                if (weight.weight <= 0f) return;
                for (int i = 0; i < written; i++)
                {
                    if (vertexWeights[i].boneIndex != weight.boneIndex) continue;
                    vertexWeights[i].weight += weight.weight;
                    return;
                }
                vertexWeights[written++] = weight;
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
            written = Mathf.Min(written, MaxSkinWeights);
            float total = 0;
            for (int i = 0; i < written; i++) total += vertexWeights[i].weight;
            newCounts[vertex] = (byte)written;
            for (int i = 0; i < written; i++)
            {
                vertexWeights[i].weight /= total;
                newWeights.Add(vertexWeights[i]);
            }
        }

        using var vertexCounts = new NativeArray<byte>(newCounts, Allocator.Temp);
        using var weights = new NativeArray<BoneWeight1>(newWeights.ToArray(), Allocator.Temp);
        riggedMesh.SetBoneWeights(vertexCounts, weights);
    }

    private static void FindFinger(Finger[] fingers, int digit, float distance, Vector3 point,
        out int first, out int second, out float blend, out float influence)
    {
        first = second = 0;
        blend = influence = 0f;
        if (digit < 0 || float.IsPositiveInfinity(distance)) return;
        var finger = fingers[digit];
        float proximal = Vector3.Distance(finger.Base, finger.Bend);
        float distal = Vector3.Distance(finger.Bend, finger.Tip);
        float progress = proximal + distal - distance;
        if (finger.Thumb)
            progress = Mathf.Max(progress, Vector3.Dot(point - finger.Base, (finger.Tip - finger.Base).normalized));
        first = finger.BoneIndex;
        second = first + 1;
        blend = Mathf.SmoothStep(0f, 1f, (progress - proximal * .65f) / (proximal * .7f));
        influence = Mathf.SmoothStep(0f, 1f, progress / (proximal * 1.1f));
    }

    private static (int digit, float distance)[] MapDigits(Mesh mesh, Matrix4x4 wristBind, Finger[] fingers)
    {
        // Follow the connected surface so a curved fingertip cannot be assigned to the next finger across the gap.
        var vertices = mesh.vertices;
        var welded = new Dictionary<Vector3Int, int>();
        var points = new List<Vector3>();
        var vertexNodes = new int[vertices.Length];
        Array.Fill(vertexNodes, -1);
        for (int i = 0; i < vertices.Length; i++)
        {
            Vector3 point = wristBind.MultiplyPoint3x4(vertices[i]);
            if (point.y < -.02f || point.sqrMagnitude > .16f * .16f) continue;
            var key = Vector3Int.RoundToInt(point * 10000f);
            if (!welded.TryGetValue(key, out int node))
            {
                node = points.Count;
                welded.Add(key, node);
                points.Add(point);
            }
            vertexNodes[i] = node;
        }
        var neighbors = new HashSet<int>[points.Count];
        for (int i = 0; i < neighbors.Length; i++) neighbors[i] = new HashSet<int>();
        var triangles = mesh.triangles;
        for (int i = 0; i < triangles.Length; i += 3)
        for (int edge = 0; edge < 3; edge++)
        {
            int a = vertexNodes[triangles[i + edge]], b = vertexNodes[triangles[i + (edge + 1) % 3]];
            if (a < 0 || b < 0 || a == b) continue;
            neighbors[a].Add(b);
            neighbors[b].Add(a);
        }
        var labels = new int[points.Count];
        var best = new float[points.Count];
        Array.Fill(best, float.PositiveInfinity);
        for (int finger = 0; finger < fingers.Length; finger++)
        {
            int seed = -1;
            float closest = float.PositiveInfinity;
            for (int node = 0; node < points.Count; node++)
            {
                float distance = (points[node] - fingers[finger].Tip).sqrMagnitude;
                if (distance >= closest) continue;
                closest = distance;
                seed = node;
            }
            if (seed < 0) continue;
            var distances = new float[points.Count];
            Array.Fill(distances, float.PositiveInfinity);
            distances[seed] = 0;
            var pending = new SortedSet<(float distance, int node)> { (0, seed) };
            while (pending.Count > 0)
            {
                var current = pending.Min;
                pending.Remove(current);
                foreach (int neighbor in neighbors[current.node])
                {
                    float distance = current.distance + Vector3.Distance(points[current.node], points[neighbor]);
                    if (distance >= distances[neighbor]) continue;
                    pending.Remove((distances[neighbor], neighbor));
                    distances[neighbor] = distance;
                    pending.Add((distance, neighbor));
                }
            }
            for (int node = 0; node < points.Count; node++)
            {
                if (distances[node] >= best[node]) continue;
                best[node] = distances[node];
                labels[node] = finger;
            }
        }
        var result = new (int digit, float distance)[vertices.Length];
        for (int i = 0; i < result.Length; i++)
        {
            int node = vertexNodes[i];
            result[i] = node >= 0 ? (labels[node], best[node]) : (-1, float.PositiveInfinity);
        }
        return result;
    }

    private void OnDestroy()
    {
        if (riggedMesh != null) Destroy(riggedMesh);
    }
}
