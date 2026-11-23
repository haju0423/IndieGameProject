using System.Collections.Generic;
using UnityEngine;

// Keep a rigid copy of the visible head surface in bone space. An aimed wager
// queries it once, then the contact follows the animated head through the recoil.
internal sealed class Play2FaceContact
{
    private readonly Transform head;
    private readonly List<Vector3> surface = new();

    private readonly Vector3[] headVertices = System.Array.Empty<Vector3>();

    internal Play2FaceContact(SkinnedMeshRenderer skin, Transform head)
    {
        this.head = head;
        var mesh = skin != null ? skin.sharedMesh : null;
        if (mesh == null || !mesh.isReadable || head == null) return;

        var bones = skin.bones;
        var bind = mesh.bindposes;
        var attached = new bool[bones.Length];
        var toHead = new Matrix4x4[bones.Length];
        for (int i = 0; i < bones.Length; i++)
        {
            if (bones[i] == null) continue;
            attached[i] = bones[i] == head || bones[i].IsChildOf(head);
            toHead[i] = head.worldToLocalMatrix * bones[i].localToWorldMatrix * bind[i];
        }

        var vertices = mesh.vertices;
        var weights = mesh.boneWeights;
        if (weights.Length != vertices.Length) return;
        for (int i = 0; i < vertices.Length; i++)
        {
            var w = weights[i];
            if (!attached[w.boneIndex0]) continue;
            var v = vertices[i];
            vertices[i] = toHead[w.boneIndex0].MultiplyPoint3x4(v) * w.weight0
                + toHead[w.boneIndex1].MultiplyPoint3x4(v) * w.weight1
                + toHead[w.boneIndex2].MultiplyPoint3x4(v) * w.weight2
                + toHead[w.boneIndex3].MultiplyPoint3x4(v) * w.weight3;
        }
        var triangles = mesh.triangles;
        for (int i = 0; i < triangles.Length; i += 3)
        {
            int a = triangles[i], b = triangles[i + 1], c = triangles[i + 2];
            if (!attached[weights[a].boneIndex0] || !attached[weights[b].boneIndex0] || !attached[weights[c].boneIndex0]) continue;
            surface.Add(vertices[a]); surface.Add(vertices[b]); surface.Add(vertices[c]);
        }

        var unique = new HashSet<Vector3>(surface);
        headVertices = new Vector3[unique.Count];
        unique.CopyTo(headVertices);
    }

    internal bool TryLabelPoint(Camera camera, out Vector3 label)
    {
        label = default;
        if (headVertices.Length == 0) return false;
        label = camera.WorldToViewportPoint(head.position);

        // A horn can become the highest point between two sampled directions as the player stands.
        // Only the vertical clip coordinate and depth are needed to find the top of the rigid mask.
        var projection = camera.projectionMatrix * camera.worldToCameraMatrix * head.localToWorldMatrix;
        var up = projection.GetRow(1);
        var depth = projection.GetRow(3);
        float bestY = 0f, bestW = 0f;
        foreach (var v in headVertices)
        {
            float w = depth.x * v.x + depth.y * v.y + depth.z * v.z + depth.w;
            if (w <= 0f) continue;
            float y = up.x * v.x + up.y * v.y + up.z * v.z + up.w;
            if (bestW > 0f && y * bestW <= bestY * w) continue;
            bestY = y;
            bestW = w;
        }
        if (bestW <= 0f) return false;
        label.y = .5f * (bestY / bestW + 1f);
        return true;
    }

    internal Vector3 Touch(Vector3 from, Vector3 toward, Vector3 fallback, float clearance)
    {
        var delta = toward - from;
        if (surface.Count == 0 || delta.sqrMagnitude < .000001f) return fallback;
        var origin = head.InverseTransformPoint(from);
        var direction = head.InverseTransformVector(delta.normalized);
        float nearest = float.PositiveInfinity;
        for (int i = 0; i < surface.Count; i += 3)
        {
            Vector3 a = surface[i], ab = surface[i + 1] - a, ac = surface[i + 2] - a;
            Vector3 p = Vector3.Cross(direction, ac);
            float determinant = Vector3.Dot(ab, p);
            if (Mathf.Abs(determinant) < 1e-9f) continue;
            Vector3 start = origin - a;
            float u = Vector3.Dot(start, p) / determinant;
            if (u < 0f || u > 1f) continue;
            Vector3 q = Vector3.Cross(start, ab);
            float v = Vector3.Dot(direction, q) / determinant;
            if (v < 0f || u + v > 1f) continue;
            float distance = Vector3.Dot(ac, q) / determinant;
            if (distance > 0f && distance < nearest) nearest = distance;
        }
        return float.IsPositiveInfinity(nearest) ? fallback : from + delta.normalized * Mathf.Max(0f, nearest - clearance);
    }
}
