using UnityEngine;

// A private, flexible copy of this card's printed surfaces. The far edge stays on the felt.
internal sealed class Play2CardPeel
{
    private static readonly Vector3 CurlDirection = new Vector3(-.22f, 0f, .9755f).normalized;
    private const int Columns = 8, Rows = 16;
    private const int FaceVertices = Columns * Rows * 4;
    private readonly Mesh mesh;
    private readonly MeshRenderer bent;
    private readonly MeshRenderer[] flat;
    private readonly Vector3[] sheet = new Vector3[FaceVertices];
    private readonly Vector3[] vertices = new Vector3[2 * FaceVertices];
    private readonly Vector3[] normals = new Vector3[2 * FaceVertices];
    private float previous = -1f;
    internal float Bend => bent.enabled ? previous : 0f;

    internal Play2CardPeel(Transform card, Material curvedPaper)
    {
        var front = card.Find("Front");
        var back = card.Find("Back");
        flat = new[] { front.GetComponent<MeshRenderer>(), back.GetComponent<MeshRenderer>(),
            card.Find("Body").GetComponent<MeshRenderer>() };
        var surface = new GameObject("Private corner peek");
        surface.transform.SetParent(card, false);
        mesh = new Mesh { name = "Pocket card corner" };
        mesh.MarkDynamic();
        surface.AddComponent<MeshFilter>().sharedMesh = mesh;
        bent = surface.AddComponent<MeshRenderer>();
        bent.sharedMaterials = new[] { curvedPaper, flat[1].sharedMaterial, flat[2].sharedMaterial };
        bent.enabled = false;
        var atlas = front.GetComponent<MeshFilter>().sharedMesh.uv;
        var uv = new Vector2[vertices.Length];
        var faceIndices = new int[Columns * Rows * 6];
        var backIndices = new int[faceIndices.Length];
        var edges = new int[(Columns + Rows) * 12];
        int nextEdge = 0;
        for (int row = 0; row < Rows; row++)
        for (int col = 0; col < Columns; col++)
        {
            int tile = row * Columns + col;
            int start = tile * 4;
            bool split = atlas.Length == 8;
            int group = split && row < Rows / 2 ? 4 : 0;
            for (int corner = 0; corner < 4; corner++)
            {
                int i = start + corner;
                float u = (col + (corner >= 2 ? 1f : 0f)) / Columns;
                float v = (row + (corner == 1 || corner == 2 ? 1f : 0f)) / Rows;
                float sectionV = split ? v * 2f - (group == 0 ? 1f : 0f) : v;
                sheet[i] = new Vector3((u - .5f) * .084f, 0, (v - .5f) * .12f);
                uv[i] = Vector2.Lerp(Vector2.Lerp(atlas[group], atlas[group + 3], u),
                    Vector2.Lerp(atlas[group + 1], atlas[group + 2], u), sectionV);
                uv[i + FaceVertices] = new Vector2(u, 1f - v);
            }
            if (col == 0) AddEdge(start, start + 1);
            if (row == Rows - 1) AddEdge(start + 1, start + 2);
            if (col == Columns - 1) AddEdge(start + 2, start + 3);
            if (row == 0) AddEdge(start + 3, start);
            int at = tile * 6;
            faceIndices[at] = start; faceIndices[at + 1] = start + 1; faceIndices[at + 2] = start + 2;
            faceIndices[at + 3] = start; faceIndices[at + 4] = start + 2; faceIndices[at + 5] = start + 3;
            for (int j = 0; j < 6; j++)
                backIndices[at + j] = FaceVertices + faceIndices[at + (j / 3) * 3 + 2 - j % 3];
        }
        mesh.vertices = vertices;
        mesh.uv = uv;
        mesh.subMeshCount = 3;
        mesh.SetTriangles(faceIndices, 0);
        mesh.SetTriangles(backIndices, 1);
        mesh.SetTriangles(edges, 2);

        void AddEdge(int a, int b)
        {
            edges[nextEdge++] = a;
            edges[nextEdge++] = a + FaceVertices;
            edges[nextEdge++] = b + FaceVertices;
            edges[nextEdge++] = a;
            edges[nextEdge++] = b + FaceVertices;
            edges[nextEdge++] = b;
        }
    }

    internal static Vector3 BentPoint(Vector3 point, float amount, out Vector3 normal)
    {
        // The sheet rolls around one diagonal axis. Every strip keeps its printed length
        // and the thumb supports a short, readable lip at the free corner.
        float distance = Vector3.Dot(point, CurlDirection) - .013f;
        float curvature = 74f * amount;
        if (distance <= 0f || curvature < .02f)
        {
            normal = Vector3.up;
            return point;
        }
        float arc = Mathf.Min(distance, .032f);
        float lip = distance - arc;
        float angle = curvature * arc;
        float sin = Mathf.Sin(angle), cos = Mathf.Cos(angle);
        normal = Vector3.up * cos + CurlDirection * sin;
        return point + CurlDirection * (sin / curvature + lip * cos - distance)
            - Vector3.up * ((1f - cos) / curvature + lip * sin);
    }

    internal void SetBend(float amount)
    {
        bool active = amount > .001f;
        if (active != bent.enabled)
        {
            bent.enabled = active;
            foreach (var surface in flat) surface.enabled = !active;
        }
        if (!active || Mathf.Approximately(amount, previous)) return;
        previous = amount;
        for (int i = 0; i < sheet.Length; i++)
        {
            var center = BentPoint(sheet[i], amount, out var normal);
            vertices[i] = center + normal * .00055f;
            vertices[i + FaceVertices] = center - normal * .00055f;
            normals[i] = normal;
            normals[i + FaceVertices] = -normal;
        }
        mesh.vertices = vertices;
        mesh.normals = normals;
        mesh.RecalculateBounds();
    }

    internal void Release() => Object.Destroy(mesh);
}
