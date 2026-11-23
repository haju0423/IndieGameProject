using System;
using UnityEngine;

internal sealed class Play2RightHandGrip
{
    internal const float PreviewViewCardScale = 1.17f;
    internal const float PreviewOpponentCardScale = 1.10f;
    internal const float PreviewSpread = 12f;
    internal static readonly Vector3 PreviewViewPoint = new(.10f, -.19f, .48f);
    internal static readonly Quaternion PreviewViewFacing = Quaternion.Euler(-78f, 7f, -7f);
    private static readonly string[] Digits = { "Thumb", "Index", "Middle", "Ring", "Pinky" };
    private static readonly Vector3 ReleaseDirection = new Vector3(0.65f, 1f, -0.70f);
    private static readonly Vector3[] CatCurl =
    {
        Vector3.zero, new Vector3(11f, 16f, 10f), new Vector3(26f, 37f, 24f),
        new Vector3(34f, 43f, 28f), new Vector3(37f, 46f, 30f)
    };
    private static readonly GripProfile CowGrip = new GripProfile(
        new Vector3(8f, 12f, 8f), new Vector3(17f, 24f, 16f),
        new Vector3(22f, 27f, 19f), new Vector3(23f, 29f, 20f),
        new Vector3(0.80f, -0.28f, -0.53f), 0.20f, 18.5f, 44f);
    private static readonly GripProfile FoxGrip = new GripProfile(
        new Vector3(7f, 10f, 7f), new Vector3(16f, 23f, 14f),
        new Vector3(20f, 30f, 18f), new Vector3(22f, 31f, 19f),
        new Vector3(0.78f, -0.22f, -0.59f), 0.185f, 46f, 56f);
    private static readonly GripProfile OwlGrip = new GripProfile(
        new Vector3(7f, 12f, 8f), new Vector3(18f, 26f, 16f),
        new Vector3(21f, 27f, 16f), new Vector3(24f, 30f, 19f),
        new Vector3(0.76f, -0.30f, -0.57f), 0.15f, 32f, 74f);

    private sealed class GripProfile
    {
        internal readonly Vector3[] Curl;
        internal readonly Vector3 PinchDirection;
        internal readonly float PadSpacing;
        internal readonly float ThumbClearanceAngle;
        internal readonly float ThumbReleaseAngle;

        internal GripProfile(Vector3 index, Vector3 middle, Vector3 ring, Vector3 pinky,
            Vector3 pinchDirection, float padSpacing, float thumbClearanceAngle, float thumbReleaseAngle)
        {
            Curl = new[] { Vector3.zero, index, middle, ring, pinky };
            PinchDirection = pinchDirection;
            PadSpacing = padSpacing;
            ThumbClearanceAngle = thumbClearanceAngle;
            ThumbReleaseAngle = thumbReleaseAngle;
        }
    }

    [Serializable]
    private sealed class PreviewPoses
    {
        public PreviewPose[] characters;
    }

    [Serializable]
    private sealed class PreviewPose
    {
        public string name;
        public float wristScale;
        public Vector3 contactPosition;
        public Quaternion contactRotation;
        public Quaternion[] joints;
    }

    private static readonly Lazy<PreviewPoses> ReferencePoses = new(() =>
    {
        var data = Resources.Load<TextAsset>("TablePresentation/RightHandCardGrip");
        return data == null ? null : JsonUtility.FromJson<PreviewPoses>(data.text);
    });

    private readonly Transform[,] joints = new Transform[5, 4];
    private readonly Quaternion[,] open = new Quaternion[5, 3];
    private readonly Quaternion[,] closed = new Quaternion[5, 3];
    private readonly Quaternion[,] held = new Quaternion[5, 3];
    private readonly float thumbClearanceAngle;
    private readonly float thumbReleaseAngle;

    internal Transform UpperArm { get; }
    internal Transform Forearm { get; }
    internal Transform Wrist { get; }
    internal Pose Contact { get; }
    internal float PreviewScale { get; } = 1f;

    internal Play2RightHandGrip(GameObject player, bool usePreviewPose = false)
    {
        var bones = player.GetComponentsInChildren<Transform>(true);
        Transform FindBone(string name) =>
            Array.Find(bones, bone => bone.name == "mixamorig:Right" + name) ??
            throw new InvalidOperationException($"Missing right {name} bone on {player.name}.");

        UpperArm = FindBone("Arm");
        Forearm = FindBone("ForeArm");
        Wrist = FindBone("Hand");
        string character = CharacterFor(player);
        var profile = character switch
        {
            "Cow" => CowGrip,
            "Fox" => FoxGrip,
            "Owl" => OwlGrip,
            _ => null
        };
        thumbClearanceAngle = profile?.ThumbClearanceAngle ?? 19f;
        thumbReleaseAngle = profile?.ThumbReleaseAngle ?? 50f;
        for (int digit = 0; digit < Digits.Length; digit++)
        {
            for (int joint = 0; joint < 4; joint++)
            {
                var bone = joints[digit, joint] = FindBone($"Hand{Digits[digit]}{joint + 1}");
                if (joint < 3) open[digit, joint] = bone.localRotation;
            }
        }

        Vector3 forward = (joints[2, 0].position - Wrist.position).normalized;
        Vector3 across = (joints[1, 0].position - joints[4, 0].position).normalized;
        Vector3 intoPalm = Vector3.Cross(forward, across).normalized;
        var curlAngles = profile?.Curl ?? CatCurl;
        for (int digit = 1; digit < Digits.Length; digit++)
        {
            Vector3 fingerDirection = profile == null
                ? forward
                : joints[digit, 1].position - joints[digit, 0].position;
            Vector3 curlAxis = Vector3.Cross(fingerDirection, intoPalm).normalized;
            for (int joint = 0; joint < 3; joint++)
            {
                var bone = joints[digit, joint];
                bone.rotation = Quaternion.AngleAxis(curlAngles[digit][joint], curlAxis) * bone.rotation;
            }
        }

        Vector3 index = Pad(1);
        Vector3 normal = Vector3.ProjectOnPlane(Pad(0) - index, forward).normalized;
        if (profile != null)
        {
            // A tilted pinch keeps the card readable without twisting the wrist toward the knuckles.
            Vector3 thumbSide = Vector3.ProjectOnPlane(across, forward).normalized;
            Vector3 pinch = profile.PinchDirection;
            normal = (thumbSide * pinch.x + intoPalm * pinch.y + forward * pinch.z).normalized;
        }
        // Leave enough room for the pads and two card meshes between the digits.
        float thickness = Vector3.Distance(joints[1, 0].position, Wrist.position) * (profile?.PadSpacing ?? 0.19f);
        CloseThumb(index + normal * thickness);
        Vector3 thumb = Pad(0);
        Vector3 gripNormal = (thumb - index).normalized;
        if (profile != null)
            gripNormal = Vector3.Slerp(normal, gripNormal, 0.35f);
        Vector3 gripForward = Vector3.ProjectOnPlane(forward, gripNormal);
        var rotation = Quaternion.LookRotation(gripForward, gripNormal);
        Contact = new Pose(Wrist.InverseTransformPoint((thumb + index) * 0.5f),
            Quaternion.Inverse(Wrist.rotation) * rotation);

        for (int digit = 0; digit < Digits.Length; digit++)
            for (int joint = 0; joint < 3; joint++)
                closed[digit, joint] = joints[digit, joint].localRotation;
        SetClosure(1f);
        for (int digit = 0; digit < Digits.Length; digit++)
            for (int joint = 0; joint < 3; joint++)
                held[digit, joint] = joints[digit, joint].localRotation;
        SetClosure(0f);

        // These joint angles and the wrist-local contact come from the seated EmotionTest grip.
        // The ShowcaseCast avatars share the same finger bones but use a different idle pose.
        var references = usePreviewPose ? ReferencePoses.Value?.characters : null;
        var reference = references == null ? null : Array.Find(references, pose => pose.name == character);
        if (reference?.joints?.Length == held.Length && reference.wristScale > 0f)
        {
            Contact = new Pose(reference.contactPosition, reference.contactRotation);
            PreviewScale = Wrist.lossyScale.x / reference.wristScale;
            for (int digit = 0; digit < Digits.Length; digit++)
                for (int joint = 0; joint < 3; joint++)
                    held[digit, joint] = reference.joints[digit * 3 + joint];
        }
    }

    private static string CharacterFor(GameObject player)
    {
        string controller = player.GetComponent<Animator>().runtimeAnimatorController.name;
        // The imported ShowcaseCast avatars share the Presentation controller name.
        // Their meshes have the same vertex counts as the original character rigs.
        if (controller == "Presentation")
        {
            return player.GetComponentInChildren<SkinnedMeshRenderer>(true).sharedMesh.vertexCount switch
            {
                4268 => "Cat",
                4426 => "Cow",
                4421 => "Fox",
                4393 => "Owl",
                _ => null
            };
        }
        return controller switch
        {
            "CatAnimator" => "Cat",
            "CowAnimator" => "Cow",
            "FoxAnimator" => "Fox",
            "OwlAnimator" => "Owl",
            _ => null
        };
    }

    internal static Pose FanCardPose(Vector3 pinch, Quaternion facing, float spread, int side, float cardScale, float handScale)
    {
        Quaternion rotation = facing * Quaternion.AngleAxis(spread * side, Vector3.up);
        Vector3 offset = new(.012f * side * handScale, .00175f * side * handScale, .044f * cardScale);
        Vector3 position = pinch - facing * Vector3.right * (.026f * handScale) + rotation * offset;
        return new Pose(position, rotation);
    }

    internal void BlendHeldPose(float weight)
    {
        for (int digit = 0; digit < Digits.Length; digit++)
        {
            for (int joint = 0; joint < 2; joint++)
            {
                var bone = joints[digit, joint];
                bone.localRotation = Quaternion.Slerp(bone.localRotation, held[digit, joint], weight);
            }
            // The table rig animates the first two joints. Reset the fingertip when the grip releases.
            joints[digit, 2].localRotation = Quaternion.Slerp(open[digit, 2], held[digit, 2], weight);
        }
    }

    internal void SetClosure(float amount, float thumbRelease = 0f, float incomingCard = 0f)
    {
        PoseDigits(amount, amount * (1f - thumbRelease));
        // The pads are located inside the skin. Clear the card face with the visible thumb surface.
        float support = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(amount * 2.3f));
        SwingThumb((thumbClearanceAngle + thumbRelease * 5f) * support, Vector3.up);
        SwingThumb(26f * incomingCard, Vector3.up);
    }

    internal void SetFoldClosure(float amount, float thumbRelaxation, float clearance)
    {
        // Keep the thumb clear while the cards slide back onto the table, then relax it.
        float thumbClosure = 1f - thumbRelaxation;
        PoseDigits(amount, thumbClosure);
        SwingThumb(thumbClearanceAngle * thumbClosure, Vector3.up);
        SwingThumb(thumbReleaseAngle * clearance, ReleaseDirection);
    }

    private void PoseDigits(float amount, float thumbClosure)
    {
        for (int digit = 0; digit < Digits.Length; digit++)
        {
            float weight = digit == 0 ? thumbClosure : amount;
            for (int joint = 0; joint < 3; joint++)
                joints[digit, joint].localRotation = Quaternion.Slerp(open[digit, joint], closed[digit, joint], weight);
        }
    }

    private void SwingThumb(float angle, Vector3 direction)
    {
        if (angle <= 0f) return;
        Transform thumbBase = joints[0, 0];
        Vector3 away = Wrist.rotation * Contact.rotation * direction;
        Vector3 hinge = Vector3.Cross(Pad(0) - thumbBase.position, away);
        thumbBase.rotation = Quaternion.AngleAxis(angle, hinge) * thumbBase.rotation;
    }

    private Vector3 Pad(int digit) => Vector3.Lerp(joints[digit, 2].position, joints[digit, 3].position, 0.60f);

    private void CloseThumb(Vector3 target)
    {
        for (int pass = 0; pass < 8; pass++)
        {
            for (int joint = 2; joint >= 0; joint--)
            {
                var bone = joints[0, joint];
                var turn = Quaternion.FromToRotation(Pad(0) - bone.position, target - bone.position);
                bone.rotation = Quaternion.RotateTowards(Quaternion.identity, turn, 14f) * bone.rotation;
                bone.localRotation = Quaternion.RotateTowards(open[0, joint], bone.localRotation, 55f);
            }
        }
    }
}
