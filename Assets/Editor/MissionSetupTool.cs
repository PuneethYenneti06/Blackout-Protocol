using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;

// Menu "Blackout Protocol/Setup Mission In Scene":
//   Builds the mission prefabs in Assets/Prefabs/Mission/ (root = scripts + collider, model = child "Visual")
//   and places everything in the open scene under one "Mission" object, near the player, so you only
//   have to move things into place. Running it again reuses what exists: nothing is duplicated.
//   Prefabs built while a model was missing get a placeholder and are rebuilt once the model exists.
// Menu "Blackout Protocol/Assign Animation Clips":
//   Finds the Mixamo clips in Assets/Animations by file name and fills every NpcAnimator
//   (scene guards and hostages, and the hostage prefabs).
public static class MissionSetupTool
{
    const string PrefabFolder = "Assets/Prefabs/Mission";
    const string PlaceholderMarker = "_placeholder";

    const string BombModel = "Assets/Bomb/TimeBomb.fbx";
    const string AmmoModel = "Assets/AmmoBox/AmmoBox.FBX";
    const string MedkitModel = "Assets/GeeKay3D/First-Aid-Set/Assets/Prefabs/FirstAidKit_Red.prefab";
    static readonly string[] HostageModels =
    {
        "Assets/npc_casual_set_00/Prefabs/npc_csl_00_character_01f_01.prefab",
        "Assets/npc_casual_set_00/Prefabs/npc_csl_00_character_01m_03.prefab",
        "Assets/npc_casual_set_00/Prefabs/npc_csl_00_character_02f_01.prefab",
        "Assets/npc_casual_set_00/Prefabs/npc_csl_00_character_02m_01.prefab",
    };

    // ------------------------------------------------------------------
    // Setup
    // ------------------------------------------------------------------

    [MenuItem("Blackout Protocol/Setup Mission In Scene")]
    public static void SetupMission()
    {
        EnsureFolder(PrefabFolder);

        GameObject bombPrefab = BuildPrefab("Bomb", BombModel, 0.5f, BombRoot);
        GameObject ammoPrefab = BuildPrefab("AmmoBox", AmmoModel, 0.5f, root => PickupRoot(root, PickupType.Ammo, 30, "rounds"));
        GameObject magPrefab = BuildPrefab("MagazinePickup", AmmoModel, 0.25f, root => PickupRoot(root, PickupType.Ammo, 30, "rounds"));
        GameObject medkitPrefab = BuildPrefab("Medkit", MedkitModel, 0.35f, root => PickupRoot(root, PickupType.Medkit, 1, "medkit"));
        GameObject switchPrefab = BuildPrefab("PowerSwitch", null, 0f, SwitchRoot);
        GameObject[] hostagePrefabs = new GameObject[HostageModels.Length];
        for (int i = 0; i < HostageModels.Length; i++)
            hostagePrefabs[i] = BuildPrefab("Hostage_" + (i + 1), HostageModels[i], -1f, HostageRoot);

        var scene = EditorSceneManager.GetActiveScene();
        Vector3 basePos = BasePosition();

        GameObject mission = FindOrCreateRoot("Mission");

        // Mission manager (reuse one anywhere in the scene)
        MissionManager mm = Object.FindFirstObjectByType<MissionManager>(FindObjectsInactive.Include);
        if (mm == null)
        {
            GameObject go = Child(mission.transform, "MissionManager", basePos);
            mm = Undo.AddComponent<MissionManager>(go);
        }

        // Bomb + spawn markers
        Transform markers = Child(mission.transform, "BombSpawnPoints", basePos).transform;
        for (int i = 0; i < 4; i++)
        {
            GameObject m = Child(markers, "BombSpawn_" + (i + 1), basePos + new Vector3(6f + i * 1.5f, 0f, 4f));
            if (m.GetComponent<BombSpawnPoint>() == null) Undo.AddComponent<BombSpawnPoint>(m);
        }
        if (Object.FindFirstObjectByType<Bomb>(FindObjectsInactive.Include) == null)
            PlacePrefab(bombPrefab, mission.transform, "Bomb", basePos + new Vector3(6f, 0f, 4f));

        // Hostages, 2 x 2 in "the hostage room"
        Transform hostages = Child(mission.transform, "Hostages", basePos).transform;
        for (int i = 0; i < hostagePrefabs.Length; i++)
        {
            if (hostages.Find("Hostage_" + (i + 1)) != null) continue;
            Vector3 p = basePos + new Vector3(-6f + (i % 2) * 1.2f, 0f, 4f + (i / 2) * 1.2f);
            PlacePrefab(hostagePrefabs[i], hostages, "Hostage_" + (i + 1), p);
        }

        // Extraction zone at the entrance
        GameObject zone = Child(mission.transform, "ExtractionZone", basePos + new Vector3(0f, 0f, -4f));
        if (zone.GetComponent<ExtractionZone>() == null)
        {
            BoxCollider box = zone.GetComponent<BoxCollider>();
            if (box == null) box = Undo.AddComponent<BoxCollider>(zone);
            box.isTrigger = true;
            box.size = new Vector3(4f, 3f, 4f);
            box.center = new Vector3(0f, 1.5f, 0f);
            Undo.AddComponent<ExtractionZone>(zone);
        }

        // Patrol routes (3 waypoints each)
        Transform routes = Child(mission.transform, "PatrolRoutes", basePos).transform;
        PatrolRoute[] routeList = new PatrolRoute[2];
        for (int r = 0; r < 2; r++)
        {
            GameObject route = Child(routes, "PatrolRoute_" + (char)('A' + r), basePos + new Vector3(0f, 0f, 8f + r * 4f));
            routeList[r] = route.GetComponent<PatrolRoute>();
            if (routeList[r] == null) routeList[r] = Undo.AddComponent<PatrolRoute>(route);
            if (route.transform.childCount == 0)
                for (int w = 0; w < 3; w++)
                    Child(route.transform, "WP_" + (w + 1), route.transform.position + new Vector3(w * 3f, 0f, 0f));
        }

        // Pickups
        Transform ammo = Child(mission.transform, "AmmoBoxes", basePos).transform;
        for (int i = 0; i < 3; i++)
            if (ammo.Find("AmmoBox_" + (i + 1)) == null)
                PlacePrefab(ammoPrefab, ammo, "AmmoBox_" + (i + 1), basePos + new Vector3(-3f + i * 1.5f, 0f, -1.5f));

        Transform medkits = Child(mission.transform, "Medkits", basePos).transform;
        for (int i = 0; i < 3; i++)
            if (medkits.Find("Medkit_" + (i + 1)) == null)
                PlacePrefab(medkitPrefab, medkits, "Medkit_" + (i + 1), basePos + new Vector3(-3f + i * 1.5f, 0f, -3f));

        // UPS power switch
        if (Object.FindFirstObjectByType<PowerSwitch>(FindObjectsInactive.Include) == null)
            PlacePrefab(switchPrefab, mission.transform, "UPS_PowerSwitch", basePos + new Vector3(3f, 0f, 0f));

        // Guards: magazine drop, NpcAnimator, patrol routes for Patrol guards that have none
        int routeIndex = 0;
        foreach (TerroristGuard g in Object.FindObjectsByType<TerroristGuard>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            Undo.RecordObject(g, "Setup guard");
            if (g.magazineDropPrefab == null) g.magazineDropPrefab = magPrefab;
            if (g.role == GuardRole.Patrol && g.patrolRoute == null)
                g.patrolRoute = routeList[routeIndex++ % routeList.Length];
            if (g.GetComponent<NpcAnimator>() == null) Undo.AddComponent<NpcAnimator>(g.gameObject);
            PrefabUtility.RecordPrefabInstancePropertyModifications(g);
        }

        AssignAnimationClips();

        EditorSceneManager.MarkSceneDirty(scene);
        Selection.activeGameObject = mission;
        Debug.Log("[MissionSetup] Done. Move the objects under 'Mission' into place, bake the NavMesh, set guard roles, then save the scene.");
    }

    // ------------------------------------------------------------------
    // Clip assignment
    // ------------------------------------------------------------------

    [MenuItem("Blackout Protocol/Assign Animation Clips")]
    public static void AssignAnimationClips()
    {
        AnimationClip death = FindClip("character@Death");
        AnimationClip praying = FindClip("Hostage_Scared@Praying");
        AnimationClip injured = FindClip("Hostage@Injured Run");
        AnimationClip goofy = FindClip("Terrified_running@Goofy Running");
        AnimationClip walk = FindClip("player@Walk Forward");
        AnimationClip run = FindClip("guard@Run Forward");

        int count = 0;
        foreach (NpcAnimator a in Object.FindObjectsByType<NpcAnimator>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            Undo.RecordObject(a, "Assign clips");
            Fill(a, death, praying, injured, goofy, walk, run);
            PrefabUtility.RecordPrefabInstancePropertyModifications(a);
            count++;
        }

        // Scene guards without an NpcAnimator yet get one, so their clips are set too
        foreach (TerroristGuard g in Object.FindObjectsByType<TerroristGuard>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (g.GetComponent<NpcAnimator>() != null) continue;
            NpcAnimator a = Undo.AddComponent<NpcAnimator>(g.gameObject);
            Fill(a, death, praying, injured, goofy, walk, run);
            count++;
        }

        // Hostage prefabs
        foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { PrefabFolder }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            GameObject contents = PrefabUtility.LoadPrefabContents(path);
            NpcAnimator a = contents.GetComponent<NpcAnimator>();
            if (a != null)
            {
                Fill(a, death, praying, injured, goofy, walk, run);
                PrefabUtility.SaveAsPrefabAsset(contents, path);
                count++;
            }
            PrefabUtility.UnloadPrefabContents(contents);
        }

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Debug.Log($"[MissionSetup] Clips assigned to {count} NpcAnimator(s). Found: death={death != null}, praying={praying != null}, " +
                  $"injuredRun={injured != null}, goofyRun={goofy != null}, walk={walk != null}, run={run != null}");
    }

    static void Fill(NpcAnimator a, AnimationClip death, AnimationClip praying, AnimationClip injured,
                     AnimationClip goofy, AnimationClip walk, AnimationClip run)
    {
        a.deathClip = death;
        if (a.GetComponent<Hostage>() != null)
        {
            a.captiveClip = praying;
            a.idleClip = null;            // freed hostages use praying as idle
            a.walkClip = injured;
            a.runClip = goofy;
            a.walkClipSpeed = 1.5f;
            a.runClipSpeed = 4.5f;
        }
        else
        {
            a.captiveClip = null;
            a.idleClip = null;            // no guard idle clip: standing guards keep their rest pose
            a.walkClip = walk;
            a.runClip = run;
            a.walkClipSpeed = 1.4f;
            a.runClipSpeed = 4f;
        }
        EditorUtility.SetDirty(a);
    }

    // Finds the clip inside Assets/Animations/**/<fileName>.fbx
    static AnimationClip FindClip(string fileName)
    {
        foreach (string guid in AssetDatabase.FindAssets("t:Model", new[] { "Assets/Animations" }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (!string.Equals(Path.GetFileNameWithoutExtension(path), fileName, System.StringComparison.OrdinalIgnoreCase))
                continue;
            foreach (Object o in AssetDatabase.LoadAllAssetsAtPath(path))
                if (o is AnimationClip clip && !clip.name.StartsWith("__preview__"))
                    return clip;
        }
        Debug.LogWarning($"[MissionSetup] Clip '{fileName}' not found in Assets/Animations.");
        return null;
    }

    // ------------------------------------------------------------------
    // Prefab building
    // ------------------------------------------------------------------

    // Builds (or reuses) Assets/Prefabs/Mission/<name>.prefab. targetSize > 0 rescales the model so its
    // largest side is about that many metres when it's far off; -1 = humanoid (only fix absurd heights).
    static GameObject BuildPrefab(string name, string modelPath, float targetSize, System.Action<GameObject> setupRoot)
    {
        string path = $"{PrefabFolder}/{name}.prefab";
        GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        Object model = string.IsNullOrEmpty(modelPath) ? null : AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);

        if (existing != null)
        {
            Transform v = existing.transform.Find("Visual");
            bool placeholder = v == null || v.Find(PlaceholderMarker) != null;
            if (!placeholder || model == null) return existing;   // keep it (rebuild only to swap a placeholder for the real model)
        }

        GameObject root = new GameObject(name);
        GameObject visual;
        if (model != null)
        {
            visual = (GameObject)PrefabUtility.InstantiatePrefab(model);
            visual.name = "Visual";
            visual.transform.SetParent(root.transform, false);
            NormalizeSize(visual, targetSize);
        }
        else
        {
            visual = Placeholder(name);
            visual.transform.SetParent(root.transform, false);
            if (!string.IsNullOrEmpty(modelPath))
                Debug.LogWarning($"[MissionSetup] Model {modelPath} not found: {name} uses a placeholder (run the tool again after restoring assets).");
        }

        setupRoot(root);
        GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, path);
        Object.DestroyImmediate(root);
        return saved;
    }

    static GameObject Placeholder(string name)
    {
        GameObject visual = new GameObject("Visual");
        GameObject shape;
        if (name.StartsWith("Hostage"))
        {
            shape = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            shape.transform.localPosition = new Vector3(0f, 0.9f, 0f);
            shape.transform.localScale = new Vector3(0.5f, 0.9f, 0.5f);
        }
        else if (name == "PowerSwitch")
        {
            shape = GameObject.CreatePrimitive(PrimitiveType.Cube);
            shape.transform.localPosition = new Vector3(0f, 1.2f, 0f);
            shape.transform.localScale = new Vector3(0.4f, 0.6f, 0.15f);
        }
        else
        {
            shape = GameObject.CreatePrimitive(PrimitiveType.Cube);
            shape.transform.localPosition = new Vector3(0f, 0.15f, 0f);
            shape.transform.localScale = new Vector3(0.4f, 0.3f, 0.3f);
        }
        Object.DestroyImmediate(shape.GetComponent<Collider>());
        shape.name = "Shape";
        shape.transform.SetParent(visual.transform, false);

        // The power switch has no model on purpose; its box is final, not a placeholder
        if (name != "PowerSwitch")
            new GameObject(PlaceholderMarker).transform.SetParent(visual.transform, false);
        return visual;
    }

    static void NormalizeSize(GameObject visual, float targetSize)
    {
        Bounds b;
        if (!RendererBounds(visual, out b)) return;
        if (targetSize < 0f)
        {
            float h = b.size.y;
            if (h > 0.01f && (h < 0.5f || h > 4f)) visual.transform.localScale *= 1.75f / h;
            return;
        }
        float largest = Mathf.Max(b.size.x, b.size.y, b.size.z);
        if (largest > 0.0001f && (largest > targetSize * 3f || largest < targetSize / 3f))
            visual.transform.localScale *= targetSize / largest;
    }

    static bool RendererBounds(GameObject go, out Bounds bounds)
    {
        bounds = new Bounds();
        bool any = false;
        foreach (Renderer r in go.GetComponentsInChildren<Renderer>())
        {
            if (!any) { bounds = r.bounds; any = true; }
            else bounds.Encapsulate(r.bounds);
        }
        return any;
    }

    // Box collider around the Visual (root sits at the origin while the prefab is built)
    static BoxCollider FitBox(GameObject root, bool trigger)
    {
        BoxCollider box = root.AddComponent<BoxCollider>();
        box.isTrigger = trigger;
        if (RendererBounds(root, out Bounds b))
        {
            box.center = b.center;
            box.size = Vector3.Max(b.size, new Vector3(0.2f, 0.2f, 0.2f));
        }
        else
        {
            box.center = new Vector3(0f, 0.15f, 0f);
            box.size = new Vector3(0.4f, 0.3f, 0.3f);
        }
        return box;
    }

    static void BombRoot(GameObject root)
    {
        FitBox(root, false);
        root.AddComponent<Bomb>();
    }

    static void PickupRoot(GameObject root, PickupType type, int amount, string displayName)
    {
        BoxCollider box = FitBox(root, true);
        box.size = Vector3.Max(box.size, new Vector3(1.2f, 1.2f, 1.2f));   // easy to walk over
        Pickup p = root.AddComponent<Pickup>();
        p.type = type;
        p.amount = amount;
        p.displayName = displayName;
    }

    static void SwitchRoot(GameObject root)
    {
        FitBox(root, false);
        PowerSwitch s = root.AddComponent<PowerSwitch>();
        s.lever = root.transform.Find("Visual/Shape");
        s.leverOffRotation = Vector3.zero;   // the box doesn't rotate; assign a real lever if the UPS model has one
    }

    static void HostageRoot(GameObject root)
    {
        CapsuleCollider c = root.AddComponent<CapsuleCollider>();
        c.center = new Vector3(0f, 0.9f, 0f);
        c.height = 1.8f;
        c.radius = 0.3f;

        NavMeshAgent agent = root.AddComponent<NavMeshAgent>();
        agent.enabled = false;            // Hostage switches it on when freed
        agent.radius = 0.3f;
        agent.height = 1.8f;

        NpcAnimator anim = root.AddComponent<NpcAnimator>();
        anim.animator = root.GetComponentInChildren<Animator>();
        anim.agent = agent;
        root.AddComponent<Hostage>();
    }

    // ------------------------------------------------------------------
    // Scene helpers
    // ------------------------------------------------------------------

    static Vector3 BasePosition()
    {
        GameObject player = GameObject.FindWithTag("Player");
        if (player != null) return player.transform.position;
        if (SceneView.lastActiveSceneView != null) return SceneView.lastActiveSceneView.pivot;
        return Vector3.zero;
    }

    static GameObject FindOrCreateRoot(string name)
    {
        foreach (GameObject go in EditorSceneManager.GetActiveScene().GetRootGameObjects())
            if (go.name == name) return go;
        GameObject created = new GameObject(name);
        Undo.RegisterCreatedObjectUndo(created, "Create " + name);
        return created;
    }

    // Finds a child by name, or creates an empty one at the given position
    static GameObject Child(Transform parent, string name, Vector3 position)
    {
        Transform t = parent.Find(name);
        if (t != null) return t.gameObject;
        GameObject go = new GameObject(name);
        Undo.RegisterCreatedObjectUndo(go, "Create " + name);
        go.transform.SetParent(parent, false);
        go.transform.position = position;
        return go;
    }

    static GameObject PlacePrefab(GameObject prefab, Transform parent, string name, Vector3 position)
    {
        Transform t = parent.Find(name);
        if (t != null) return t.gameObject;
        if (prefab == null) return null;
        GameObject go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
        go.name = name;
        go.transform.position = position;
        Undo.RegisterCreatedObjectUndo(go, "Place " + name);
        return go;
    }

    static void EnsureFolder(string folder)
    {
        if (AssetDatabase.IsValidFolder(folder)) return;
        string parent = Path.GetDirectoryName(folder).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
    }
}
