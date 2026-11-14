using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;

// Indoor lighting for the map, set up when the game starts (nothing is added to the scene file):
// - the sun is turned way down, since the building now has a roof
// - a warm ceiling light is placed at every light fixture in the map (or on a grid if none are found)
// - darker indoor ambient light and a light haze (fog), so far corridors fade into gloom
// The power cut (BlackoutDirector) turns these lights off and back on like the others.
// Called by MapTextures after the roof is built.
public static class MapLighting
{
    public static bool enabled = true;

    // Ceiling lights
    public static Color lampColor = new Color(1f, 0.92f, 0.80f);
    public static float lampIntensity = 2.2f;
    public static float lampRange = 8f;
    public static float lampSpacing = 2.5f;     // fixtures closer than this share one light
    public static float gridSpacing = 6f;       // used when the map has no light fixtures
    public static int maxLamps = 160;

    // Sun, ambient, fog
    public static float sunIntensity = 0.15f;
    public static Color ambientSky = new Color(0.17f, 0.18f, 0.21f);
    public static Color ambientEquator = new Color(0.10f, 0.10f, 0.11f);
    public static Color ambientGround = new Color(0.05f, 0.05f, 0.05f);
    public static Color fogColor = new Color(0.07f, 0.075f, 0.09f);
    public static float fogDensity = 0.03f;

    public static void Apply()
    {
        if (!enabled) return;
        if (GameObject.Find("Map Lights") != null) return;

        // Sun: dim, the inside is lit by the ceiling lights now
        foreach (Light l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
            if (l.type == LightType.Directional) l.intensity = Mathf.Min(l.intensity, sunIntensity);

        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = ambientSky;
        RenderSettings.ambientEquatorColor = ambientEquator;
        RenderSettings.ambientGroundColor = ambientGround;
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.ExponentialSquared;
        RenderSettings.fogColor = fogColor;
        RenderSettings.fogDensity = fogDensity;

        // Ceiling height from the roof
        float ceilingY = float.NaN;
        if (MapRoof.Roof != null) ceilingY = MapRoof.Roof.GetComponent<Renderer>().bounds.min.y;

        // Light fixtures in the map: objects using the "light" material
        List<Vector3> spots = new List<Vector3>();
        foreach (MeshRenderer r in Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
        {
            if (r.GetComponentInParent<NavMeshAgent>() != null || r.GetComponentInParent<PlayerHealth>() != null) continue;
            bool isLamp = false;
            foreach (Material m in r.sharedMaterials)
                if (MapTextures.BaseName(m) == "light") { isLamp = true; break; }
            if (!isLamp) continue;
            Bounds b = r.bounds;
            if (b.size.magnitude > 6f) continue; // not a fixture
            AddSpot(spots, new Vector3(b.center.x, b.min.y - 0.3f, b.center.z), lampSpacing);
        }
        string source = "light fixtures";

        // No fixtures: a light every few metres over the walkable floor
        if (spots.Count < 5)
        {
            spots.Clear();
            source = "grid";
            NavMeshTriangulation tri = NavMesh.CalculateTriangulation();
            if (tri.indices != null)
            {
                for (int t = 0; t + 2 < tri.indices.Length; t += 3)
                {
                    Vector3 p = (tri.vertices[tri.indices[t]] + tri.vertices[tri.indices[t + 1]] + tri.vertices[tri.indices[t + 2]]) / 3f;
                    float y = float.IsNaN(ceilingY) ? p.y + 2.7f : ceilingY - 0.35f;
                    if (y - p.y < 1.8f || y - p.y > 6f) continue;
                    AddSpot(spots, new Vector3(p.x, y, p.z), gridSpacing);
                }
            }
        }

        GameObject parent = new GameObject("Map Lights");
        int made = 0;
        foreach (Vector3 s in spots)
        {
            if (made >= maxLamps) break;
            GameObject go = new GameObject("Ceiling Light");
            go.transform.SetParent(parent.transform, false);
            go.transform.position = s;
            Light l = go.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = lampColor;
            l.intensity = lampIntensity;
            l.range = lampRange;
            l.shadows = LightShadows.None;
            made++;
        }

        // The power-cut system remembers each light's brightness; let it see the new setup
        if (BlackoutDirector.Instance != null) BlackoutDirector.Instance.RefreshLights();

        Debug.Log($"[MapLighting] {made} ceiling lights from {source} (of {spots.Count} spots), sun dimmed to {sunIntensity}, fog {fogDensity}.");
    }

    static void AddSpot(List<Vector3> spots, Vector3 p, float spacing)
    {
        foreach (Vector3 s in spots)
        {
            Vector3 d = s - p; d.y *= 3f; // lights on different floors don't merge
            if (d.sqrMagnitude < spacing * spacing) return;
        }
        spots.Add(p);
    }
}
