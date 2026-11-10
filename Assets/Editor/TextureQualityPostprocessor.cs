using UnityEditor;
using UnityEngine;

public sealed class TextureQualityPostprocessor : AssetPostprocessor
{
    private const int DefaultMaxTextureSize = 2048;

    public override int GetPostprocessOrder()
    {
        return 100;
    }

    private void OnPreprocessTexture()
    {
        ApplyQuality((TextureImporter)assetImporter);
    }

    [MenuItem("Tools/Blackout Protocol/Improve Imported Texture Quality")]
    private static void ImproveImportedTextureQuality()
    {
        string[] textureGuids = AssetDatabase.FindAssets("t:Texture", new[] { "Assets" });
        int changedCount = 0;
        foreach (string guid in textureGuids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null)
            {
                continue;
            }

            bool changed = ApplyQuality(importer);
            if (changed)
            {
                changedCount++;
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            }
        }

        Debug.Log($"Texture quality pass completed. Updated {changedCount} textures.");
    }

    private static bool ApplyQuality(TextureImporter importer)
    {
        bool changed = false;
        int targetMaxSize = Mathf.Max(importer.maxTextureSize, DefaultMaxTextureSize);
        if (importer.maxTextureSize != targetMaxSize)
        {
            importer.maxTextureSize = targetMaxSize;
            changed = true;
        }

        if (importer.textureType == TextureImporterType.Sprite)
        {
            return changed;
        }

        if (importer.mipmapEnabled == false)
        {
            importer.mipmapEnabled = true;
            changed = true;
        }

        if (importer.anisoLevel < 4)
        {
            importer.anisoLevel = 4;
            changed = true;
        }

        if (importer.textureCompression != TextureImporterCompression.CompressedHQ)
        {
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            changed = true;
        }

        return changed;
    }
}
