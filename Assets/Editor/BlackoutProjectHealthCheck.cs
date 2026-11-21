using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

// Menu: Blackout Protocol > Check Project Health
//
// Checks the scripts, the sounds in Resources, the Tags, and every enabled Build Settings scene
// (one after another; it asks to save first). Prints each result to the Console and shows a
// summary. It only opens scenes, it doesn't change anything.
//
// Run it before every recording. Fix every FAIL. Look at each WARN and decide.
public static class BlackoutProjectHealthCheck
{
    static readonly List<(string level, string text)> results = new List<(string, string)>();

    static readonly string[] RequiredScripts =
    {
        "TerroristGuard", "GuardAlert", "MissionManager", "BlackoutDirector", "BombKeypad",
        "Bomb", "PowerSwitch", "Hostage", "BossFight", "JumpScare", "PlayerHealth", "PlayerShooting",
    };

    static readonly string[] RequiredSounds =
    {
        "bomb_beep", "bomb_defused", "bomb_explosion", "door_open", "door_close",
        "footstep_run", "gunshot_guard", "gunshot_player", "jumpscare", "keypad_error",
    };

    [MenuItem("Blackout Protocol/Check Project Health")]
    public static void Run()
    {
        results.Clear();

        Section("Scripts");
        CheckScripts();

        Section("Sounds (Resources/Sounds)");
        CheckSounds();

        Section("Tags");
        if (!InternalEditorUtility.tags.Contains("Player"))
            Fail("The tag 'Player' is not defined (Edit > Project Settings > Tags and Layers). The guards, the jump scare and the health script all look for it.");
        else
            Ok("Tag 'Player' exists.");

        Section("Build Settings");
        var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).ToList();
        if (scenes.Count == 0)
        {
            Fail("No enabled scenes in Build Settings.");
        }

        foreach (var s in scenes)
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(s.path) == null)
                Fail($"Build scene is missing from disk: {s.path}");
            else
                Ok($"Build scene: {s.path}");
        }

        // Open each build scene and check it
        // Ask before the check opens other scenes. Saving only the active scene keeps this
        // compatible with every Unity 6 version (the older multi-scene save call isn't available).
        Scene current = EditorSceneManager.GetActiveScene();
        if (current.isDirty)
        {
            bool save = EditorUtility.DisplayDialog("Blackout Protocol health check",
                "The open scene has unsaved changes. Save it before the check runs?", "Save", "Cancel");
            if (!save)
            {
                Debug.LogWarning("[Health] Cancelled.");
                return;
            }
            EditorSceneManager.SaveScene(current);
        }

        string originalPath = EditorSceneManager.GetActiveScene().path;
        foreach (var s in scenes)
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(s.path) == null) continue;
            Section("Scene: " + s.path);
            EditorSceneManager.OpenScene(s.path, OpenSceneMode.Single);
            CheckScene();
        }

        if (!string.IsNullOrEmpty(originalPath) && AssetDatabase.LoadAssetAtPath<SceneAsset>(originalPath) != null)
        {
            EditorSceneManager.OpenScene(originalPath, OpenSceneMode.Single);
        }

        Report();
    }

    // ------------------------------------------------------------------ checks

    static void CheckScripts()
    {
        foreach (string name in RequiredScripts)
        {
            if (FindType(name) == null)
                Fail($"Script '{name}' is not in the project. It is needed by the mission. Restore it from your repository.");
            else
                Ok($"Script '{name}' found.");
        }
    }

    static void CheckSounds()
    {
        foreach (string name in RequiredSounds)
        {
            if (Resources.Load<AudioClip>("Sounds/" + name) == null)
                Warn($"Resources/Sounds/{name} did not load. That sound will be silent in the build.");
        }
    }

    static void CheckScene()
    {
        // Missing scripts (the pink "Missing Script" components)
        int missingCount = 0;
        foreach (Transform t in UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject) > 0)
            {
                missingCount++;
                if (missingCount <= 5) Fail($"Missing script on '{t.gameObject.name}' (pink component). Re-add or restore the script.");
            }
        }
        if (missingCount > 5) Fail($"...and {missingCount - 5} more objects with missing scripts.");

        // Player
        GameObject player = GameObject.FindWithTag("Player");
        if (player == null) Fail("No object tagged 'Player' in this scene.");
        else Ok($"Player object: '{player.name}'.");

        // Audio listeners
        int listeners = UnityEngine.Object.FindObjectsByType<AudioListener>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length;
        if (listeners == 0) Fail("No AudioListener in the scene. There will be no sound.");
        else if (listeners > 1) Fail($"{listeners} AudioListeners in the scene. Unity keeps one and warns; delete the extras (usually a second camera).");
        else Ok("One AudioListener.");

        // Main camera
        int cams = GameObject.FindGameObjectsWithTag("MainCamera").Length;
        if (cams == 0) Fail("No camera tagged MainCamera. The flashlight and the first-person view attach to it.");
        else if (cams > 1) Warn($"{cams} cameras tagged MainCamera. Only one should be.");

        // Mission pieces
        Bomb bomb = UnityEngine.Object.FindFirstObjectByType<Bomb>(FindObjectsInactive.Include);
        if (bomb == null)
        {
            Warn("No Bomb in this scene.");
        }
        else
        {
            foreach (Renderer r in bomb.GetComponentsInChildren<Renderer>())
            {
                Material m = r.sharedMaterial;
                if (m != null && m.HasProperty("_EmissionColor") && !m.IsKeywordEnabled("_EMISSION"))
                {
                    Warn($"Material '{m.name}' on the bomb has Emission off, so the bomb won't blink. Tick Emission in the material.");
                    break;
                }
            }
        }

        MissionManager mission = UnityEngine.Object.FindFirstObjectByType<MissionManager>(FindObjectsInactive.Include);
        int hostages = UnityEngine.Object.FindObjectsByType<Hostage>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length;
        if (mission != null && hostages != mission.totalHostages)
            Warn($"MissionManager expects {mission.totalHostages} hostages, the scene has {hostages}.");
        else if (hostages == 0)
            Warn("No Hostage in this scene.");
        else
            Ok($"{hostages} hostages.");

        if (UnityEngine.Object.FindFirstObjectByType<PowerSwitch>(FindObjectsInactive.Include) == null)
            Warn("No PowerSwitch in this scene. Players can only cut the power with the B key.");

        Type guardType = FindType("TerroristGuard");
        if (guardType != null)
        {
            int guards = UnityEngine.Object.FindObjectsByType(guardType, FindObjectsInactive.Include, FindObjectsSortMode.None).Length;
            if (guards == 0) Warn("No TerroristGuard in this scene. There are no enemies. Use the Blackout Protocol > Auto tools to add them.");
            else Ok($"{guards} guards.");
        }

        // Lighting
        if (LightmapSettings.lightmaps.Length > 0)
            Warn("Baked lightmaps are in this scene. Baked light stays on the walls when the power is cut. Use Realtime or Mixed lights wherever the blackout has to darken the room.");

        int areaLights = UnityEngine.Object.FindObjectsByType<Light>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .Count(l => l.type == LightType.Rectangle);
        if (areaLights > 0) Ok($"{areaLights} area light(s) (they are dimmed in the blackout because includeAreaLights is on).");

        // NavMesh
        NavMeshTriangulation tri = NavMesh.CalculateTriangulation();
        if (tri.indices.Length == 0)
            Fail("No NavMesh is baked. Guards won't move. Run Blackout Protocol > Auto > Bake NavMesh.");
        else
            Ok($"NavMesh baked ({tri.indices.Length / 3} triangles).");

        // Global volume (the blackout colour grade needs one, but it creates its own if missing)
        bool hasGlobalVolume = UnityEngine.Object.FindObjectsByType<Volume>(FindObjectsInactive.Include, FindObjectsSortMode.None).Any(v => v.isGlobal);
        if (!hasGlobalVolume) Ok("No global Volume in the scene. BlackoutDirector will create one for the colour grade.");
    }

    // ------------------------------------------------------------------ helpers

    static Type FindType(string name)
    {
        foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            try
            {
                Type t = asm.GetType(name);
                if (t != null) return t;
            }
            catch (Exception)
            {
                // Some assemblies can't be queried. Skip them.
            }
        }

        return null;
    }

    static void Section(string title)
    {
        results.Add(("section", title));
    }

    static void Ok(string text) => results.Add(("ok", text));
    static void Warn(string text) => results.Add(("warn", text));
    static void Fail(string text) => results.Add(("fail", text));

    static void Report()
    {
        int fails = results.Count(r => r.level == "fail");
        int warns = results.Count(r => r.level == "warn");

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("=== Blackout Protocol health check ===");
        foreach (var r in results)
        {
            switch (r.level)
            {
                case "section": sb.AppendLine("\n-- " + r.text); break;
                case "ok": sb.AppendLine("  [ OK ]  " + r.text); break;
                case "warn": sb.AppendLine("  [WARN]  " + r.text); break;
                case "fail": sb.AppendLine("  [FAIL]  " + r.text); break;
            }
        }

        string summary = $"{fails} fail(s), {warns} warning(s).";
        sb.AppendLine("\n" + summary);

        if (fails > 0) Debug.LogError(sb.ToString());
        else if (warns > 0) Debug.LogWarning(sb.ToString());
        else Debug.Log(sb.ToString());

        EditorUtility.DisplayDialog("Blackout Protocol health check",
            summary + "\n\nThe full list is in the Console. Fix every FAIL before recording.", "OK");
    }
}
