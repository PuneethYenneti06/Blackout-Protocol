using System.Collections.Generic;
using System.IO;
using System.Text;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;

// Automation for the remaining in-Unity setup steps. Each method is also a menu item under
// "Blackout Protocol/Auto", and they can be called from the Unity CLI (script eval).
//  - Bake NavMesh:            adds a NavMeshSurface over the whole level and bakes it
//  - Add Guards:              duplicates the first guard until there are `wantedGuards`, assigning roles
//  - Setup Player Animator:   adds Reload/Hit/Die/Kneel/Heal triggers + states to SoldierController
//  - Convert Player Rig:      tries Humanoid on the soldier model + demo clips, reverts if the avatar fails
public static class MissionAutoSetup
{
    const string SoldierModel = "Assets/LowPolySoldiers_demo/models/Soldier_demo.FBX";
    static readonly string[] SoldierClips =
    {
        "Assets/LowPolySoldiers_demo/animation/demo_combat_idle.FBX",
        "Assets/LowPolySoldiers_demo/animation/demo_combat_run.FBX",
        "Assets/LowPolySoldiers_demo/animation/demo_combat_shoot.FBX",
    };
    const string ControllerPath = "Assets/SoldierController.controller";

    // ------------------------------------------------------------------ NavMesh

    [MenuItem("Blackout Protocol/Auto/Bake NavMesh")]
    public static string BakeNavMesh()
    {
        NavMeshSurface surface = Object.FindFirstObjectByType<NavMeshSurface>(FindObjectsInactive.Include);
        if (surface == null)
        {
            GameObject go = new GameObject("NavMesh");
            Undo.RegisterCreatedObjectUndo(go, "Create NavMesh");
            surface = go.AddComponent<NavMeshSurface>();
        }
        surface.collectObjects = CollectObjects.All;
        surface.useGeometry = NavMeshCollectGeometry.RenderMeshes;
        surface.layerMask = ~0;
        surface.overrideVoxelSize = true;
        surface.voxelSize = 0.1f;       // thin walls and doorways need a fine voxel size
        surface.overrideTileSize = true;
        surface.tileSize = 128;
        surface.BuildNavMesh();

        // BuildNavMesh() from code leaves the data in memory only; store it as an asset next to the
        // scene (like the Inspector's Bake button does) or it is lost when the scene reloads
        if (surface.navMeshData != null && !AssetDatabase.Contains(surface.navMeshData))
        {
            string scenePath = surface.gameObject.scene.path;
            string folder = Path.ChangeExtension(scenePath, null);
            if (!AssetDatabase.IsValidFolder(folder))
                AssetDatabase.CreateFolder(Path.GetDirectoryName(scenePath).Replace('\\', '/'), Path.GetFileName(folder));
            surface.navMeshData.name = "NavMesh-" + surface.gameObject.name;
            AssetDatabase.CreateAsset(surface.navMeshData, folder + "/" + surface.navMeshData.name + ".asset");
            AssetDatabase.SaveAssets();
            EditorUtility.SetDirty(surface);
        }
        EditorSceneManager.MarkSceneDirty(surface.gameObject.scene);

        NavMeshTriangulation tri = NavMesh.CalculateTriangulation();
        string msg = $"NavMesh baked: {tri.vertices.Length} vertices, {tri.indices.Length / 3} triangles";
        Debug.Log("[MissionAuto] " + msg);
        return msg;
    }

    // ------------------------------------------------------------------ Guards

    public struct GuardSpec
    {
        public string name;
        public Vector3 position;
        public float yaw;
        public GuardRole role;
        public string patrolRoute;   // name of a PatrolRoute object, for Patrol guards
    }

    // Duplicates the first TerroristGuard in the scene for each spec that has no guard of that name yet.
    public static string AddGuards(List<GuardSpec> specs)
    {
        TerroristGuard[] existing = Object.FindObjectsByType<TerroristGuard>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        if (existing.Length == 0) return "No TerroristGuard in the scene to duplicate.";
        TerroristGuard template = existing[0];
        foreach (TerroristGuard g in existing) if (g.name == "Terrorist_1") template = g;

        Transform parent = template.transform.parent;
        int added = 0, updated = 0;
        foreach (GuardSpec spec in specs)
        {
            TerroristGuard guard = null;
            foreach (TerroristGuard g in Object.FindObjectsByType<TerroristGuard>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (g.name == spec.name) { guard = g; break; }

            if (guard == null)
            {
                // Duplicate the scene instance: it carries the TerroristGuard and its tuned rifle values
                // (the Insurgent prefab asset itself has no TerroristGuard component)
                GameObject go = Object.Instantiate(template.gameObject);
                go.name = spec.name;
                if (parent != null) go.transform.SetParent(parent, true);
                Undo.RegisterCreatedObjectUndo(go, "Add guard");
                guard = go.GetComponent<TerroristGuard>();
                added++;
            }
            else updated++;

            Undo.RecordObject(guard.transform, "Place guard");
            Undo.RecordObject(guard, "Set role");
            guard.transform.position = spec.position;
            guard.transform.rotation = Quaternion.Euler(0f, spec.yaw, 0f);
            guard.transform.localScale = template.transform.localScale;
            guard.role = spec.role;
            if (!string.IsNullOrEmpty(spec.patrolRoute))
            {
                GameObject route = GameObject.Find(spec.patrolRoute);
                if (route != null) guard.patrolRoute = route.GetComponent<PatrolRoute>();
            }
            PrefabUtility.RecordPrefabInstancePropertyModifications(guard);
        }
        EditorSceneManager.MarkSceneDirty(template.gameObject.scene);
        string msg = $"Guards: {added} added, {updated} updated, total now {Object.FindObjectsByType<TerroristGuard>(FindObjectsSortMode.None).Length}";
        Debug.Log("[MissionAuto] " + msg);
        return msg;
    }

    // ------------------------------------------------------------------ Player animator

    [MenuItem("Blackout Protocol/Auto/Setup Player Animator")]
    public static string SetupPlayerAnimator()
    {
        AnimatorController ac = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (ac == null) return "SoldierController.controller not found";

        StringBuilder log = new StringBuilder();
        string[] triggers = { "Reload", "Hit", "Die", "Kneel", "Heal", "Shoot" };
        foreach (string t in triggers)
        {
            bool has = false;
            foreach (AnimatorControllerParameter p in ac.parameters) if (p.name == t) has = true;
            if (!has) { ac.AddParameter(t, AnimatorControllerParameterType.Trigger); log.Append($"+param {t} "); }
        }

        AnimatorControllerLayer baseLayer = ac.layers[0];
        AnimatorControllerLayer upper = null;
        foreach (AnimatorControllerLayer l in ac.layers) if (l.name == "UpperBody") upper = l;

        AnimatorState idle = FindState(baseLayer.stateMachine, "Idle");
        AnimatorState upperRest = upper != null ? FindState(upper.stateMachine, "Empty") : null;

        // Full-body states on the base layer
        AddState(baseLayer.stateMachine, "Hit", "player@Hit Reaction", idle, true, log);
        AddState(baseLayer.stateMachine, "Die", "character@Death", null, false, log);
        AddState(baseLayer.stateMachine, "Kneel", "player_diffusing_bomb@Kneeling Inspecting", idle, true, log);

        // Arms-only states on the upper-body layer (fall back to the base layer if there is none)
        AnimatorStateMachine armsSm = upper != null ? upper.stateMachine : baseLayer.stateMachine;
        AnimatorState armsRest = upper != null ? upperRest : idle;
        AddState(armsSm, "Reload", "player_idle@Reloading", armsRest, true, log);
        AddState(armsSm, "Heal", "player_using_medkit@Drinking", armsRest, true, log);

        EditorUtility.SetDirty(ac);
        AssetDatabase.SaveAssets();
        string msg = "Player animator: " + (log.Length == 0 ? "already set up" : log.ToString());
        Debug.Log("[MissionAuto] " + msg);
        return msg;
    }

    static AnimatorState FindState(AnimatorStateMachine sm, string name)
    {
        foreach (ChildAnimatorState s in sm.states) if (s.state.name.Trim() == name) return s.state;
        return null;
    }

    static void AddState(AnimatorStateMachine sm, string name, string clipFile, AnimatorState exitTo, bool hasExit, StringBuilder log)
    {
        AnimatorState state = FindState(sm, name);
        if (state == null)
        {
            state = sm.AddState(name);
            log.Append($"+state {name} ");
        }
        AnimationClip clip = FindClipAsset(clipFile);
        if (clip != null && state.motion != clip) { state.motion = clip; log.Append($"(clip {clipFile}) "); }
        else if (clip == null) log.Append($"(clip {clipFile} NOT FOUND) ");

        // Any State -> this state on its trigger
        bool hasAny = false;
        foreach (AnimatorStateTransition t in sm.anyStateTransitions)
            if (t.destinationState == state) hasAny = true;
        if (!hasAny)
        {
            AnimatorStateTransition t = sm.AddAnyStateTransition(state);
            t.AddCondition(AnimatorConditionMode.If, 0f, name);
            t.hasExitTime = false;
            t.duration = 0.1f;
            t.canTransitionToSelf = false;
        }

        // Back to the rest state when the clip ends
        if (hasExit && exitTo != null && state.transitions.Length == 0)
        {
            AnimatorStateTransition back = state.AddTransition(exitTo);
            back.hasExitTime = true;
            back.exitTime = 0.9f;
            back.duration = 0.15f;
        }
    }

    static AnimationClip FindClipAsset(string fileName)
    {
        foreach (string guid in AssetDatabase.FindAssets("t:Model", new[] { "Assets/Animations" }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (!string.Equals(Path.GetFileNameWithoutExtension(path), fileName, System.StringComparison.OrdinalIgnoreCase)) continue;
            foreach (Object o in AssetDatabase.LoadAllAssetsAtPath(path))
                if (o is AnimationClip clip && !clip.name.StartsWith("__preview__")) return clip;
        }
        return null;
    }

    // ------------------------------------------------------------------ Player rig

    [MenuItem("Blackout Protocol/Auto/Convert Player Rig To Humanoid")]
    public static string ConvertPlayerRigToHumanoid()
    {
        List<string> paths = new List<string> { SoldierModel };
        paths.AddRange(SoldierClips);

        ModelImporter modelImporter = AssetImporter.GetAtPath(SoldierModel) as ModelImporter;
        if (modelImporter == null) return "Soldier_demo.FBX importer not found";
        if (modelImporter.animationType == ModelImporterAnimationType.Human) return "Player rig is already Humanoid";

        // Model first: its avatar is the one the clips will reuse
        modelImporter.animationType = ModelImporterAnimationType.Human;
        modelImporter.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
        modelImporter.SaveAndReimport();

        Avatar avatar = null;
        foreach (Object o in AssetDatabase.LoadAllAssetsAtPath(SoldierModel))
            if (o is Avatar a) avatar = a;

        if (avatar == null || !avatar.isValid || !avatar.isHuman)
        {
            modelImporter.animationType = ModelImporterAnimationType.Generic;
            modelImporter.SaveAndReimport();
            string fail = "Humanoid avatar could not be built for Soldier_demo.FBX (bones don't map). Reverted to Generic.";
            Debug.LogWarning("[MissionAuto] " + fail);
            return fail;
        }

        foreach (string clipPath in SoldierClips)
        {
            ModelImporter ci = AssetImporter.GetAtPath(clipPath) as ModelImporter;
            if (ci == null) continue;
            ci.animationType = ModelImporterAnimationType.Human;
            ci.avatarSetup = ModelImporterAvatarSetup.CopyFromOther;
            ci.sourceAvatar = avatar;
            ci.SaveAndReimport();
        }

        string msg = "Player rig converted to Humanoid (model + demo_combat clips share its avatar).";
        Debug.Log("[MissionAuto] " + msg);
        return msg;
    }

    // ------------------------------------------------------------------ Inspection helpers (used from the CLI)

    // Dumps the scene hierarchy (depth-limited) with world positions and renderer bounds, for placing things.
    public static string DumpHierarchy(int maxDepth, string rootName)
    {
        StringBuilder sb = new StringBuilder();
        foreach (GameObject go in EditorSceneManager.GetActiveScene().GetRootGameObjects())
        {
            if (!string.IsNullOrEmpty(rootName) && go.name != rootName) continue;
            Dump(go.transform, 0, maxDepth, sb);
        }
        return sb.ToString();
    }

    static void Dump(Transform t, int depth, int maxDepth, StringBuilder sb)
    {
        if (depth > maxDepth) return;
        Renderer r = t.GetComponent<Renderer>();
        string b = r != null ? $" bounds c={r.bounds.center:F1} s={r.bounds.size:F1}" : "";
        sb.Append(new string(' ', depth * 2)).Append(t.name).Append($" pos={t.position:F1}{b} children={t.childCount}\n");
        for (int i = 0; i < t.childCount; i++) Dump(t.GetChild(i), depth + 1, maxDepth, sb);
    }

    // Bounds of every renderer under an object: a quick way to see how big the level is
    public static string LevelBounds(string rootName)
    {
        GameObject root = GameObject.Find(rootName);
        if (root == null) return "not found";
        Bounds b = new Bounds();
        bool any = false;
        foreach (Renderer r in root.GetComponentsInChildren<Renderer>())
        {
            if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
        }
        return any ? $"center={b.center:F2} size={b.size:F2} min={b.min:F2} max={b.max:F2}" : "no renderers";
    }
}
