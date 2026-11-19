using System;
using UnityEngine;
using UnityEngine.SceneManagement;

// Chip collisions live in their own metre-scale table. They never enter the room's betting state.
internal sealed class Play2WagerFelt
{
    private const int Capacity = 40;
    private readonly Transform[] tokens = new Transform[Capacity];
    private readonly Rigidbody[] bodies = new Rigidbody[Capacity];
    private readonly Vector3[] origins = new Vector3[Capacity];
    private readonly Quaternion[] rotations = new Quaternion[Capacity];
    private readonly Scene scene;
    private readonly PhysicsScene physics;
    private readonly Func<int, Transform> createToken;
    private readonly PhysicsMaterial felt, clay;
    private readonly Mesh hull;
    private int next;
    internal int Count { get; private set; }

    internal Play2WagerFelt(Func<int, Transform> createToken, Bounds chipBounds, Play2TableAudio sound)
    {
        this.createToken = createToken;
        hull = CreateHull(chipBounds);
        scene = SceneManager.CreateScene("Wager felt " + Guid.NewGuid().ToString("N"),
            new CreateSceneParameters(LocalPhysicsMode.Physics3D));
        physics = scene.GetPhysicsScene();
        felt = new PhysicsMaterial("Table felt") { dynamicFriction = .52f, staticFriction = .60f, bounciness = .12f };
        clay = new PhysicsMaterial("Clay chip") { dynamicFriction = .32f, staticFriction = .52f, bounciness = .23f };
        var floor = new GameObject("Betting surface");
        SceneManager.MoveGameObjectToScene(floor, scene);
        var plane = floor.AddComponent<BoxCollider>();
        plane.size = new Vector3(2.8f, .04f, 2.8f);
        plane.center = Vector3.down * .009f;
        plane.sharedMaterial = felt;
        plane.contactOffset = .0006f;
        floor.AddComponent<Play2FeltImpact>().Sound = sound;
    }

    // The release mesh discards its CPU data. Use the same dimensions for a small collision hull.
    private static Mesh CreateHull(Bounds bounds)
    {
        const int sides = 16;
        var vertices = new Vector3[sides * 2];
        var triangles = new int[(sides * 2 + (sides - 2) * 2) * 3];
        int index = 0;
        for (int i = 0; i < sides; i++)
        {
            float angle = i * 2f * Mathf.PI / sides;
            Vector3 rim = bounds.center + new Vector3(Mathf.Cos(angle) * bounds.extents.x, 0, Mathf.Sin(angle) * bounds.extents.z);
            vertices[i] = rim - Vector3.up * bounds.extents.y;
            vertices[i + sides] = rim + Vector3.up * bounds.extents.y;
            int n = (i + 1) % sides;
            triangles[index++] = i; triangles[index++] = i + sides; triangles[index++] = n + sides;
            triangles[index++] = i; triangles[index++] = n + sides; triangles[index++] = n;
            if (i == 0 || i == sides - 1) continue;
            triangles[index++] = 0; triangles[index++] = i; triangles[index++] = n;
            triangles[index++] = sides; triangles[index++] = n + sides; triangles[index++] = i + sides;
        }
        var mesh = new Mesh { name = "Clay chip collision hull", vertices = vertices, triangles = triangles };
        mesh.RecalculateBounds();
        return mesh;
    }

    internal void Throw(Vector3 position, Quaternion rotation, Vector3 velocity, Vector3 spin)
    {
        int i = next;
        next = (next + 1) % Capacity;
        if (tokens[i] == null)
        {
            tokens[i] = createToken(i);
            var proxy = new GameObject("Physical wager " + i);
            SceneManager.MoveGameObjectToScene(proxy, scene);
            var shape = proxy.AddComponent<MeshCollider>();
            shape.convex = true;
            shape.sharedMesh = hull;
            shape.sharedMaterial = clay;
            shape.contactOffset = .0006f;
            var body = proxy.AddComponent<Rigidbody>();
            body.mass = .012f;
            body.linearDamping = .55f;
            body.angularDamping = 1.5f;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            body.maxAngularVelocity = 28f;
            bodies[i] = body;
        }

        var rb = bodies[i];
        rb.gameObject.SetActive(true);
        rb.isKinematic = false;
        rb.position = position;
        rb.rotation = rotation;
        rb.linearVelocity = velocity;
        rb.angularVelocity = spin;
        rb.WakeUp();
        tokens[i].SetLocalPositionAndRotation(position, rotation);
        tokens[i].gameObject.SetActive(true);
        Count = Mathf.Min(Count + 1, Capacity);
    }

    internal void Step(float dt)
    {
        if (Count == 0) return;
        physics.Simulate(dt);
        for (int i = 0; i < Count; i++)
        {
            var rb = bodies[i];
            // At the cushion the horizontal component is reflected back onto the round felt.
            Vector3 p = rb.position;
            Vector3 radial = Vector3.ProjectOnPlane(p, Vector3.up);
            if (p.y < .06f && radial.sqrMagnitude > .60f * .60f)
            {
                var n = radial.normalized;
                p = n * .598f + Vector3.up * Mathf.Max(.015f, p.y);
                rb.position = p;
                var v = rb.linearVelocity;
                rb.linearVelocity = v - 1.35f * Mathf.Max(0f, Vector3.Dot(v, n)) * n;
            }
            tokens[i].SetLocalPositionAndRotation(p, rb.rotation);
        }
    }

    internal void BeginSweep()
    {
        for (int i = 0; i < Count; i++)
        {
            origins[i] = tokens[i].localPosition;
            rotations[i] = tokens[i].localRotation;
            bodies[i].gameObject.SetActive(false);
        }
    }

    internal void Sweep(float t, int slots, Func<int, Vector3> stack)
    {
        for (int i = 0; i < Count; i++)
        {
            float slide = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((t - (i % 4) * .04f) / .88f));
            int slot = Mathf.Max(0, slots - Count + i);
            tokens[i].SetLocalPositionAndRotation(Vector3.Lerp(origins[i], stack(slot), slide),
                Quaternion.Slerp(rotations[i], Quaternion.Euler(0, 37 * i, 0), slide));
        }
    }

    internal void Clear()
    {
        for (int i = 0; i < Count; i++)
        {
            bodies[i].gameObject.SetActive(false);
            tokens[i].gameObject.SetActive(false);
        }
        Count = next = 0;
    }

    internal void Release()
    {
        if (scene.IsValid() && scene.isLoaded) SceneManager.UnloadSceneAsync(scene);
        UnityEngine.Object.Destroy(hull);
        UnityEngine.Object.Destroy(felt);
        UnityEngine.Object.Destroy(clay);
    }
}

// The collider is in the isolated chip physics scene. It reports only real contacts.
internal sealed class Play2FeltImpact : MonoBehaviour
{
    internal Play2TableAudio Sound { private get; set; }

    private void OnCollisionEnter(Collision contact)
    {
        if (Sound != null) Sound.ChipLand(contact.relativeVelocity.magnitude);
    }
}
