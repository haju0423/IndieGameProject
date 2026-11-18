using UnityEngine;

internal static class Play2TableArm
{
    internal static void Reach(Transform upperArm, Transform forearm, Transform hand, Vector3 target)
    {
        Vector3 shoulder = upperArm.position;
        Vector3 elbow = forearm.position;
        Vector3 wrist = hand.position;
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
        Vector3 nextElbow = shoulder + axis * along + bend * Mathf.Sqrt(Mathf.Max(0f, upper * upper - along * along));
        upperArm.rotation = Quaternion.FromToRotation(elbow - shoulder, nextElbow - shoulder) * upperArm.rotation;
        forearm.rotation = Quaternion.FromToRotation(hand.position - forearm.position,
            shoulder + axis * reach - forearm.position) * forearm.rotation;
    }
}
