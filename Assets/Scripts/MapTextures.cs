using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

// The map's materials point to texture files that were never added to the project, so every wall,
// floor and ceiling shows up as the same flat grey. When the game starts, this swaps those materials
// (matched by name) for textured ones: plaster walls, tiled floor, office ceiling, wood doors, metal, etc.
// Textures are CC0 from ambientCG, in Assets/Resources/MapTextures. They are laid on by world position
// (Blackout/MapTriplanar shader), because the map's own UVs can't be trusted.
public static class MapTextures
{
    public static bool enabled = true;

    class Preset
    {
        public string texture; public Color tint; public float tileSize; public float smoothness; public float metallic;
        public float normalStrength = 1f;
        public bool structure;   // sides = texture, up = floor tiles, down = ceiling panels
        public Material material;
        public int used;
    }

    static readonly Dictionary<string, Preset> presets = new Dictionary<string, Preset>
    {
        { "wall",     new Preset { texture = "PaintedPlaster017", tint = new Color(0.93f, 0.91f, 0.87f), tileSize = 2.5f, smoothness = 0.12f } },
        { "floor",    new Preset { texture = "Tiles074",          tint = Color.white,                     tileSize = 1.6f, smoothness = 0.45f } },
        { "ceiling",  new Preset { texture = "OfficeCeiling001",  tint = Color.white,                     tileSize = 1.2f, smoothness = 0.10f } },
        { "door",     new Preset { texture = "Wood095",           tint = new Color(0.85f, 0.75f, 0.62f),  tileSize = 1.5f, smoothness = 0.35f } },
        { "darkwood", new Preset { texture = "Wood051",           tint = Color.white,                     tileSize = 1.5f, smoothness = 0.40f } },
        { "concrete", new Preset { texture = "Concrete048",       tint = Color.white,                     tileSize = 2.0f, smoothness = 0.15f } },
        { "blackmetal", new Preset { texture = "Metal063",        tint = new Color(0.55f, 0.55f, 0.55f),  tileSize = 1.0f, smoothness = 0.45f, metallic = 0.6f } },
        { "chrome",   new Preset { texture = "Metal049A",         tint = Color.white,                     tileSize = 1.0f, smoothness = 0.75f, metallic = 1f } },
        { "plastic",  new Preset { texture = "Plastic010",        tint = Color.white,                     tileSize = 0.5f, smoothness = 0.50f } },
        { "blackplastic", new Preset { texture = "Plastic006",    tint = Color.white,                     tileSize = 0.5f, smoothness = 0.55f } },
        { "porcelain", new Preset { texture = "Porcelain001",     tint = Color.white,                     tileSize = 0.6f, smoothness = 0.80f } },
        { "structure", new Preset { texture = "PaintedPlaster017", tint = new Color(0.93f, 0.91f, 0.87f), tileSize = 2.5f, smoothness = 0.15f, structure = true } },
        { "chalkboard", new Preset { texture = "Plastic006",      tint = new Color(0.30f, 0.42f, 0.33f),  tileSize = 1.0f, smoothness = 0.10f, normalStrength = 0.3f } },
    };

    // Map material name (lower case, without ".001" endings) -> preset
    static readonly Dictionary<string, string> byName = new Dictionary<string, string>
    {
        { "wall material", "wall" }, { "wall2_cuts", "wall" }, { "wall2_inside", "wall" }, { "wall2_outside", "wall" },
        { "white", "structure" }, { "putih", "structure" },
        { "tile", "floor" },
        { "roof", "ceiling" },
        { "door", "door" }, { "door_inside", "door" }, { "door_outside", "door" }, { "door_material", "door" },
        { "frame_in", "door" }, { "frame_out", "door" },
        { "dark wood", "darkwood" }, { "wood", "darkwood" },
        { "stairs_material", "concrete" },
        { "black metal", "blackmetal" }, { "cage_in", "blackmetal" }, { "cage_out", "blackmetal" }, { "matt_black", "blackmetal" },
        { "metal", "blackmetal" }, { "outer black", "blackmetal" }, { "hitam", "blackmetal" }, { "crnamat", "blackmetal" },
        { "chrome", "chrome" }, { "inox", "chrome" }, { "metal_06", "chrome" }, { "handle_inside", "chrome" },
        { "handle_material", "chrome" }, { "blades", "chrome" },
        { "socket_plastic", "plastic" }, { "socket_plastic_switchs", "plastic" }, { "grey", "plastic" },
        { "mouse_-_mat_grey", "plastic" }, { "mouse_-_shiny_grey", "plastic" }, { "mouse_-_wheel_grey", "plastic" },
        { "tombol", "plastic" }, { "tomboll", "plastic" },
        { "black-plastic", "blackplastic" },
        { "whiteporcelain", "porcelain" }, { "white_ceramic", "porcelain" },
        { "chalk board", "chalkboard" },
    };

    // Never given the fallback texture (see-through, glowing or small detail materials)
    static readonly HashSet<string> keepAsIs = new HashSet<string>
    {
        "glass", "light", "screen", "lowpoly_screen", "lowpoly_body", "blenderkeyboardmat", "body", "redmat03",
        "copper_parts", "biru", "biru_tua",
    };
    static readonly HashSet<string> propKeys = new HashSet<string> { "plastic", "blackplastic", "porcelain", "chrome" };
    public static float fallbackMinSize = 2.5f;  // unmatched materials on objects at least this big get floor/wall/ceiling textures

    static int lastFrame = -1;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Init()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        Apply();
    }

    static void OnSceneLoaded(Scene s, LoadSceneMode m) => Apply();

    public static string BaseName(Material m)
    {
        if (m == null) return "";
        string n = m.name.Replace(" (Instance)", "");
        n = Regex.Replace(n, @"\.\d+$", "");
        return n.Trim().ToLowerInvariant();
    }

    static void Apply()
    {
        if (!enabled || Time.frameCount == lastFrame) return;
        lastFrame = Time.frameCount;

        // The roof is built first, so the ceiling it found is known and the roof gets textured too
        MapRoof.Build();
        MapLighting.Apply();

        Shader shader = Shader.Find("Blackout/MapTriplanar");
        if (shader == null)
        {
            Debug.LogError("[MapTextures] Shader Blackout/MapTriplanar not found; map left as it was.");
            return;
        }

        // The ceiling the roof found counts as ceiling, whatever its material is called
        Dictionary<string, string> names = new Dictionary<string, string>(byName);
        string ceilingName = BaseName(MapRoof.CeilingMaterial);
        if (ceilingName != "" && !names.ContainsKey(ceilingName)) names[ceilingName] = "ceiling";

        foreach (Preset p in presets.Values) p.used = 0;

        // The map: the root object that holds most of the wall materials
        Dictionary<Transform, int> rootVotes = new Dictionary<Transform, int>();
        MeshRenderer[] all = Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None);
        foreach (MeshRenderer r in all)
            foreach (Material m in r.sharedMaterials)
                if (names.TryGetValue(BaseName(m), out string key) && key == "wall")
                {
                    rootVotes.TryGetValue(r.transform.root, out int c);
                    rootVotes[r.transform.root] = c + 1;
                }
        Transform mapRoot = null; int bestVotes = 0;
        foreach (var kv in rootVotes) if (kv.Value > bestVotes) { bestVotes = kv.Value; mapRoot = kv.Key; }
        if (mapRoot == null)
        {
            Debug.LogWarning("[MapTextures] Couldn't find the map (no wall materials).");
            return;
        }

        Dictionary<string, int> unmatched = new Dictionary<string, int>();
        Dictionary<string, string> report = new Dictionary<string, string>();   // material -> what it became
        Dictionary<string, int> reportCount = new Dictionary<string, int>();
        Dictionary<string, float> reportSize = new Dictionary<string, float>();
        int renderers = 0;
        List<MeshRenderer> targets = new List<MeshRenderer>(mapRoot.GetComponentsInChildren<MeshRenderer>(true));
        if (MapRoof.Roof != null) targets.Add(MapRoof.Roof.GetComponent<MeshRenderer>());
        foreach (GameObject w in MapRoof.OuterWalls)
            if (w != null && Get(shader, "wall") is Material wallMat) w.GetComponent<MeshRenderer>().sharedMaterial = wallMat;
        int fallback = 0;

        foreach (MeshRenderer r in targets)
        {
            if (r == null || IsCharacter(r.transform)) continue;
            Material[] mats = r.sharedMaterials;
            bool changed = false;
            for (int i = 0; i < mats.Length; i++)
            {
                string n = BaseName(mats[i]);
                if (n == "" || n.StartsWith("map_")) continue;
                Material before = mats[i];
                reportCount.TryGetValue(n, out int rc); reportCount[n] = rc + 1;
                reportSize.TryGetValue(n, out float rs); reportSize[n] = Mathf.Max(rs, MaxSize(r.bounds));
                if (names.TryGetValue(n, out string key))
                {
                    // Small-prop looks (plastic, porcelain, chrome) on a big object: it is part of the
                    // building, so it gets the floor/wall/ceiling look instead
                    if (propKeys.Contains(key) && MaxSize(r.bounds) >= fallbackMinSize) key = "structure";
                    Preset p = presets[key];
                    Material pm = Get(shader, key);
                    if (pm == null) continue;
                    mats[i] = pm;
                    p.used++;
                    changed = true;
                }
                else if (!keepAsIs.Contains(n) && !HasTexture(mats[i]) && MaxSize(r.bounds) >= fallbackMinSize
                         && Get(shader, "structure") is Material structureMat)
                {
                    // Untextured part of a big mesh (a room shell): floor below, walls on the sides, ceiling above
                    mats[i] = structureMat;
                    presets["structure"].used++;
                    fallback++;
                    changed = true;
                    unmatched.TryGetValue(n + " (fallback)", out int c);
                    unmatched[n + " (fallback)"] = c + 1;
                }
                else
                {
                    unmatched.TryGetValue(n, out int c);
                    unmatched[n] = c + 1;
                }
                string became = mats[i] != before ? mats[i].name : "left as is" + (HasTexture(before) ? " (has texture)" : "");
                if (!report.TryGetValue(n, out string was) || was.StartsWith("left")) report[n] = became;
            }
            if (changed) { r.sharedMaterials = mats; renderers++; }
        }

        StringBuilder sb = new StringBuilder();
        sb.Append($"[MapTextures] Textured {renderers} objects under '{mapRoot.name}' (ceiling material: '{ceilingName}'). Used:");
        foreach (var kv in presets) if (kv.Value.used > 0) sb.Append($" {kv.Key} {kv.Value.used},");
        sb.Append($" Outer walls {MapRoof.OuterWalls.Count}. Left as is / fallback:");
        List<KeyValuePair<string, int>> left = new List<KeyValuePair<string, int>>(unmatched);
        left.Sort((a, b) => b.Value.CompareTo(a.Value));
        for (int i = 0; i < left.Count && i < 25; i++) sb.Append($" '{left[i].Key}' {left[i].Value},");
        Debug.Log(sb.ToString());

#if UNITY_EDITOR
        // Full per-material list, for tuning: <project>/Logs/MapTextures.txt
        try
        {
            StringBuilder file = new StringBuilder();
            file.AppendLine(sb.ToString());
            List<string> keys = new List<string>(reportCount.Keys);
            keys.Sort((a, b) => reportSize[b].CompareTo(reportSize[a]));
            foreach (string k in keys)
                file.AppendLine($"{k}	-> {report[k]}	uses {reportCount[k]}	biggest {reportSize[k]:0.0} m");
            string dir = System.IO.Path.Combine(Application.dataPath, "..", "Logs");
            System.IO.Directory.CreateDirectory(dir);
            System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "MapTextures.txt"), file.ToString());
        }
        catch (System.Exception) { }
#endif
    }

    static Material Get(Shader shader, string key)
    {
        Preset p = presets[key];
        if (p.material == null) p.material = Make(shader, key, p);
        return p.material;
    }

    static bool HasTexture(Material m)
    {
        if (m == null) return false;
        if (m.HasProperty("_BaseMap") && m.GetTexture("_BaseMap") != null) return true;
        if (m.HasProperty("_MainTex") && m.GetTexture("_MainTex") != null) return true;
        return false;
    }

    static float MaxSize(Bounds b) => Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));

    static bool IsCharacter(Transform t)
    {
        return t.GetComponentInParent<NavMeshAgent>() != null || t.GetComponentInParent<PlayerHealth>() != null
            || t.GetComponentInParent<Camera>() != null;
    }

    static Material Make(Shader shader, string key, Preset p)
    {
        Texture2D color = Resources.Load<Texture2D>("MapTextures/" + p.texture + "_Color");
        Texture2D normal = Resources.Load<Texture2D>("MapTextures/" + p.texture + "_Normal");
        if (color == null)
        {
            Debug.LogWarning($"[MapTextures] Missing texture MapTextures/{p.texture}_Color");
            return null;
        }
        Material m = new Material(shader) { name = "Map_" + key };
        m.SetTexture("_BaseMap", color);
        if (normal != null) m.SetTexture("_NormalMap", normal);
        m.SetColor("_BaseColor", p.tint);
        m.SetFloat("_TileSize", p.tileSize);
        m.SetFloat("_Smoothness", p.smoothness);
        m.SetFloat("_Metallic", p.metallic);
        m.SetFloat("_NormalStrength", p.normalStrength);
        if (p.structure)
        {
            m.SetFloat("_Structure", 1f);
            m.SetTexture("_FloorMap", Resources.Load<Texture2D>("MapTextures/Tiles074_Color"));
            m.SetTexture("_FloorNormal", Resources.Load<Texture2D>("MapTextures/Tiles074_Normal"));
            m.SetFloat("_FloorTile", presets["floor"].tileSize);
            m.SetTexture("_CeilMap", Resources.Load<Texture2D>("MapTextures/OfficeCeiling001_Color"));
            m.SetTexture("_CeilNormal", Resources.Load<Texture2D>("MapTextures/OfficeCeiling001_Normal"));
            m.SetFloat("_CeilTile", presets["ceiling"].tileSize);
        }
        return m;
    }
}
