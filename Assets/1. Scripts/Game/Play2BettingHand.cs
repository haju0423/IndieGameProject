using UnityEngine;

internal enum Play2Gesture { Wave, Rude, Point, ThumbsDown, Despair, Celebrate }

// Cards stay in the other hand. The chip is released from the same contact used by the fingers.
internal sealed class Play2BettingHand
{
    private readonly Transform upper, lower, wrist, camera;
    private readonly Play2FingerRig fingers;
    private readonly Pose contact;
    private readonly Quaternion upperRest, lowerRest, wristRest;
    private readonly GameObject viewModel;
    private readonly bool left;
    private readonly Vector3 shoulderLocal, elbowLocal;
    internal const float PickupTime = .16f, ReleaseTime = .36f, TossEnd = .78f;
    private Vector3 start, release, restingPad;
    private Quaternion facing, restingWrist;

    internal Play2BettingHand(GameObject actor, Transform view)
    {
        camera = view;
        left = !Play2CardGrip.UsesLeftHand;
        if (view != null)
        {
            viewModel = Object.Instantiate(actor, view, false);
            if (actor.TryGetComponent<Play2FingerRig>(out var original))
                viewModel.GetComponent<Play2FingerRig>().BindViewCopy(original);
            viewModel.name = "First Person Betting Arm";
            viewModel.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            viewModel.transform.localScale = actor.transform.lossyScale * .9f;
            actor = viewModel;
        }
        var animator = actor.GetComponent<Animator>();
        upper = animator.GetBoneTransform(left ? HumanBodyBones.LeftUpperArm : HumanBodyBones.RightUpperArm);
        lower = animator.GetBoneTransform(left ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm);
        wrist = animator.GetBoneTransform(left ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
        shoulderLocal = upper.localPosition;
        elbowLocal = lower.localPosition;
        upperRest = upper.localRotation;
        lowerRest = lower.localRotation;
        wristRest = wrist.localRotation;
        fingers = Play2FingerRig.Attach(animator);
        fingers.SetCardGrip(left, 1);
        contact = fingers.CardContact(left, wrist);
        fingers.SetCardGrip(left, 0);
        if (viewModel != null)
        {
            animator.enabled = false;
            var armSkin = actor.GetComponentInChildren<SkinnedMeshRenderer>();
            fingers.PrepareViewMesh(armSkin);
            Play2CardGrip.ShowArmOnly(armSkin, upper);
            viewModel.SetActive(false);
        }
    }

    internal void Begin(Vector3 startPoint, Vector3 releasePoint)
    {
        start = startPoint;
        release = releasePoint;
        facing = Quaternion.LookRotation(Vector3.ProjectOnPlane(release - start, Vector3.up), Vector3.up);
        restingPad = Contact;
        restingWrist = wrist.rotation;
        if (viewModel != null) viewModel.SetActive(true);
    }

    internal Vector3 Contact => wrist.TransformPoint(contact.position);
    internal Quaternion PinchRotation => wrist.rotation * contact.rotation;

    internal void Pose(float time, bool aimed)
    {
        // Start below the frame (or in the seated idle pose), then pinch the chip on the felt.
        const float windupEnd = .23f, followEnd = .44f;
        Vector3 home = camera != null ? ViewPoint(new Vector3(.29f, -.48f, .23f)) : restingPad;
        Vector3 direction = facing * Vector3.forward;
        Vector3 windup = start - direction * .018f + Vector3.up * (aimed ? .047f : .025f);
        float approach = Mathf.SmoothStep(0f, 1f, time / PickupTime);
        float lift = Mathf.SmoothStep(0f, 1f, (time - PickupTime) / (windupEnd - PickupTime));
        float flick = Mathf.Clamp01((time - windupEnd) / (ReleaseTime - windupEnd));
        float follow = Mathf.Clamp01((time - ReleaseTime) / (followEnd - ReleaseTime));
        float recover = Mathf.SmoothStep(0f, 1f, (time - followEnd) / (TossEnd - followEnd));

        Vector3 point = Vector3.Lerp(home, start, approach);
        point += Vector3.up * (.028f * Mathf.Sin(approach * Mathf.PI));
        point += (windup - start) * lift + (release - windup) * (flick * flick);
        // Continue through the release at the same speed, then ease the wrist back to idle.
        point += (release - windup) * ((followEnd - ReleaseTime) / (ReleaseTime - windupEnd))
            * (follow * (2f - follow));
        point = Vector3.Lerp(point, home, recover);
        float curl = Mathf.SmoothStep(0f, 1f, (time - .08f) / (PickupTime - .08f))
            * (1f - Mathf.SmoothStep(0f, 1f, (time - ReleaseTime + .012f) / .090f));
        float pitch = 9f * lift - (aimed ? 26f : 18f) * flick;
        Quaternion rotation = facing * Quaternion.Euler(pitch, 0f, 0f) * Quaternion.Inverse(contact.rotation);
        if (camera == null)
            rotation = Quaternion.Slerp(restingWrist, rotation, approach * (1f - recover));
        PlacePalm(point, rotation, curl);
    }

    internal void BeginCheek()
    {
        restingPad = Contact;
        restingWrist = wrist.rotation;
        if (viewModel != null) viewModel.SetActive(true);
    }

    internal void RubCheek(Vector3 cheek, Transform eye, float elapsed)
    {
        float reach = Mathf.SmoothStep(0f, 1f, (elapsed - .07f) / .27f);
        float recover = Mathf.SmoothStep(0f, 1f, (elapsed - .62f) / .26f);
        float weight = reach * (1f - recover);
        Vector3 toward = Vector3.ProjectOnPlane(eye.forward, Vector3.up).normalized;
        Vector3 side = Vector3.Cross(Vector3.up, toward) * (left ? -1f : 1f);
        float rub = Mathf.Sin(Mathf.Clamp01((elapsed - .32f) / .31f) * Mathf.PI * 2f);
        cheek += (Vector3.up * .013f + toward * .009f) * rub;
        Vector3 home = camera != null ? ViewPoint(new Vector3(.31f, -.47f, .23f)) : restingPad;
        Quaternion palm = fingers.PalmRotation(left, Vector3.up, -side);
        PlacePalm(Vector3.Lerp(home, cheek, weight), Quaternion.Slerp(restingWrist, palm, weight), .18f * weight, fingertips: true);
    }

    internal float ReachablePush(Vector3 startPad, Vector3 travel)
    {
        Quaternion down = fingers.PalmRotation(left, facing * Vector3.forward, Vector3.down);
        Vector3 offset = down * Vector3.Scale(fingers.PushContact(left), wrist.lossyScale);
        return Play2TableArm.ReachablePush(upper, lower, wrist, startPad, travel, offset);
    }

    internal void Push(Vector3 point, float time)
    {
        float approach = Mathf.SmoothStep(0, 1, Mathf.Clamp01(time / .18f));
        float withdraw = Mathf.SmoothStep(0, 1, Mathf.Clamp01((time - Play2TableChips.PushEnd) / .29f));
        Quaternion down = fingers.PalmRotation(left, facing * Vector3.forward, Vector3.down);
        Vector3 home = camera != null ? ViewPoint(new Vector3(.31f, -.48f, .16f)) : restingPad;
        Quaternion rest = camera != null ? down : restingWrist;
        float weight = approach * (1f - withdraw);
        PlacePalm(Vector3.Lerp(home, point, weight), Quaternion.Slerp(rest, down, weight), .32f * weight, true);
    }

    private void PlacePalm(Vector3 point, Quaternion rotation, float curl, bool push = false, bool fingertips = false)
    {
        fingers.SetCardGrip(left, curl);
        Vector3 pad = fingertips ? fingers.FingerPad(left, 2, wrist) : push ? fingers.PushContact(left) : contact.position;
        Vector3 target = point - rotation * Vector3.Scale(pad, wrist.lossyScale);
        if (camera != null)
        {
            PlaceViewArm(target, rotation, true, true);
        }
        else
        {
            upper.localRotation = upperRest;
            lower.localRotation = lowerRest;
            Play2TableArm.Reach(upper, lower, wrist, target);
        }
        wrist.rotation = rotation;
    }

    internal void Rest()
    {
        if (wrist == null) return;
        upper.localRotation = upperRest;
        lower.localRotation = lowerRest;
        wrist.localRotation = wristRest;
        fingers.SetCardGrip(left, 0);
        if (viewModel != null) viewModel.SetActive(false);
    }

    internal void Gesture(Vector3 target, Vector3 origin, float elapsed, Play2Gesture gesture)
    {
        float rise = Mathf.SmoothStep(0, 1, Mathf.Clamp01(elapsed / .22f));
        float fall = Mathf.SmoothStep(0, 1, Mathf.Clamp01((elapsed - 1.05f) / .28f));
        float weight = rise * (1 - fall);
        Vector3 toward = Vector3.ProjectOnPlane(target - origin, Vector3.up).normalized;
        Vector3 point = origin + toward * .12f + Vector3.up * (.19f * weight);
        if (camera != null)
            point = ViewPoint(Vector3.Lerp(new Vector3(.30f, -.43f, .29f), new Vector3(.20f, -.14f, .53f), weight));
        if (gesture == Play2Gesture.Point)
        {
            if (camera == null) point = origin + toward * (.22f * weight) + Vector3.up * (.12f * weight);
        }
        else if (gesture == Play2Gesture.ThumbsDown)
        {
            if (camera == null) point = origin + toward * .24f + Vector3.up * (.15f * weight);
            else point = ViewPoint(Vector3.Lerp(new Vector3(.30f, -.43f, .29f), new Vector3(.20f, -.10f, .56f), weight));
        }
        else if (gesture == Play2Gesture.Celebrate)
        {
            // Draw a fist towards the chest, with one short upward pump.
            float pump = Mathf.Sin(Mathf.Clamp01((elapsed - .22f) / .46f) * Mathf.PI);
            point = camera != null
                ? ViewPoint(Vector3.Lerp(new Vector3(.32f, -.42f, .33f),
                    new Vector3(.235f, -.13f + .045f * pump, .50f), weight))
                : upper.position + toward * .14f + Vector3.Cross(Vector3.up, toward) * (left ? -.075f : .075f)
                    + Vector3.up * (.055f + .060f * pump);
        }
        var rotation = fingers.PalmRotation(left, camera == null ? Vector3.up : camera.up,
            camera == null ? toward : camera.forward);
        if (gesture == Play2Gesture.Point)
        {
            Vector3 direction = target - point;
            Vector3 backFacing = camera == null ? Vector3.up : camera.up - camera.forward * .3f;
            rotation = fingers.PointingRotation(left, direction, backFacing);
        }
        else if (gesture == Play2Gesture.Rude)
            rotation = fingers.PalmRotation(left, Vector3.up, -toward);
        else if (gesture == Play2Gesture.ThumbsDown)
            rotation = fingers.ThumbsDownRotation(left, camera == null ? toward : camera.forward);
        var wristTarget = point - rotation * Vector3.Scale(contact.position, wrist.lossyScale);
        if (viewModel != null)
        {
            viewModel.SetActive(weight > 0);
            PlaceViewArm(wristTarget, rotation, gesture == Play2Gesture.ThumbsDown);
        }
        else
        {
            upper.localRotation = upperRest;
            lower.localRotation = lowerRest;
            wrist.localRotation = wristRest;
            wristTarget = Vector3.Lerp(wrist.position, wristTarget, weight);
            rotation = Quaternion.Slerp(wrist.rotation, rotation, weight);
            Play2TableArm.Reach(upper, lower, wrist, wristTarget);
        }
        wrist.rotation = rotation;
        for (int finger = 0; finger < fingers.FingerCount(left); finger++)
        {
            bool curl = gesture == Play2Gesture.Rude && finger != 2
                || gesture == Play2Gesture.Point && finger != 1
                || gesture == Play2Gesture.ThumbsDown && finger != 0 || gesture == Play2Gesture.Celebrate;
            fingers.SetGestureCurl(left, finger, curl ? weight : 0);
        }
        if (gesture == Play2Gesture.Rude) fingers.ExtendFinger(left, 2, weight);
        else if (gesture == Play2Gesture.Point) fingers.ExtendFinger(left, 1, weight);
        if (gesture == Play2Gesture.ThumbsDown) fingers.ExtendThumb(left, weight);
        else if (gesture == Play2Gesture.Rude || gesture == Play2Gesture.Point) fingers.FoldThumb(left, weight, gesture == Play2Gesture.Rude);
        if (gesture == Play2Gesture.Wave)
        {
            for (int finger = 1; finger < fingers.FingerCount(left); finger++) fingers.ExtendFinger(left, finger, weight);
            wrist.rotation = Quaternion.AngleAxis(Mathf.Sin(elapsed * 15) * 16f * weight, toward) * wrist.rotation;
        }
    }

    private void PlaceViewArm(Vector3 target, Quaternion rotation, bool fromSide = false, bool alignSleeve = false)
    {
        upper.SetLocalPositionAndRotation(shoulderLocal, upperRest);
        lower.SetLocalPositionAndRotation(elbowLocal, lowerRest);
        Vector3 elbow = fromSide ? new Vector3(.48f, -.43f, .22f) : new Vector3(.28f, -.48f, .12f);
        Quaternion neutralForearm = rotation * Quaternion.Inverse(wristRest);
        Vector3 neutralDirection = neutralForearm * wrist.localPosition.normalized;
        Vector3 towardHand = target - ViewPoint(elbow);
        Vector3 armDirection = alignSleeve ? towardHand.normalized : Vector3.RotateTowards(neutralDirection, towardHand.normalized, 40f * Mathf.Deg2Rad, 0f);
        Quaternion armRotation = Quaternion.FromToRotation(neutralDirection, armDirection) * neutralForearm;
        Transform body = viewModel.transform;
        body.rotation = armRotation * Quaternion.Inverse(lower.rotation) * body.rotation;
        body.position += target - wrist.position;
        Play2TableArm.AnchorViewSleeve(upper, lower, camera, left);
    }

    private Vector3 ViewPoint(Vector3 local)
    {
        if (left) local.x = -local.x;
        return camera.TransformPoint(local);
    }

    internal void Release()
    {
        Rest();
        if (viewModel != null) Object.Destroy(viewModel);
    }
}
