using RedLoader;
using Sons.Crafting;
using Sons.Weapon;
using UnityEngine;
using UnityEngine.UI;

namespace ChampionsOfTheSons.Mod.Book;

/// <summary>
/// Our tab on the guide book: a copy of the game's lowest root tab (its object with the slide Animator, the
/// AnimRoot it animates and the tab mesh), placed one tab step below it as a sibling in the book's tab column, with a
/// muted tab color and our own icon face. The copy has no game interaction (its <see cref="HeldBookInteraction"/>,
/// mouse proxy and sub-tabs are removed before it ever activates), so the game neither hovers nor clicks it; we drive
/// its Animator's SlideOut ourselves: out while the cursor is over it or while our page shows, like the game's tabs,
/// plus the game's white hover outline while the cursor is over it (COS-131). Clicks are found by our own hit test on the book cursor.
/// </summary>
internal static class BookTabs
{
    private sealed record TabDef(BookTab Tab, string IconId, string ColorHex);

    /// <summary>
    /// One tab for the mod (only one fits under the game's column); Gear, Bag and Character are sub-tabs in the page's
    /// title bar. Keyed by <see cref="BookTab.Gear"/>; it opens the last mod page shown.
    /// </summary>
    private static readonly TabDef[] Defs =
    {
        new(BookTab.Gear, "ui.book_tab", "#8A4B2C"),
    };

    /// <summary>The game's slide parameter on its tab Animators (BookRootTabSlide / BookTabSlide).</summary>
    private const string SlideOutParam = "SlideOut";

    private sealed class Ours
    {
        public Renderer Face = null!;
        public Transform Root = null!;
        public Animator? Animator;
        public Collider? Hover;
        public GameObject? Outline;
        public bool Out, Lit;
    }

    private static readonly Dictionary<BookTab, Ours> Made = new();
    private static readonly Dictionary<BookTab, Renderer> Faces = new();
    private static bool _pageShown;
    private static Collider? _neighbour;
    private static Animator? _neighbourAnimator;

    public static IReadOnlyDictionary<BookTab, Renderer> Tabs => Faces;

    /// <summary>The tab the cursor was over on the last <see cref="Tick"/> (None if none).</summary>
    public static BookTab Hovered { get; private set; }

    public static void Attach(BlueprintBookController book, Vector3 pageUp, Vector3 pageRight)
    {
        Made.Clear();
        Faces.Clear();
        Hovered = BookTab.None;
        try
        {
            var tabs = book.GetComponentsInChildren<Renderer>(false).Where(r => r.gameObject.name == "TactiPadTab").ToList();
            if (Camera.main == null || tabs.Count < 2) { RLog.Warning($"Champions of the Sons book: {tabs.Count} game tabs, no tabs added"); return; }
            // Lowest tab along the page's own up axis, so the book's pose (still raising or not) does not matter.
            tabs = tabs.OrderBy(r => Vector3.Dot(r.bounds.center, pageUp)).ToList();
            var lowest = RootTab(tabs[0].transform, book.transform);
            var next = tabs.Skip(1).Select(t => RootTab(t.transform, book.transform)).FirstOrDefault(t => t != null && t != lowest);
            if (lowest == null || next == null || lowest.parent != next.parent)
            {
                RLog.Warning("Champions of the Sons book: the game's tab column was not found, no tabs added");
                return;
            }
            var column = lowest.parent;
            _neighbour = lowest.GetComponent<Collider>();
            _neighbourAnimator = lowest.GetComponent<Animator>();
            var step = lowest.localPosition - next.localPosition;
            // Built under an inactive holder so the copy never runs Awake/OnEnable of the game's tab scripts.
            var holder = new GameObject("CotsTabHolder");
            holder.SetActive(false);
            holder.transform.SetParent(column, false);
            for (int i = 0; i < Defs.Length; i++)
            {
                var d = Defs[i];
                var go = UnityEngine.Object.Instantiate(lowest.gameObject, holder.transform, false);
                go.name = "CotsTab-" + d.Tab;
                StripGameInteraction(go.transform);
                var face = go.GetComponentsInChildren<Renderer>(true).FirstOrDefault(r => r.gameObject.name == "TactiPadTab");
                if (face == null) { UnityEngine.Object.Destroy(go); continue; }
                Paint(face, d);
                go.transform.SetParent(column, false);
                var le = go.GetComponent<LayoutElement>();
                if (le == null) le = go.AddComponent<LayoutElement>();
                le.ignoreLayout = true;
                go.transform.localPosition = lowest.localPosition + step * (i + 1);
                go.transform.localRotation = lowest.localRotation;
                go.transform.localScale = lowest.localScale;
                var an = go.GetComponent<Animator>();
                var outline = go.transform.Find("AnimRoot/Outline/ButtonVisualRoot");
                if (outline != null) outline.gameObject.SetActive(false);
                Made[d.Tab] = new Ours { Face = face, Root = go.transform, Animator = an, Hover = go.GetComponent<Collider>(), Outline = outline != null ? outline.gameObject : null };
                Faces[d.Tab] = face;
            }
            UnityEngine.Object.Destroy(holder);
            Apply();
        }
        catch (Exception e)
        {
            RLog.Error($"Champions of the Sons book: adding tabs failed: {e}");
        }
    }

    /// <summary>The root tab (directly in the tab column) that holds a tab mesh: its topmost ancestor with a <see cref="HeldBookInteraction"/>.</summary>
    private static Transform? RootTab(Transform mesh, Transform book)
    {
        Transform? root = null;
        for (var t = mesh; t != null && t != book; t = t.parent)
            if (t.GetComponent<HeldBookInteraction>() != null) root = t;
        return root;
    }

    /// <summary>
    /// Removes what makes the copy a game tab: its sub-tabs (anything below the root that carries its own
    /// interaction) and, on the root, the interaction and the mouse-event proxy. Keeps the Animator and the collider.
    /// </summary>
    private static void StripGameInteraction(Transform root)
    {
        foreach (var sub in root.GetComponentsInChildren<HeldBookInteraction>(true).Where(h => h.transform != root).Select(h => h.gameObject).ToList())
            UnityEngine.Object.DestroyImmediate(sub);
        foreach (var c in root.GetComponents<Component>())
        {
            var n = c.GetIl2CppType().Name;
            if (n is "HeldBookInteraction" or "MouseEventsProxy") UnityEngine.Object.DestroyImmediate(c);
        }
    }

    private static void Paint(Renderer face, TabDef d)
    {
        var mats = face.materials;
        if (mats.Length > 0) mats[0].SetColor("_BaseColor", PageKit.Hex(d.ColorHex));
        var icon = Face(d.IconId);
        if (mats.Length > 1 && icon != null)
        {
            mats[1].SetTexture("_BaseColorMap", icon);
            mats[1].SetTexture("_MainTex", icon);
        }
        face.materials = mats;
    }

    /// <summary>Marks whether one of our pages shows: the tab stays slid out then, like the game's open category.</summary>
    public static void Select(BookTab tab)
    {
        _pageShown = tab != BookTab.None;
        Apply();
    }

    /// <summary>Per frame while the book is open: hover from the book cursor.</summary>
    public static void Tick(Vector2 cursor)
    {
        Hovered = HitTest(cursor);
        Apply();
    }

    private static void Apply()
    {
        foreach (var (t, o) in Made)
        {
            // The game's hovered tab also shows its input-prompt frame (Outline/ButtonVisualRoot, a white outline).
            bool lit = Hovered == t;
            if (o.Outline != null && o.Lit != lit) { o.Lit = lit; o.Outline.SetActive(lit); }
            bool slide = _pageShown || Hovered == t;
            if (o.Animator == null || o.Root == null || (o.Out == slide && o.Animator.GetBool(SlideOutParam) == slide)) continue;
            o.Out = slide;
            o.Animator.SetBool(SlideOutParam, slide);
        }
    }

    /// <summary>Whether our tab shows the hover outline (for the self-test).</summary>
    public static bool OutlineShown(BookTab tab) => Made.TryGetValue(tab, out var o) && o.Outline != null && o.Outline.activeSelf;

    /// <summary>Whether our tab's Animator is set to slide out (for the self-test).</summary>
    public static bool SlidOut(BookTab tab) => Made.TryGetValue(tab, out var o) && o.Animator != null && o.Animator.GetBool(SlideOutParam);

    /// <summary>Where our tab's face sits relative to its root (changes only with its own slide), and the root's place in the column.</summary>
    public static (Vector3 Face, Vector3 Root) Pose(BookTab tab) =>
        Made.TryGetValue(tab, out var o) && o.Face != null && o.Root != null
            ? (o.Root.InverseTransformPoint(o.Face.transform.position), o.Root.localPosition)
            : default;

    /// <summary>Screen center of the game's tab right above ours (for the self-test), and whether its own slide is out.</summary>
    public static Vector2 NeighbourScreenCenter() =>
        Camera.main != null && _neighbour != null ? Camera.main.WorldToScreenPoint(_neighbour.bounds.center) : default;

    public static bool NeighbourSlidOut => _neighbourAnimator != null && _neighbourAnimator.GetBool(SlideOutParam);

    /// <summary>
    /// Self-test: hovers the game's tab above ours through its own mouse handlers (the book's virtual
    /// cursor does not drive the game's mouse hover in a test run).
    /// </summary>
    public static void SlideNeighbourForTest(bool slide)
    {
        var hbi = _neighbour != null ? _neighbour.GetComponent<HeldBookInteraction>() : null;
        if (hbi == null) return;
        if (slide) hbi.OnMouseEnter(); else hbi.OnMouseExit();
    }

    /// <summary>How far the neighbour's animated part sits from its root (grows when it slides out).</summary>
    public static float NeighbourSlideOffset() =>
        _neighbour != null && _neighbour.transform.childCount > 0 ? _neighbour.transform.GetChild(0).localPosition.magnitude : 0;

    /// <summary>
    /// Which of our tabs is under a screen point (the book cursor), if any. Uses the tab root's collider, which does not
    /// move with the slide (like the game's own hover), so a sliding tab never slips out from under the cursor.
    /// </summary>
    public static BookTab HitTest(Vector2 screen)
    {
        var cam = Camera.main;
        if (cam == null) return BookTab.None;
        foreach (var (t, o) in Made)
        {
            if (o.Face == null) continue;
            var b = o.Hover != null ? o.Hover.bounds : o.Face.bounds;
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
        if (cam == null || !Made.TryGetValue(tab, out var o) || o.Face == null) return default;
        return cam.WorldToScreenPoint(o.Hover != null ? o.Hover.bounds.center : o.Face.bounds.center);
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

    private static readonly Dictionary<string, Texture2D?> FaceCache = new();

    /// <summary>
    /// Our tab face: a ready-made icon in the game's tab-icon layout (black frame, white field, flat black glyph;
    /// <c>tools/icons/generate_icons.py booktab</c>), copied into a mipmapped texture so it stays clean when small.
    /// </summary>
    private static Texture2D? Face(string iconId)
    {
        if (FaceCache.TryGetValue(iconId, out var cached)) return cached;
        Texture2D? result = null;
        var src = Icons.IconCache.GetTexture(iconId);
        if (src != null && src.isReadable)
        {
            result = new Texture2D(src.width, src.height, TextureFormat.RGBA32, true) { name = "cots-tab-" + iconId };
            result.SetPixels32(src.GetPixels32());
            result.Apply(true);
            result.filterMode = FilterMode.Trilinear;
            result.wrapMode = TextureWrapMode.Clamp;
            result.hideFlags = HideFlags.HideAndDontSave;
        }
        FaceCache[iconId] = result;
        return result;
    }
}
