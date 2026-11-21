using ChampionsOfTheSons.Mod.Hud;
using ChampionsOfTheSons.Mod.Icons;
using SUI;
using UnityEngine;
using UnityEngine.UI;

namespace ChampionsOfTheSons.Mod.ActionBar;

/// <summary>
/// The rune & bone HUD textures (COS-151, <c>content/icons/ui.hud.*.png</c>, made by <c>tools/hud_art.py</c>) as sliced sprites.
/// A missing file leaves the container's flat fallback color, so the bar still works without the art.
/// </summary>
internal static class HudArt
{
    public const string Backing = "ui.hud.backing", SlotFrame = "ui.hud.slot_frame", SlotGlow = "ui.hud.slot_glow", SlotInset = "ui.hud.slot_inset",
        BarChannel = "ui.hud.bar_channel", BarStrap = "ui.hud.bar_strap", BarFill = "ui.hud.bar_fill", LevelDisc = "ui.hud.level_disc",
        KeyPlate = "ui.hud.key_plate", Clasp = "ui.hud.clasp";

    // 9-slice borders in texture pixels (left, bottom, right, top); keep in sync with SLICES in tools/hud_art.py.
    private static readonly Dictionary<string, Vector4> Borders = new()
    {
        [Backing] = new(56, 40, 56, 40),
        [SlotFrame] = new(30, 30, 30, 30),
        [SlotGlow] = new(34, 34, 34, 34),
        [BarChannel] = new(26, 14, 26, 14),
        [BarStrap] = new(22, 14, 22, 14),
        [BarFill] = new(4, 4, 4, 4),
        [KeyPlate] = new(10, 8, 10, 8),
    };

    private static readonly Dictionary<(string, int), Sprite?> Cache = new();

    /// <summary>The sprite with its border drawn at <paramref name="scale"/> canvas units per texture pixel, or null when the file is missing.</summary>
    public static Sprite? Get(string id, float scale)
    {
        var key = (id, Mathf.RoundToInt(scale * 1000));
        if (Cache.TryGetValue(key, out var cached)) return cached;
        Sprite? sprite = null;
        var tex = IconCache.GetTexture(id);
        if (tex != null)
        {
            var border = Borders.TryGetValue(id, out var b) ? b : Vector4.zero;
            // Canvas units per sprite pixel = referencePixelsPerUnit (100) / pixelsPerUnit.
            sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f / Mathf.Max(0.01f, scale), 0,
                SpriteMeshType.FullRect, border);
            sprite.hideFlags = HideFlags.HideAndDontSave;
        }
        Cache[key] = sprite;
        return sprite;
    }

    /// <summary>A container showing <paramref name="id"/>, or <paramref name="fallbackHex"/> as a flat color when the art is missing.</summary>
    public static SContainerOptions Piece(string id, float scale, string fallbackHex, string tintHex = "#FFFFFF")
    {
        var c = SUI.SUI.SContainer.Background(HudKit.C(fallbackHex), EBackground.None);
        Paint(c, id, scale, tintHex);
        return c;
    }

    public static Image? Paint(SContainerOptions c, string id, float scale, string tintHex = "#FFFFFF")
    {
        var img = c.Root.GetComponent<Image>();
        if (img == null) return null;
        img.raycastTarget = false;
        var s = Get(id, scale);
        if (s == null) return img;
        img.sprite = s;
        img.type = Borders.ContainsKey(id) ? Image.Type.Sliced : Image.Type.Simple;
        img.color = HudKit.C(tintHex);
        return img;
    }
}
