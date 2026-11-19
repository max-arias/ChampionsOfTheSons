using Il2CppInterop.Runtime;
using RedLoader;
using UnityEngine;

namespace ChampionsOfTheSons.Mod.Bundles;

/// <summary>
/// The mod's HDRP asset bundle (COS-5): built from <c>unity/CotsBundles</c> with <c>tools/build-bundles.ps1</c> into
/// <c>content/bundles/cots</c> and embedded in the DLL. Prefabs are looked up by name; every caller keeps a primitive
/// fallback, so a build without the bundle still plays. See docs/asset-bundles.md.
/// </summary>
internal static class CotsBundle
{
    public const string ResourceName = "bundles/cots";

    /// <summary>Prefab names the mod asks for. Keep in sync with the prefabs in unity/CotsBundles/Assets/Cots.</summary>
    public const string LootMarker = "LootMarker", LootSparkle = "LootSparkle";

    private static AssetBundle? _bundle;
    private static bool _tried;
    private static readonly Dictionary<string, GameObject?> Prefabs = new();

    public static bool Loaded => Load() != null;

    /// <summary>Why the bundle is not loaded (for the log and the self-test), or "" when it is.</summary>
    public static string Problem { get; private set; } = "";

    public static AssetBundle? Load()
    {
        if (_tried) return _bundle;
        _tried = true;
        try
        {
            using var stream = typeof(CotsBundle).Assembly.GetManifestResourceStream(ResourceName);
            if (stream == null)
            {
                Problem = "not embedded (build it with tools/build-bundles.ps1)";
                RLog.Msg("Champions of the Sons: no asset bundle embedded; using primitive placeholders");
                return null;
            }
            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            var bytes = ms.ToArray();
            _bundle = AssetBundle.LoadFromMemory(bytes);
            if (_bundle == null) Problem = "AssetBundle.LoadFromMemory returned null (Unity or HDRP version mismatch?)";
            else RLog.Msg($"Champions of the Sons: asset bundle loaded ({bytes.Length / 1024} KB): {string.Join(", ", _bundle.GetAllAssetNames())}");
        }
        catch (Exception e)
        {
            Problem = e.Message;
            RLog.Error($"Champions of the Sons: asset bundle failed to load: {e}");
        }
        return _bundle;
    }

    public static GameObject? Prefab(string name)
    {
        if (Prefabs.TryGetValue(name, out var cached)) return cached;
        GameObject? prefab = null;
        var bundle = Load();
        if (bundle != null)
        {
            var asset = bundle.LoadAsset(name, Il2CppType.Of<GameObject>());
            prefab = asset == null ? null : asset.Cast<GameObject>();
            if (prefab == null) RLog.Warning($"Champions of the Sons: bundle has no prefab '{name}'");
        }
        Prefabs[name] = prefab;
        return prefab;
    }

    /// <summary>An instance of a bundled prefab under <paramref name="parent"/>, or null when the bundle or prefab is missing.</summary>
    public static GameObject? Spawn(string name, Transform parent)
    {
        var prefab = Prefab(name);
        if (prefab == null) return null;
        var go = UnityEngine.Object.Instantiate(prefab, parent, false);
        go.name = name;
        return go;
    }
}
