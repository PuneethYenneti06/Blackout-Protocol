using System;
using UnityEditor;
using UnityEngine;

// Import settings for the Mixamo clips under Assets/Animations/.
// Runs ONLY the first time a file is imported (no .meta yet), so anything you change
// by hand in the Inspector afterwards is never overwritten.
//  - Rig: Humanoid, avatar created from this model.
//  - Loop: idle / walk / run / praying clips loop; death / hit / reload / kneel / drinking don't.
//  - Root rotation and root height (Y) are baked into the pose, so the clips don't turn
//    or lift the character; NavMeshAgents and scripts decide where the character goes.
public class MixamoImportSettings : AssetPostprocessor
{
    const string Folder = "Assets/Animations/";

    static readonly string[] LoopWords = { "idle", "walk", "run", "pray" };
    static readonly string[] OnceWords = { "death", "die", "hit", "reload", "kneel", "inspect", "drink" };

    bool Applies()
    {
        if (!assetPath.StartsWith(Folder, StringComparison.OrdinalIgnoreCase)) return false;
        if (!assetPath.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase)) return false;
        return assetImporter is ModelImporter importer && importer.importSettingsMissing;
    }

    void OnPreprocessModel()
    {
        if (!Applies()) return;

        ModelImporter importer = (ModelImporter)assetImporter;
        importer.animationType = ModelImporterAnimationType.Human;
        importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
        importer.importAnimation = true;
        Debug.Log($"[MixamoImportSettings] Humanoid rig set for {assetPath}");
    }

    void OnPreprocessAnimation()
    {
        if (!Applies()) return;

        ModelImporter importer = (ModelImporter)assetImporter;
        ModelImporterClipAnimation[] clips = importer.defaultClipAnimations;
        if (clips == null || clips.Length == 0) return;

        bool loop = ShouldLoop(assetPath);
        foreach (ModelImporterClipAnimation clip in clips)
        {
            clip.loopTime = loop;
            clip.loopPose = false;

            // Bake root rotation and Y into the pose
            clip.lockRootRotation = true;
            clip.keepOriginalOrientation = true;
            clip.lockRootHeightY = true;
            clip.keepOriginalPositionY = true;
        }
        importer.clipAnimations = clips;
        Debug.Log($"[MixamoImportSettings] {clips.Length} clip(s) in {assetPath}, loop = {loop}");
    }

    // Decides by the Mixamo clip name after '@' (e.g. "player_idle@Reloading" is a reload, not an idle).
    static bool ShouldLoop(string path)
    {
        string file = System.IO.Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
        int at = file.IndexOf('@');
        string clipName = at >= 0 ? file.Substring(at + 1) : file;

        foreach (string w in OnceWords)
            if (clipName.Contains(w)) return false;
        foreach (string w in LoopWords)
            if (clipName.Contains(w)) return true;
        return false;
    }
}
