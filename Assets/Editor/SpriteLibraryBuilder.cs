using System.IO;
using System.Linq;
using RingingLoot.View;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

// Assets/Art/Sprites 아래 스프라이트를 모아 Assets/Resources/SpriteLibrary.asset 으로 만든다.
// 에디터를 열 때 목록이 없거나 개수가 다르면 자동으로 다시 만들고, 빌드 직전에도 한 번 만든다. 수동: Tools > Build Sprite Library
public class SpriteLibraryBuilder : IPreprocessBuildWithReport
{
    const string Root = "Assets/Art/Sprites";
    const string Dir = "Assets/Resources";
    const string AssetPath = "Assets/Resources/SpriteLibrary.asset";

    public int callbackOrder => 0;
    public void OnPreprocessBuild(BuildReport report) { Build(); }

    [InitializeOnLoadMethod]
    static void Auto()
    {
        EditorApplication.delayCall += () =>
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            var lib = AssetDatabase.LoadAssetAtPath<SpriteLibrary>(AssetPath);
            int n = AssetDatabase.FindAssets("t:Sprite", new[] { Root }).Length;
            if (lib == null || lib.Count != n) Build();
        };
    }

    [MenuItem("Tools/Build Sprite Library")]
    public static void Build()
    {
        var found = AssetDatabase.FindAssets("t:Sprite", new[] { Root })
            .Select(AssetDatabase.GUIDToAssetPath).OrderBy(p => p, System.StringComparer.Ordinal).ToList();
        var keys = new System.Collections.Generic.List<string>();
        var sprites = new System.Collections.Generic.List<Sprite>();
        foreach (var path in found)
        {
            var s = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (s == null) continue;
            string key = path.Substring(Root.Length + 1);
            key = key.Substring(0, key.Length - Path.GetExtension(key).Length);
            keys.Add(key); sprites.Add(s);
        }
        if (!AssetDatabase.IsValidFolder(Dir)) AssetDatabase.CreateFolder("Assets", "Resources");
        var lib = AssetDatabase.LoadAssetAtPath<SpriteLibrary>(AssetPath);
        if (lib == null) { lib = ScriptableObject.CreateInstance<SpriteLibrary>(); AssetDatabase.CreateAsset(lib, AssetPath); }
        lib.Set(keys.ToArray(), sprites.ToArray());
        EditorUtility.SetDirty(lib);
        AssetDatabase.SaveAssets();
        Debug.Log($"[RingingLoot] SpriteLibrary: 스프라이트 {keys.Count}개");
    }
}
