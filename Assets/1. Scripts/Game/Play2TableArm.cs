using UnityEngine;

internal static class Play2TableArm
{
    // Keep the cropped sleeve opening behind the camera frame while preserving elbow and wrist contact.
    internal static void AnchorViewSleeve(Transform shoulder, Transform elbow, Transform view, bool left)
    {
        Vector3 joint = elbow.position;
        Quaternion orientation = elbow.rotation;
        Vector3 bone = joint - shoulder.position;
        Vector3 shoulderHint = view.TransformPoint(new Vector3(left ? -.37f : .37f, -.52f, .11f));
        Vector3 direction = (joint - shoulderHint).normalized;
        shoulder.rotation = Quaternion.FromToRotation(bone, direction) * shoulder.rotation;
        shoulder.position = shoulderHint;
        elbow.position = joint;
        elbow.rotation = orientation;
    }

    // Travel of a flat palm along the felt, limited by the two bone lengths.
    internal static float ReachablePush(Transform shoulder, Transform elbow, Transform wrist,
        Vector3 palmStart, Vector3 travel, Vector3 wristToPad)
    {
        float distance = travel.magnitude;
        if (distance < .0001f) return 0f;
        Vector3 fromShoulder = palmStart - wristToPad - shoulder.position;
        Vector3 axis = travel / distance;
        float reach = Vector3.Distance(shoulder.position, elbow.position) + Vector3.Distance(elbow.position, wrist.position) - .002f;
        float along = Vector3.Dot(fromShoulder, axis);
        float sideSquared = Mathf.Max(0f, fromShoulder.sqrMagnitude - along * along);
        float furthest = Mathf.Sqrt(Mathf.Max(0f, reach * reach - sideSquared)) - along;
        return Mathf.Clamp01(furthest / distance);
    }

    internal static void Reach(Transform upperArm, Transform forearm, Transform hand, Vector3 target)
        => ReachPoint(upperArm, forearm, hand.position, target);

    internal static void ReachPoint(Transform upperArm, Transform forearm, Vector3 wrist, Vector3 target, Vector3 surfaceNormal = default, float normalWeight = 1f)
    {
        Vector3 shoulder = upperArm.position;
        Vector3 elbow = forearm.position;
        Vector3 tipInForearm = forearm.InverseTransformPoint(wrist);
        float upper = Vector3.Distance(shoulder, elbow);
        float lower = Vector3.Distance(elbow, wrist);
        Vector3 offset = target - shoulder;
        if (upper < .0001f || lower < .0001f || offset.sqrMagnitude < .000001f) return;
        Vector3 axis = offset.normalized;
        float reach = Mathf.Clamp(offset.magnitude, Mathf.Abs(upper - lower) + 0.001f, upper + lower - 0.001f);
        Vector3 bend = Vector3.ProjectOnPlane(elbow - shoulder, axis);
        if (bend.sqrMagnitude < .000001f) bend = Vector3.ProjectOnPlane(upperArm.right, axis);
        if (bend.sqrMagnitude < .000001f) bend = Vector3.ProjectOnPlane(upperArm.forward, axis);
        bend.Normalize();
        float along = (upper * upper - lower * lower + reach * reach) / (2f * reach);
        float radius = Mathf.Sqrt(Mathf.Max(0f, upper * upper - along * along));
        Vector3 circleCentre = shoulder + axis * along;
        if (surfaceNormal != Vector3.zero && radius > .0001f)
        {
            // Pick a point on the joint's reachable circle so the last phalanx lies along the paper.
            // Of the two possible bends, keep the one nearest the authored knuckle pose.
            Vector3 across = Vector3.ProjectOnPlane(surfaceNormal, axis);
            float span = across.magnitude;
            if (span > .0001f)
            {
                across /= span;
                float cosine = Mathf.Clamp(Vector3.Dot(target - circleCentre, surfaceNormal) / (radius * span), -1f, 1f);
                Vector3 tangent = Vector3.Cross(axis, across);
                if (Vector3.Dot(tangent, bend) < 0f) tangent = -tangent;
                Vector3 tangentBend = across * cosine + tangent * Mathf.Sqrt(1f - cosine * cosine);
                bend = Vector3.Slerp(bend, tangentBend, normalWeight).normalized;
            }
        }
        Vector3 nextElbow = circleCentre + bend * radius;
        upperArm.rotation = Quaternion.FromToRotation(elbow - shoulder, nextElbow - shoulder) * upperArm.rotation;
        forearm.rotation = Quaternion.FromToRotation(forearm.TransformPoint(tipInForearm) - forearm.position,
            shoulder + axis * reach - forearm.position) * forearm.rotation;
    }
}
