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
/// COS-168: like the game's tabs, ours carries a chain of sub-tabs (the game nests each sub-tab under the one before it: Tab1, Tab2...
/// under each AnimRoot); ours keeps the copied chain, makes it five long, puts an icon on each and slides them out while one of our
/// pages shows: Character, Inventory, Spells, Perks and Help.
/// </summary>
internal static class BookTabs
{
    private sealed record TabDef(BookTab Tab, string IconId, string ColorHex);

    /// <summary>
    /// One tab for the mod (only one fits under the game's column), keyed by <see cref="BookTab.Gear"/>; it opens the last mod page shown.
    /// Its sub-tabs (<see cref="SubDefs"/>) open each page.
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
        public bool Out, Lit, Open;
        /// <summary>Hover growth 0..1 and the face's own scale (COS-194).</summary>
        public float Grow;
        public Vector3 BaseScale;
    }

    /// <summary>A hovered sub-tab's face grows to this scale, at this rate per second (COS-194).</summary>
    private const float HoverScale = 1.18f, HoverGrowRate = 8f;

    private static readonly Dictionary<BookTab, Ours> Made = new();

    /// <summary>The sub-tabs, one per mod page (COS-168). Inventory is keyed <see cref="BookTab.Bag"/>: the root tab keeps <see cref="BookTab.Gear"/>.</summary>
    private static readonly (BookTab Tab, string IconId)[] SubDefs =
    {
        (BookTab.Character, "ui.book_tab.character"), (BookTab.Bag, "ui.book_tab.inventory"), (BookTab.Spells, "ui.book_tab.spells"),
        (BookTab.Perks, "ui.book_tab.perks"), (BookTab.Help, "ui.book_tab.help"),
    };

    private const string SubColor = "#7A4228", SubOpenColor = "#C0703F";
    private static readonly Dictionary<BookTab, Ours> Subs = new();
    private static BookTab _current = BookTab.None;
    private static readonly Dictionary<BookTab, Renderer> Faces = new();
    private static bool _pageShown;
    private static Collider? _neighbour;
    private static Animator? _neighbourAnimator;

    public static IReadOnlyDictionary<BookTab, Renderer> Tabs => Faces;

    /// <summary>The tab the cursor was over on the last <see cref="Tick"/> (None if none).</summary>
    public static BookTab Hovered { get; private set; }

    /// <summary>Puts our tab on a game book, or picks up the one already there (the player switches between the two books).</summary>
    public static void Attach(HeldControllerBase book, Vector3 pageUp)
    {
        Made.Clear();
        Subs.Clear();
        Faces.Clear();
        Hovered = BookTab.None;
        try
        {
            var tabs = book.GetComponentsInChildren<Renderer>(false).Where(r => r.gameObject.name == "TactiPadTab" && !IsOurs(r.transform)).ToList();
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
                var existing = column.Find("CotsTab-" + d.Tab);
                var go = existing != null ? existing.gameObject : UnityEngine.Object.Instantiate(lowest.gameObject, holder.transform, false);
                var face = go.GetComponentsInChildren<Renderer>(true).FirstOrDefault(r => r.gameObject.name == "TactiPadTab");
                if (existing == null)
                {
                    go.name = "CotsTab-" + d.Tab;
                    StripGameInteraction(go.transform);
                    face = RootFace(go.transform);
                    if (face == null) { UnityEngine.Object.Destroy(go); continue; }
                    Paint(face, d.ColorHex, d.IconId);
                    BuildSubChain(go.transform);
                }
                else face = RootFace(go.transform);
                if (face == null) continue;
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
                PickUpSubs(go.transform);
            }
            UnityEngine.Object.Destroy(holder);
            Apply();
        }
        catch (Exception e)
        {
            RLog.Error($"Champions of the Sons book: adding tabs failed: {e}");
        }
    }

    private static bool IsOurs(Transform t)
    {
        for (; t != null; t = t.parent)
            if (t.gameObject.name.StartsWith("CotsTab-", StringComparison.Ordinal)) return true;
        return false;
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
    /// Removes what makes the copy a game tab: the interaction and the mouse-event proxy on the root and on every sub-tab of its
    /// chain (COS-168 keeps the chain itself). Keeps the Animators and the colliders.
    /// </summary>
    private static void StripGameInteraction(Transform root)
    {
        foreach (var c in root.GetComponentsInChildren<Component>(true).ToList())
        {
            if (c == null) continue;
            var n = c.GetIl2CppType().Name;
            if (n is "HeldBookInteraction" or "MouseEventsProxy") UnityEngine.Object.DestroyImmediate(c);
        }
    }

    /// <summary>The tab mesh directly under a tab's own AnimRoot (not a sub-tab's).</summary>
    private static Renderer? RootFace(Transform tab)
    {
        var anim = tab.Find("AnimRoot");
        var mesh = anim != null ? anim.Find("TactiPadTab") : null;
        return mesh != null ? mesh.GetComponent<Renderer>() : null;
    }

    /// <summary>The sub-tab nested under a tab: a child of its AnimRoot with its own Animator and collider.</summary>
    private static Transform? SubOf(Transform tab)
    {
        var anim = tab.Find("AnimRoot");
        if (anim == null) return null;
        for (int i = 0; i < anim.childCount; i++)
        {
            var c = anim.GetChild(i);
            if (c.GetComponent<Animator>() != null && c.GetComponent<Collider>() != null) return c;
        }
        return null;
    }

    /// <summary>COS-168: makes the copied sub-tab chain exactly one link per mod page (cloning the last link when the game's tab
    /// has fewer), names each, paints its icon and hides the game's page number.</summary>
    private static void BuildSubChain(Transform root)
    {
        var chain = new List<Transform>();
        for (var sub = SubOf(root); sub != null; sub = SubOf(sub)) chain.Add(sub);
        if (chain.Count == 0) { RLog.Warning("Champions of the Sons book: the game's tab has no sub-tabs to copy; no sub-tabs added"); return; }
        while (chain.Count > SubDefs.Length) { UnityEngine.Object.DestroyImmediate(chain[^1].gameObject); chain.RemoveAt(chain.Count - 1); }
        while (chain.Count < SubDefs.Length)
        {
            var last = chain[^1];
            var clone = UnityEngine.Object.Instantiate(last.gameObject, last.Find("AnimRoot"), false).transform;
            clone.localPosition = last.localPosition;
            clone.localRotation = last.localRotation;
            clone.localScale = last.localScale;
            chain.Add(clone);
        }
        for (int i = 0; i < chain.Count; i++)
        {
            var link = chain[i];
            link.gameObject.name = "CotsSub-" + SubDefs[i].Tab;
            var visuals = link.Find("AnimRoot/Visuals");
            if (visuals != null) UnityEngine.Object.DestroyImmediate(visuals.gameObject); // the game's page number (COS-180: hiding was undone)
            var face = RootFace(link);
            if (face != null) Paint(face, SubColor, SubDefs[i].IconId);
        }
    }

    /// <summary>Finds our sub-tabs under the root copy (built now or on an earlier attach) and keeps them for slide, hover and hits.</summary>
    private static void PickUpSubs(Transform root)
    {
        for (var sub = SubOf(root); sub != null; sub = SubOf(sub))
        {
            var name = sub.gameObject.name;
            if (!name.StartsWith("CotsSub-", StringComparison.Ordinal) || !Enum.TryParse<BookTab>(name.Substring(8), out var tab)) continue;
            var face = RootFace(sub);
            if (face == null) continue;
            // COS-180: the game can turn its page-number text back on; our copies carry none at all.
            var visuals = sub.Find("AnimRoot/Visuals");
            if (visuals != null) UnityEngine.Object.Destroy(visuals.gameObject);
            var outline = sub.Find("AnimRoot/Outline/ButtonVisualRoot");
            if (outline != null) outline.gameObject.SetActive(false);
            Subs[tab] = new Ours { Face = face, Root = sub, Animator = sub.GetComponent<Animator>(), Hover = sub.GetComponent<Collider>(), Outline = outline != null ? outline.gameObject : null };
            Faces[tab] = face;
        }
    }

    private static void Paint(Renderer face, string colorHex, string iconId)
    {
        var mats = face.materials;
        if (mats.Length > 0) mats[0].SetColor("_BaseColor", PageKit.Hex(colorHex));
        var icon = Face(iconId);
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
        _current = tab == BookTab.Gear ? BookTab.Bag : tab;
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
        // The sub-tabs slide out while one of our pages shows (each rides on the one before it, so they move together); the open
        // page's tab is a lighter colour. COS-194: the hovered one grows, eased, as the game's hovered tabs pop out.
        float step = Mathf.Clamp01(Time.unscaledDeltaTime * HoverGrowRate);
        foreach (var (t, o) in Subs)
        {
            bool lit = Hovered == t;
            if (o.Outline != null && o.Lit != lit) { o.Lit = lit; o.Outline.SetActive(lit); }
            bool open = t == _current && _pageShown;
            if (o.Face != null && o.Open != open)
            {
                o.Open = open;
                var mats = o.Face.materials;
                if (mats.Length > 0) mats[0].SetColor("_BaseColor", PageKit.Hex(open ? SubOpenColor : SubColor));
            }
            if (o.Face != null)
            {
                var ft = o.Face.transform;
                if (o.BaseScale == Vector3.zero) o.BaseScale = ft.localScale;
                o.Grow = Mathf.MoveTowards(o.Grow, lit && _pageShown ? 1 : 0, step);
                float k = 1 + (HoverScale - 1) * Mathf.SmoothStep(0, 1, o.Grow);
                ft.localScale = o.BaseScale * k;
            }
            if (o.Animator == null || (o.Out == _pageShown && o.Animator.GetBool(SlideOutParam) == _pageShown)) continue;
            o.Out = _pageShown;
            o.Animator.SetBool(SlideOutParam, _pageShown);
        }
    }

    /// <summary>Page-number texts still showing on our sub-tabs (self-test, COS-180).</summary>
    public static int SubTabTexts() => Subs.Values.Where(o => o.Root != null)
        .Sum(o => o.Root.GetComponentsInChildren<TMPro.TextMeshProUGUI>(false).Count(t => t.enabled && t.gameObject.activeInHierarchy));

    /// <summary>The sub-tabs made on the current book (self-tests).</summary>
    public static IReadOnlyCollection<BookTab> SubTabs => Subs.Keys;

    /// <summary>Screen center of a sub-tab's collider (self-tests).</summary>
    public static Vector2 SubScreenCenter(BookTab tab)
    {
        var cam = Camera.main;
        if (cam == null || !Subs.TryGetValue(tab, out var o)) return default;
        return cam.WorldToScreenPoint(o.Hover != null ? o.Hover.bounds.center : o.Face.bounds.center);
    }

    /// <summary>Whether our tab shows the hover outline (for the self-test).</summary>
    public static bool OutlineShown(BookTab tab) => Made.TryGetValue(tab, out var o) && o.Outline != null && o.Outline.activeSelf;

    /// <summary>Whether a sub-tab's Animator is set to slide out (self-test).</summary>
    public static bool SubSlidOut(BookTab tab) => Subs.TryGetValue(tab, out var o) && o.Animator != null && o.Animator.GetBool(SlideOutParam);

    /// <summary>How far a sub-tab's hover growth has gone, 0..1 (self-test, COS-194).</summary>
    public static float SubGrow(BookTab tab) => Subs.TryGetValue(tab, out var o) ? o.Grow : 0;

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
    /// <summary>Sub-tabs first (only while they are slid out), the nearest one whose screen rectangle holds the point; then the root tab
    /// (<see cref="BookTab.Gear"/>: it opens the last mod page).</summary>
    public static BookTab HitTest(Vector2 screen)
    {
        var cam = Camera.main;
        if (cam == null) return BookTab.None;
        if (_pageShown)
        {
            BookTab best = BookTab.None;
            float bestD = float.MaxValue;
            foreach (var (t, o) in Subs)
                if (ScreenRect(cam, o) is Rect r && r.Contains(screen) && (r.center - screen).sqrMagnitude < bestD) { best = t; bestD = (r.center - screen).sqrMagnitude; }
            if (best != BookTab.None) return best;
        }
        foreach (var (t, o) in Made)
            if (ScreenRect(cam, o) is Rect r && r.Contains(screen)) return t;
        return BookTab.None;
    }

    private static Rect? ScreenRect(Camera cam, Ours o)
    {
        if (o.Face == null) return null;
        var b = o.Hover != null ? o.Hover.bounds : o.Face.bounds;
        float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
        for (int i = 0; i < 8; i++)
        {
            var c = new Vector3((i & 1) == 0 ? b.min.x : b.max.x, (i & 2) == 0 ? b.min.y : b.max.y, (i & 4) == 0 ? b.min.z : b.max.z);
            var sp = cam.WorldToScreenPoint(c);
            x0 = Mathf.Min(x0, sp.x); y0 = Mathf.Min(y0, sp.y); x1 = Mathf.Max(x1, sp.x); y1 = Mathf.Max(y1, sp.y);
        }
        return Rect.MinMaxRect(x0, y0, x1, y1);
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
        if (IsOurs(go.transform)) return false;
        if (go.name == "TactiPadTab") return true;
        var gameTabs = BookHost.GameTabs;
        if (gameTabs != null)
            foreach (var t in gameTabs)
                if (t != null && t.Pointer == hbi.Pointer) return true;
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
