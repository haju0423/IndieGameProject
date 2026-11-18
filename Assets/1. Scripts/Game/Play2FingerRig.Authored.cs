using System;
using UnityEngine;

public sealed partial class Play2FingerRig
{
    // Keep the artist's skin weights and use the skeleton mapped on the Humanoid avatar.
    private static bool TryGetAuthoredHands(Animator animator, SkinnedMeshRenderer skin, out Finger[][] hands)
    {
        hands = new Finger[2][];
        var bones = skin.bones;
        var bind = skin.sharedMesh.bindposes;
        for (int side = 0; side < hands.Length; side++)
        {
            bool left = side == 0;
            var wrist = animator.GetBoneTransform(left ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
            int wristIndex = Array.IndexOf(bones, wrist);
            if (wristIndex < 0 || bind.Length != bones.Length) return false;
            var wristBind = bind[wristIndex];
            var hand = hands[side] = new Finger[5];
            int first = (int)(left ? HumanBodyBones.LeftThumbProximal : HumanBodyBones.RightThumbProximal);
            for (int i = 0; i < hand.Length; i++)
            {
                var proximal = animator.GetBoneTransform((HumanBodyBones)(first + i * 3));
                var bend = animator.GetBoneTransform((HumanBodyBones)(first + i * 3 + 1));
                var distal = animator.GetBoneTransform((HumanBodyBones)(first + i * 3 + 2));
                if (proximal == null || bend == null || distal == null) return false;
                int baseIndex = Array.IndexOf(bones, proximal), bendIndex = Array.IndexOf(bones, bend);
                int endIndex = Array.IndexOf(bones, distal);
                if (baseIndex < 0 || bendIndex < 0 || endIndex < 0) return false;
                Matrix4x4 baseToWrist = wristBind * bind[baseIndex].inverse;
                Matrix4x4 bendToWrist = wristBind * bind[bendIndex].inverse;
                Matrix4x4 endToWrist = wristBind * bind[endIndex].inverse;
                Vector3 root = baseToWrist.GetColumn(3), joint = bendToWrist.GetColumn(3);
                Vector3 end = endToWrist.GetColumn(3);
                Vector3 tip = distal.childCount > 0
                    ? endToWrist.MultiplyPoint3x4(distal.GetChild(0).localPosition)
                    : end + (end - joint) * .55f;
                hand[i] = new Finger
                {
                    Thumb = i == 0, Base = root, Bend = joint, Tip = tip,
                    Proximal = proximal, Distal = bend, BoneIndex = baseIndex, DistalBoneIndex = bendIndex,
                    BaseInWrist = baseToWrist.rotation, BendInWrist = bendToWrist.rotation,
                    BaseRest = baseToWrist.rotation, BendRest = (bind[baseIndex] * bind[bendIndex].inverse).rotation,
                    PadOffset = bendToWrist.inverse.MultiplyPoint3x4(Vector3.Lerp(joint, tip, .62f))
                };
            }

            Vector3 palm = Vector3.Cross(hand[2].Tip - hand[2].Base, hand[4].Base - hand[1].Base).normalized;
            if (!left) palm = -palm;
            foreach (var finger in hand)
            {
                Vector3 curl = finger.Thumb ? hand[1].Bend - finger.Bend : palm;
                finger.BaseAxis = Quaternion.Inverse(finger.BaseInWrist)
                    * Vector3.Cross(finger.Bend - finger.Base, curl).normalized;
                finger.BendAxis = Quaternion.Inverse(finger.BendInWrist)
                    * Vector3.Cross(finger.Tip - finger.Bend, curl).normalized;
            }
        }
        return true;
    }
}
