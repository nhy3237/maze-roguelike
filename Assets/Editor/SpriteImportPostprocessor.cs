using UnityEditor;
using UnityEngine;

// Assets/Art/Sprites 아래 PNG를 README_import.md 기준으로 자동 설정한다.
public class SpriteImportPostprocessor : AssetPostprocessor
{
    const string Root = "Assets/Art/Sprites/";

    void OnPreprocessTexture()
    {
        if (!assetPath.StartsWith(Root)) return;
        var imp = (TextureImporter)assetImporter;

        imp.textureType = TextureImporterType.Sprite;
        imp.spriteImportMode = SpriteImportMode.Single;
        imp.spritePixelsPerUnit = 36;
        imp.filterMode = FilterMode.Point;
        imp.textureCompression = TextureImporterCompression.Uncompressed;
        imp.mipmapEnabled = false;

        var s = new TextureImporterSettings();
        imp.ReadTextureSettings(s);
        s.spriteMeshType = SpriteMeshType.FullRect;
        s.spriteAlignment = (int)SpriteAlignment.Custom;
        imp.SetTextureSettings(s);
        imp.spritePivot = PivotFor(assetPath);
    }

    static Vector2 PivotFor(string path)
    {
        string name = System.IO.Path.GetFileNameWithoutExtension(path);
        bool body = path.Contains("/Characters/") && (name.StartsWith("warrior_") || name.StartsWith("guardian_"));
        return body ? new Vector2(0.5f, 1f / 3f) : new Vector2(0.5f, 0.5f);
    }

    [MenuItem("Tools/Reimport Sprites")]
    static void ReimportSprites()
    {
        AssetDatabase.ImportAsset("Assets/Art/Sprites", ImportAssetOptions.ImportRecursive | ImportAssetOptions.ForceUpdate);
    }
}
