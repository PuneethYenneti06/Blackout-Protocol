using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

// The map has ceilings over some rooms only; the corridor and several rooms are open to the sky.
// This puts one flat roof over the whole floor at the height of the existing ceilings, slightly above
// them, so rooms that already have a ceiling look the same and the open parts get covered.
// Worked out from the map at runtime (walk map + upward rays), so nothing is added to the scene file.
public static class MapRoof
{
    public static bool enabled = true;
    public static bool castShadows = false;   // false = lighting inside stays as it was
    public static float padding = 1f;         // how far past the walkable floor the roof reaches, metres
    public static float fallbackHeight = 3f;  // used if no ceiling is found above the floor

    static int lastFrame = -1;

    // The roof object and the material of the existing ceiling it was matched to (MapTextures reads these)
    public static GameObject Roof { get; private set; }
    public static Material CeilingMaterial { get; private set; }
    // Walls closing off the open outer sides of the building (MapTextures gives them the wall texture)
    public static readonly List<GameObject> OuterWalls = new List<GameObject>();
    public static bool outerWalls = true;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Init()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        Build();
    }

    static void OnSceneLoaded(Scene s, LoadSceneMode m) => Build();

    public static void Build()
    {
        if (!enabled || Time.frameCount == lastFrame) return;
        lastFrame = Time.frameCount;

        NavMeshTriangulation tri = NavMesh.CalculateTriangulation();
        if (tri.vertices == null || tri.vertices.Length < 3) return;
        if (Roof != null) return;

        // Footprint of the walkable floor
        Vector3 min = tri.vertices[0], max = tri.vertices[0];
        List<float> floorYs = new List<float>(tri.vertices.Length);
        foreach (Vector3 v in tri.vertices)
        {
            min = Vector3.Min(min, v);
            max = Vector3.Max(max, v);
            floorYs.Add(v.y);
        }
        floorYs.Sort();
        float floorY = floorYs[floorYs.Count / 2];

        // Ceiling height: cast rays straight up from spots on the floor and take the most common height
        Dictionary<int, int> votes = new Dictionary<int, int>();
        Dictionary<int, Material> voteMaterial = new Dictionary<int, Material>();
        RaycastHit[] hits = new RaycastHit[8];
        int triangles = tri.indices.Length / 3;
        Random.State saved = Random.state;
        Random.InitState(4242);
        for (int s = 0; s < 400; s++)
        {
            int t = Random.Range(0, triangles);
            Vector3 p = (tri.vertices[tri.indices[t * 3]] + tri.vertices[tri.indices[t * 3 + 1]]
                       + tri.vertices[tri.indices[t * 3 + 2]]) / 3f;
            if (Mathf.Abs(p.y - floorY) > 0.5f) continue; // stairs and ramps
            int n = Physics.RaycastNonAlloc(p + Vector3.up * 0.3f, Vector3.up, hits, 8f, ~0, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue; Collider bestCol = null; int bestTri = -1;
            for (int i = 0; i < n; i++)
            {
                Collider c = hits[i].collider;
                if (c.GetComponentInParent<NavMeshAgent>() != null || c.GetComponentInParent<PlayerHealth>() != null
                    || c.attachedRigidbody != null) continue;
                if (hits[i].distance < best) { best = hits[i].distance; bestCol = c; bestTri = hits[i].triangleIndex; }
            }
            if (bestCol == null) continue;
            float h = p.y + 0.3f + best - floorY;
            if (h < 2.2f || h > 6f) continue;
            int key = Mathf.RoundToInt(h * 20f); // 5 cm buckets
            votes.TryGetValue(key, out int count);
            votes[key] = count + 1;
            if (!voteMaterial.ContainsKey(key)) voteMaterial[key] = MaterialAt(bestCol, bestTri);
        }
        Random.state = saved;

        float height = fallbackHeight;
        Material source = null;
        int bestVotes = 0;
        foreach (var kv in votes)
            if (kv.Value > bestVotes) { bestVotes = kv.Value; height = kv.Key / 20f; source = voteMaterial[kv.Key]; }

        float topY = max.y;
        if (topY > floorY + height - 1f)
            Debug.LogWarning($"[MapRoof] Walk map reaches {topY - floorY:0.00} m above the floor (stairs?); the roof may block it.");

        // The roof: a thin slab just above the existing ceilings
        GameObject roof = GameObject.CreatePrimitive(PrimitiveType.Cube);
        roof.name = "Map Roof";
        float thickness = 0.2f;
        roof.transform.position = new Vector3((min.x + max.x) * 0.5f, floorY + height + 0.02f + thickness * 0.5f, (min.z + max.z) * 0.5f);
        roof.transform.localScale = new Vector3(max.x - min.x + padding * 2f, thickness, max.z - min.z + padding * 2f);

        MeshRenderer mr = roof.GetComponent<MeshRenderer>();
        if (source != null) mr.sharedMaterial = source;
        else
        {
            Shader lit = Shader.Find("Universal Render Pipeline/Lit");
            if (lit != null)
            {
                Material m = new Material(lit);
                m.SetColor("_BaseColor", new Color(0.78f, 0.78f, 0.76f));
                mr.sharedMaterial = m;
            }
        }
        Roof = roof;

        // Outer walls along the roof's edges, from below the floor up to the roof, so no side is open
        OuterWalls.Clear();
        if (outerWalls)
        {
            Vector3 c = roof.transform.position;
            Vector3 size = roof.transform.localScale;
            float bottom = floorY - 0.5f;
            float top = c.y + thickness * 0.5f;
            float h = top - bottom;
            float wt = 0.3f;
            void Wall(string name, Vector3 center, Vector3 scale)
            {
                GameObject w = GameObject.CreatePrimitive(PrimitiveType.Cube);
                w.name = name;
                w.transform.position = center;
                w.transform.localScale = scale;
                if (source != null) w.GetComponent<MeshRenderer>().sharedMaterial = source;
                OuterWalls.Add(w);
            }
            float midY = bottom + h * 0.5f;
            Wall("Map Outer Wall N", new Vector3(c.x, midY, c.z + size.z * 0.5f + wt * 0.5f), new Vector3(size.x + wt * 2f, h, wt));
            Wall("Map Outer Wall S", new Vector3(c.x, midY, c.z - size.z * 0.5f - wt * 0.5f), new Vector3(size.x + wt * 2f, h, wt));
            Wall("Map Outer Wall E", new Vector3(c.x + size.x * 0.5f + wt * 0.5f, midY, c.z), new Vector3(wt, h, size.z));
            Wall("Map Outer Wall W", new Vector3(c.x - size.x * 0.5f - wt * 0.5f, midY, c.z), new Vector3(wt, h, size.z));
        }
        CeilingMaterial = source;
        mr.shadowCastingMode = castShadows ? ShadowCastingMode.On : ShadowCastingMode.Off;

        Debug.Log($"[MapRoof] Roof at {height:0.00} m above the floor (y {floorY:0.00}), {bestVotes} ceiling hits, " +
                  $"size {roof.transform.localScale.x:0.0} x {roof.transform.localScale.z:0.0} m, material {(source != null ? source.name : "plain grey")}");
    }

    // The material of the part of the mesh that was hit (meshes with several materials have one per sub-mesh)
    static Material MaterialAt(Collider c, int triangle)
    {
        Renderer r = c.GetComponent<Renderer>();
        if (r == null) return null;
        Material[] mats = r.sharedMaterials;
        if (mats.Length <= 1 || triangle < 0) return mats.Length > 0 ? mats[0] : null;
        MeshCollider mc = c as MeshCollider;
        if (mc == null || mc.sharedMesh == null) return mats[0];
        Mesh mesh = mc.sharedMesh;
        int index = triangle * 3;
        for (int s = 0; s < mesh.subMeshCount && s < mats.Length; s++)
        {
            var sm = mesh.GetSubMesh(s);
            if (index >= sm.indexStart && index < sm.indexStart + sm.indexCount) return mats[s];
        }
        return mats[0];
    }
}
