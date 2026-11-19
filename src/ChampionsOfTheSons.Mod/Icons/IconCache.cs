using RedLoader;
using UnityEngine;

namespace ChampionsOfTheSons.Mod.Icons;

/// <summary>
/// Lazy cache of the content icons (<c>Mods/ChampionsOfTheSons/icons/&lt;id&gt;.png</c>, e.g. <c>item.broken_flip_flops.png</c>).
/// Every lookup returns null when the file is missing or unreadable, so callers fall back to their own placeholder.
/// Main thread only (Unity objects).
/// </summary>
public static class IconCache
{
    private static readonly Dictionary<string, Texture2D?> Textures = new();
    private static readonly Dictionary<string, Sprite?> Sprites = new();
    private static string? _dir;

    /// <summary>Folder holding the PNGs; resolved once, next to the mod DLL (Mods/) or under the game folder.</summary>
    public static string Directory => _dir ??= ResolveDirectory();

    public static Texture2D? GetTexture(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        if (Textures.TryGetValue(id, out var cached)) return cached;
        Texture2D? tex = null;
        try
        {
            var path = Path.Combine(Directory, id + ".png");
            if (File.Exists(path))
            {
                tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!ImageConversion.LoadImage(tex, File.ReadAllBytes(path))) { UnityEngine.Object.Destroy(tex); tex = null; }
                else
                {
                    tex.filterMode = FilterMode.Bilinear;
                    tex.wrapMode = TextureWrapMode.Clamp;
                    tex.hideFlags = HideFlags.HideAndDontSave;
                }
            }
        }
        catch (Exception e)
        {
            RLog.Warning($"IconCache: {id}: {e.GetType().Name}: {e.Message}");
            tex = null;
        }
        Textures[id] = tex;
        return tex;
    }

    public static Sprite? GetSprite(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        if (Sprites.TryGetValue(id, out var cached)) return cached;
        var tex = GetTexture(id);
        Sprite? sprite = null;
        if (tex != null)
        {
            sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
            sprite.hideFlags = HideFlags.HideAndDontSave;
        }
        Sprites[id] = sprite;
        return sprite;
    }

    private static string ResolveDirectory()
    {
        var candidates = new List<string>();
        try
        {
            var loc = typeof(IconCache).Assembly.Location;
            if (!string.IsNullOrEmpty(loc)) candidates.Add(Path.Combine(Path.GetDirectoryName(loc)!, "ChampionsOfTheSons", "icons"));
        }
        catch { /* dynamic assembly: no location */ }
        candidates.Add(Path.Combine(AppContext.BaseDirectory, "Mods", "ChampionsOfTheSons", "icons"));
        foreach (var c in candidates) if (System.IO.Directory.Exists(c)) return c;
        return candidates[0];
    }
}
