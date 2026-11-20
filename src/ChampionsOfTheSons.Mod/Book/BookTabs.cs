using RedLoader;
using Sons.Crafting;
using Sons.Weapon;
using UnityEngine;

namespace ChampionsOfTheSons.Mod.Book;

/// <summary>
/// Our tabs on the guide book: clones of the game's tab mesh placed one tab step below its last tab, with a muted
/// tab color and a flat light glyph like the game's own icons. Clicks on them are found by our own hit test on the
/// book cursor (the clones are not wired into the game's book).
/// </summary>
internal static class BookTabs
{
    private sealed record TabDef(BookTab Tab, string IconId, string ColorHex);

    private static readonly TabDef[] Defs =
    {
        new(BookTab.Gear, "item.steel_breastplate", "#8A4B2C"),
        new(BookTab.Bag, "perk.more_building_resources", "#6B5A3A"),
        new(BookTab.Character, "perk.all_attributes_2", "#46535E"),
    };

    private static readonly Dictionary<BookTab, Renderer> Ours = new();
    private static readonly Dictionary<BookTab, Vector3> Home = new();
    private static Vector3 _slideOut;
    private static BlueprintBookController? _book;

    public static IReadOnlyDictionary<BookTab, Renderer> Tabs => Ours;

    public static void Attach(BlueprintBookController book, Vector3 pageUp, Vector3 pageRight)
    {
        _book = book;
        Ours.Clear();
        Home.Clear();
        try
        {
            var cam = Camera.main;
            var tabs = book.GetComponentsInChildren<Renderer>(false).Where(r => r.gameObject.name == "TactiPadTab").ToList();
            if (cam == null || tabs.Count < 2) { RLog.Warning($"Champions of the Sons book: {tabs.Count} game tabs, no tabs added"); return; }
            // Lowest tab along the page's own up axis, so the book's pose (still raising or not) does not matter.
            tabs = tabs.OrderBy(r => Vector3.Dot(r.bounds.center, pageUp)).ToList();
            var lowest = tabs[0];
            var next = tabs.Skip(1).FirstOrDefault(t => Vector3.Distance(t.bounds.center, lowest.bounds.center) > 0.005f) ?? tabs[1];
            var step = lowest.bounds.center - next.bounds.center;
            // Slide out sideways a little when selected, like the game's active tab (page right = -local x of the page).
            _slideOut = pageRight.normalized * 0.004f;
            for (int i = 0; i < Defs.Length; i++)
            {
                var d = Defs[i];
                var go = UnityEngine.Object.Instantiate(lowest.gameObject, lowest.transform.parent, false);
                go.name = "CotsTab-" + d.Tab;
                go.transform.position = lowest.transform.position + step * (i + 1);
                foreach (var hbi in go.GetComponentsInChildren<HeldBookInteraction>(true)) hbi.enabled = false;
                var r = go.GetComponent<Renderer>();
                var mats = r.materials;
                if (mats.Length > 0) mats[0].SetColor("_BaseColor", PageKit.Hex(d.ColorHex));
                var glyph = Glyph(d.IconId);
                if (mats.Length > 1 && glyph != null)
                {
                    mats[1].SetTexture("_BaseColorMap", glyph);
                    mats[1].SetTexture("_MainTex", glyph);
                }
                r.materials = mats;
                Ours[d.Tab] = r;
                Home[d.Tab] = go.transform.localPosition;
            }
        }
        catch (Exception e)
        {
            RLog.Error($"Champions of the Sons book: adding tabs failed: {e}");
        }
    }

    /// <summary>Slides the selected tab out and the others back.</summary>
    public static void Select(BookTab tab)
    {
        foreach (var (t, r) in Ours)
        {
            if (r == null) continue;
            var home = Home[t];
            r.transform.localPosition = home;
            if (t == tab) r.transform.position += _slideOut;
        }
    }

    /// <summary>Which of our tabs is under a screen point (the book cursor), if any.</summary>
    public static BookTab HitTest(Vector2 screen)
    {
        var cam = Camera.main;
        if (cam == null) return BookTab.None;
        foreach (var (t, r) in Ours)
        {
            if (r == null) continue;
            var b = r.bounds;
            float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
            for (int i = 0; i < 8; i++)
            {
                var c = new Vector3((i & 1) == 0 ? b.min.x : b.max.x, (i & 2) == 0 ? b.min.y : b.max.y, (i & 4) == 0 ? b.min.z : b.max.z);
                var sp = cam.WorldToScreenPoint(c);
                x0 = Mathf.Min(x0, sp.x); y0 = Mathf.Min(y0, sp.y); x1 = Mathf.Max(x1, sp.x); y1 = Mathf.Max(y1, sp.y);
            }
            if (screen.x >= x0 && screen.x <= x1 && screen.y >= y0 && screen.y <= y1) return t;
        }
        return BookTab.None;
    }

    public static Vector2 ScreenCenter(BookTab tab)
    {
        var cam = Camera.main;
        return cam != null && Ours.TryGetValue(tab, out var r) && r != null ? (Vector2)cam.WorldToScreenPoint(r.bounds.center) : default;
    }

    /// <summary>A game tab (not ours): a layout item whose object carries a tab mesh.</summary>
    public static bool IsGameTab(HeldBookInteraction hbi)
    {
        var go = hbi.gameObject;
        if (go.name.StartsWith("CotsTab-", StringComparison.Ordinal)) return false;
        if (go.name == "TactiPadTab") return true;
        foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            if (r.gameObject.name == "TactiPadTab") return true;
        return false;
    }

    private static readonly Dictionary<string, Texture2D?> Glyphs = new();

    /// <summary>
    /// The game's tab icons are a light glyph on a dark square. Ours: the colored game-icons render's glyph (its
    /// saturated, bright pixels; the gray disc behind it is dropped) in off-white on a dark square.
    /// </summary>
    private static Texture2D? Glyph(string iconId)
    {
        if (Glyphs.TryGetValue(iconId, out var cached)) return cached;
        Texture2D? result = null;
        var src = Icons.IconCache.GetTexture(iconId);
        if (src != null && src.isReadable)
        {
            var px = src.GetPixels32();
            var outPx = new Color32[px.Length];
            var bg = new Color32(0x2B, 0x2A, 0x28, 0xFF);
            var fg = new Color32(0xEC, 0xE6, 0xD8, 0xFF);
            for (int i = 0; i < px.Length; i++)
            {
                var c = px[i];
                Color.RGBToHSV(c, out _, out float s, out float v);
                float glyph = c.a > 128 ? Mathf.Clamp01((s - 0.25f) * 5f) * Mathf.Clamp01((v - 0.3f) * 5f) : 0f;
                outPx[i] = Color32.Lerp(bg, fg, glyph);
            }
            result = new Texture2D(src.width, src.height, TextureFormat.RGBA32, false) { name = "cots-tab-" + iconId };
            result.SetPixels32(outPx);
            result.Apply();
            result.hideFlags = HideFlags.HideAndDontSave;
        }
        Glyphs[iconId] = result;
        return result;
    }
}
