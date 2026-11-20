using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ChampionsOfTheSons.Mod.Book;

/// <summary>
/// Plain uGUI helpers for the guide book page canvas (COS-59). The page is laid out in page pixels
/// (<see cref="W"/> x <see cref="H"/>, origin top-left) and every object sits on <see cref="Layer"/>, which only the
/// page camera renders. Hit-testing is geometry on page pixels, not the EventSystem.
/// </summary>
internal static class PageKit
{
    /// <summary>The visible page: the part of the 1024x1024 page texture the book mesh shows (measured in COS-62).</summary>
    public const float W = 666, H = 1024;
    public const int Layer = 31;

    // Colors (the game's guide book look, a little darker; COS-57 / COS-59).
    public static readonly Color Paper = Hex("#D9CFB8");
    public static readonly Color PaperShade = Hex("#C9BEA4");
    public static readonly Color Ink = Hex("#2A2622");
    public static readonly Color InkFaint = Hex("#2A262299");
    public static readonly Color TitleBar = Hex("#5A4636");
    public static readonly Color TitleText = Hex("#EFE7D6");
    public static readonly Color Select = Hex("#B8322A");
    public static readonly Color Good = Hex("#3E6B2A");
    public static readonly Color Bad = Hex("#8E2424");

    /// <summary>Rarity tape colors 0..7 (COS-57): muted so they sit on paper.</summary>
    private static readonly Color[] Rarity =
    {
        Hex("#9A968C"), Hex("#6E8B4E"), Hex("#4F7FA6"), Hex("#C9982A"), Hex("#7A4A82"), Hex("#D9622B"), Hex("#9E2B2B"), Hex("#6A52C8"),
    };

    public static Color RarityColor(int r) => Rarity[Math.Clamp(r, 0, Rarity.Length - 1)];

    public static Color Hex(string hex) => ColorUtility.TryParseHtmlString(hex, out var c) ? c : Color.magenta;

    /// <summary>"#RRGGBB" (ColorUtility.ToHtmlStringRGB is stripped from the game build).</summary>
    public static string HexOf(Color c) =>
        $"#{(int)Mathf.Round(Mathf.Clamp01(c.r) * 255):X2}{(int)Mathf.Round(Mathf.Clamp01(c.g) * 255):X2}{(int)Mathf.Round(Mathf.Clamp01(c.b) * 255):X2}";

    /// <summary>A child rectangle at page pixels (x, y from the top-left) of <paramref name="parent"/>'s page space.</summary>
    public static RectTransform Rect(Transform parent, string name, float x, float y, float w, float h)
    {
        var go = new GameObject(name) { layer = Layer };
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0, 1);
        rt.pivot = new Vector2(0, 1);
        rt.anchoredPosition = new Vector2(x, -y);
        rt.sizeDelta = new Vector2(w, h);
        return rt;
    }

    /// <summary>A child that fills its parent.</summary>
    public static RectTransform Fill(Transform parent, string name, float inset = 0)
    {
        var go = new GameObject(name) { layer = Layer };
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(inset, inset);
        rt.offsetMax = new Vector2(-inset, -inset);
        return rt;
    }

    public static Image Box(Transform parent, string name, float x, float y, float w, float h, Color color)
    {
        var img = Rect(parent, name, x, y, w, h).gameObject.AddComponent<Image>();
        img.color = color;
        img.raycastTarget = false;
        return img;
    }

    /// <summary>A 1-unit-wide frame drawn as four thin boxes inside the rectangle.</summary>
    public static Image[] Frame(Transform parent, float x, float y, float w, float h, Color color, float t = 2)
    {
        return new[]
        {
            Box(parent, "frame-t", x, y, w, t, color), Box(parent, "frame-b", x, y + h - t, w, t, color),
            Box(parent, "frame-l", x, y, t, h, color), Box(parent, "frame-r", x + w - t, y, t, h, color),
        };
    }

    public static TextMeshProUGUI Text(Transform parent, string name, float x, float y, float w, float h, string text, float size, Color color,
        TextAlignmentOptions align = TextAlignmentOptions.MidlineLeft, bool wrap = false)
    {
        var t = Rect(parent, name, x, y, w, h).gameObject.AddComponent<TextMeshProUGUI>();
        t.text = text;
        t.fontSize = size;
        t.color = color;
        t.alignment = align;
        t.enableWordWrapping = wrap;
        t.overflowMode = wrap ? TextOverflowModes.Truncate : TextOverflowModes.Ellipsis;
        t.richText = true;
        t.raycastTarget = false;
        return t;
    }

    public static void SetLayerRecursive(GameObject go, int layer)
    {
        go.layer = layer;
        foreach (var t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;
    }

    /// <summary>Page pixels (origin top-left) of a 0..1 page point (origin bottom-left).</summary>
    public static Vector2 ToPagePx(Vector2 p01) => new(p01.x * W, (1 - p01.y) * H);

    public static bool Inside(Vector2 px, float x, float y, float w, float h) => px.x >= x && px.x <= x + w && px.y >= y && px.y <= y + h;
}

/// <summary>A clickable rectangle on the page (page pixels).</summary>
internal readonly record struct PageHit(string Key, float X, float Y, float W, float H)
{
    public bool Contains(Vector2 px) => PageKit.Inside(px, X, Y, W, H);
}
