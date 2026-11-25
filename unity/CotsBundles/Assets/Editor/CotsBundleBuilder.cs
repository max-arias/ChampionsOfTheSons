// Builds the mod's HDRP asset bundle (COS-5). Run from the command line through tools/build-bundles.ps1:
//   Unity.exe -batchmode -quit -projectPath unity/CotsBundles -executeMethod CotsBundleBuilder.BuildFromCommandLine
// Every bundled prefab is generated from code into Assets/Cots/Generated (git-ignored), so the bundle rebuilds the
// same way every time: drop models from the third-party sources under Assets/ThirdParty (COS-63, CotsLootPrefabs) and
// effects from our own textures and the Cots/Fx shader (COS-102, CotsFxPrefabs). Only the generated prefabs are
// tagged; their meshes, materials, textures and shaders come along as dependencies. No custom MonoBehaviours in
// bundled prefabs: the game has no such scripts; motion and tinting are done by the mod.
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

public static class CotsBundleBuilder
{
    private const string BundleName = "cots";

    [MenuItem("Cots/Generate prefabs")]
    public static void GeneratePrefabs()
    {
        EnsurePipeline();
        CotsGen.Reset();
        CotsLootPrefabs.Generate();
        CotsFxPrefabs.Generate();
        CotsPackPrefabs.Generate();   // after CotsFxPrefabs: its rows replace the prefabs of the same name
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
    }

    [MenuItem("Cots/Build bundle")]
    public static void Build()
    {
        GeneratePrefabs();
        foreach (var name in AssetDatabase.GetAllAssetBundleNames()) AssetDatabase.RemoveAssetBundleName(name, true);
        var prefabs = AssetDatabase.FindAssets("t:Prefab", new[] { CotsGen.Root }).Select(AssetDatabase.GUIDToAssetPath).ToList();
        foreach (var path in prefabs) AssetImporter.GetAtPath(path).SetAssetBundleNameAndVariant(BundleName, "");
        var dupes = prefabs.GroupBy(Path.GetFileNameWithoutExtension).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        if (dupes.Count > 0) throw new System.Exception("prefab names must be unique (the mod loads by name): " + string.Join(", ", dupes));

        var output = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "..", "content", "bundles"));
        Directory.CreateDirectory(output);
        var manifest = BuildPipeline.BuildAssetBundles(output, BuildAssetBundleOptions.ChunkBasedCompression, BuildTarget.StandaloneWindows64);
        if (manifest == null) throw new System.Exception("BuildAssetBundles failed");
        // Keep only the bundle itself; Unity also writes a folder-named manifest bundle and .manifest text files.
        foreach (var f in Directory.GetFiles(output))
            if (Path.GetFileName(f) != BundleName) File.Delete(f);
        Debug.Log($"Cots bundle written to {output}: {prefabs.Count} prefabs: " + string.Join(", ", prefabs.Select(Path.GetFileNameWithoutExtension).OrderBy(n => n)));
    }

    public static void BuildFromCommandLine()
    {
        try
        {
            Build();
            EditorApplication.Exit(0);
        }
        catch (System.Exception e)
        {
            Debug.LogException(e);
            EditorApplication.Exit(1);
        }
    }

    /// <summary>
    /// HDRP must be the active pipeline while building, or HDRP/Lit variants are stripped or compiled for no pipeline
    /// (pink in game). Both lit shader modes are kept, since the game's camera settings decide forward or deferred.
    /// The game renders with Direct3D 11; D3D12 is kept in case the game is started with -dx12.
    /// </summary>
    private static void EnsurePipeline()
    {
        const string path = "Assets/Settings/CotsHDRP.asset";
        var asset = AssetDatabase.LoadAssetAtPath<HDRenderPipelineAsset>(path);
        if (asset == null)
        {
            Directory.CreateDirectory("Assets/Settings");
            asset = ScriptableObject.CreateInstance<HDRenderPipelineAsset>();
            AssetDatabase.CreateAsset(asset, path);
        }
        var so = new SerializedObject(asset);
        var mode = so.FindProperty("m_RenderPipelineSettings.supportedLitShaderMode");
        if (mode != null) mode.intValue = (int)RenderPipelineSettings.SupportedLitShaderMode.Both;
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(asset);
        GraphicsSettings.defaultRenderPipeline = asset;
        for (int i = 0; i < QualitySettings.names.Length; i++)
        {
            QualitySettings.SetQualityLevel(i, false);
            QualitySettings.renderPipeline = asset;
        }
        // HDRP's global settings (default resources, diffusion profiles) are created on first use; make sure they exist.
        typeof(HDRenderPipelineAsset).Assembly.GetType("UnityEngine.Rendering.HighDefinition.HDRenderPipelineGlobalSettings")?.GetMethod("Ensure", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)
            ?.Invoke(null, new object[] { true });
        PlayerSettings.colorSpace = ColorSpace.Linear;
        PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.StandaloneWindows64, false);
        PlayerSettings.SetGraphicsAPIs(BuildTarget.StandaloneWindows64, new[] { GraphicsDeviceType.Direct3D11, GraphicsDeviceType.Direct3D12 });
        AssetDatabase.SaveAssets();
    }
}
