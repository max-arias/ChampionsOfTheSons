using RedLoader;
using Sons.Crafting;
using Sons.Weapon;
using UnityEngine;
using UnityEngine.UI;

namespace ChampionsOfTheSons.Mod.Book;

/// <summary>
/// Our tab on the guide book: a copy of the game's lowest root tab (its object with the slide Animator, the
/// AnimRoot it animates and the tab mesh), a sibling in the book's tab column but turned a quarter and placed on the page's
/// top edge near the right corner (COS-195: below the column the thumb covered it), with a muted tab color and our own icon face. The copy has no game interaction (its <see cref="HeldBookInteraction"/>,
/// mouse proxy and sub-tabs are removed before it ever activates), so the game neither hovers nor clicks it; we drive
/// its Animator's SlideOut ourselves: out while the cursor is over it or while our page shows, like the game's tabs,
/// plus the game's white hover outline while the cursor is over it (COS-131). Clicks are found by our own hit test on the book cursor.
/// COS-168: like the game's tabs, ours carries a chain of sub-tabs (the game nests each sub-tab under the one before it: Tab1, Tab2...
/// under each AnimRoot); ours keeps the copied chain, makes it five long, puts an icon on each and, while one of our pages shows,
/// spreads them leftward along the top edge (COS-195; we move the links ourselves, their Animators are off):
/// Character, Inventory, Spells, Perks and Help.
/// </summary>
internal static class BookTabs
{
    private sealed record TabDef(BookTab Tab, string IconId, string ColorHex);

    /// <summary>
    /// One tab for the mod, keyed by <see cref="BookTab.Gear"/>; it opens the last mod page shown.
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
        /// <summary>A sub-tab's place under its parent when tucked behind the tab before it, and one step along the top edge (COS-195).</summary>
        public Vector3 Rest, Offset;
        /// <summary>A sub-tab's AnimRoot; its Animator is off and we hold it at the link's own place (COS-195).</summary>
        public Transform? Anim;
    }

    /// <summary>How fast the sub-tabs spread along the edge, per second (COS-195).</summary>
    private const float SubSlideRate = 4f;
    private static float _subT;
    /// <summary>The sub-tabs stay out this long after the cursor leaves our tabs, so crossing the gap between two never folds them.</summary>
    private const float HoverLinger = 0.35f;
    /// <summary>How far out the sub-tabs must be before the cursor can hit them.</summary>
    private const float SubHitAt = 0.95f;
    private static float _hoverUntil;

    /// <summary>Whether the sub-tabs are wanted out: one of our pages shows, or the cursor is (or just was) over one of our tabs,
    /// as the game's tabs spread theirs on hover.</summary>
    private static bool SpreadWanted => _pageShown || Time.unscaledTime < _hoverUntil;
    private static GameObject? _firstSub;

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
    public static void Attach(HeldControllerBase book, Vector3 pageUp, Vector3 pageRight, Vector3 pageTopRight)
    {
        Made.Clear();
        Subs.Clear();
        Faces.Clear();
        _firstSub = null;
        _subT = 0;
        _hoverUntil = 0;
        _pageShown = BookHost.ModPageActive;
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
            // COS-195: the top edge. The tab's centre sits as far above the top edge as the game's tabs sit right of the right
            // edge, and as far in from the corner as the game's top tab is; a quarter turn makes the tab's "out" the page's up.
            var highest = RootTab(tabs[^1].transform, book.transform) ?? next;
            Vector3 c0 = Centre(lowest), fromCorner = c0 - pageTopRight;
            float past = Vector3.Dot(fromCorner, pageRight), inset = Vector3.Dot(pageTopRight - Centre(highest), pageUp);
            var target = c0 - pageRight * (past + inset) + pageUp * (past - Vector3.Dot(fromCorner, pageUp));
            var turn = Quaternion.FromToRotation(pageRight, pageUp);
            float stepLen = (lowest.position - next.position).magnitude;
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
                go.transform.localScale = lowest.localScale;
                go.transform.rotation = turn * lowest.rotation;
                go.transform.position = target - pageRight * (stepLen * i) - turn * (c0 - lowest.position);
                var an = go.GetComponent<Animator>();
                var outline = go.transform.Find("AnimRoot/Outline/ButtonVisualRoot");
                if (outline != null) outline.gameObject.SetActive(false);
                Made[d.Tab] = new Ours { Face = face, Root = go.transform, Animator = an, Hover = go.GetComponent<Collider>(), Outline = outline != null ? outline.gameObject : null };
                Faces[d.Tab] = face;
                PickUpSubs(go.transform, pageUp, -pageRight * stepLen);
            }
            UnityEngine.Object.Destroy(holder);
            Apply();
        }
        catch (Exception e)
        {
            RLog.Error($"Champions of the Sons book: adding tabs failed: {e}");
        }
    }

    /// <summary>The middle of a root tab: its collider's centre, from the transform (bounds lag while the book moves).</summary>
    private static Vector3 Centre(Transform tab)
    {
        var box = tab.GetComponent<BoxCollider>();
        return box != null ? tab.TransformPoint(box.center) : tab.position;
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
        if (chain.Count == 0)
        {
            // The tutorial book's tabs carry no sub-tabs: the first link is a copy of the root tab itself (same parts: Animator,
            // collider, AnimRoot with the tab mesh and outline), sitting exactly on it.
            var anim = root.Find("AnimRoot");
            if (anim == null) { RLog.Warning("Champions of the Sons book: the game's tab has no AnimRoot; no sub-tabs added"); return; }
            var first = UnityEngine.Object.Instantiate(root.gameObject, anim, false).transform;
            first.localPosition = Vector3.zero;
            first.localRotation = Quaternion.identity;
            first.localScale = Vector3.one;
            var layout = first.GetComponent<LayoutElement>();
            if (layout != null) UnityEngine.Object.DestroyImmediate(layout);
            chain.Add(first);
        }
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
            // COS-195: the chain runs leftward from the root tab, so the last link is the first page: the row reads left to right.
            var link = chain[i];
            var def = SubDefs[chain.Count - 1 - i];
            link.gameObject.name = "CotsSub-" + def.Tab;
            var visuals = link.Find("AnimRoot/Visuals");
            if (visuals != null) UnityEngine.Object.DestroyImmediate(visuals.gameObject); // the game's page number (COS-180: hiding was undone)
            var face = RootFace(link);
            if (face != null) Paint(face, SubColor, def.IconId);
        }
    }

    /// <summary>Finds our sub-tabs under the root copy (built now or on an earlier attach) and keeps them for slide, hover and hits.
    /// COS-195: each link rests exactly behind the one before it and moves one <paramref name="edgeStep"/> (world) along the top edge.</summary>
    private static void PickUpSubs(Transform root, Vector3 pageUp, Vector3 edgeStep)
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
            // COS-195: with the link's Animator off nothing hides a leftover number text; ours show none.
            foreach (var text in sub.GetComponentsInChildren<TMPro.TextMeshProUGUI>(true))
                if (text != null) text.enabled = false;
            var outline = sub.Find("AnimRoot/Outline/ButtonVisualRoot");
            if (outline != null) outline.gameObject.SetActive(false);
            var along = sub.parent.InverseTransformVector(edgeStep);
            Vector3 alongDir = along.normalized, outDir = sub.parent.InverseTransformVector(pageUp).normalized;
            var rest = sub.localPosition;
            rest -= alongDir * Vector3.Dot(rest, alongDir);
            rest -= outDir * Vector3.Dot(rest, outDir);
            sub.localPosition = rest;
            _firstSub ??= sub.gameObject;
            var animator = sub.GetComponent<Animator>();
            if (animator != null) animator.enabled = false;
            Subs[tab] = new Ours { Face = face, Root = sub, Animator = sub.GetComponent<Animator>(), Hover = sub.GetComponent<Collider>(), Outline = outline != null ? outline.gameObject : null, Rest = rest, Offset = along, Anim = sub.Find("AnimRoot") };
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

    /// <summary>The book was put away: nothing of ours shows or is hovered when it comes up again.</summary>
    public static void Closed()
    {
        _pageShown = false;
        _current = BookTab.None;
        _hoverUntil = 0;
        _subT = 0;
        Hovered = BookTab.None;
    }

    /// <summary>Per frame while the book is open: hover from the book cursor.</summary>
    public static void Tick(Vector2 cursor)
    {
        Hovered = HitTest(cursor);
        Apply();
    }

    private static void Apply()
    {
        if (Hovered != BookTab.None) _hoverUntil = Time.unscaledTime + HoverLinger;
        bool wanted = SpreadWanted;
        foreach (var (t, o) in Made)
        {
            // The game's hovered tab also shows its input-prompt frame (Outline/ButtonVisualRoot, a white outline).
            bool lit = Hovered == t;
            if (o.Outline != null && o.Lit != lit) { o.Lit = lit; o.Outline.SetActive(lit); }
            bool slide = wanted;
            if (o.Animator == null || o.Root == null || (o.Out == slide && o.Animator.GetBool(SlideOutParam) == slide)) continue;
            o.Out = slide;
            o.Animator.SetBool(SlideOutParam, slide);
        }
        // The sub-tabs spread along the top edge while one of our pages shows or the cursor is over our tabs (each rides on the one
        // before it, so one step each makes a row) and hide behind the root tab otherwise, like the game's; the open page's tab is a lighter colour. COS-194: the hovered one
        // grows, eased, as the game's hovered tabs pop out.
        float step = Mathf.Clamp01(Time.unscaledDeltaTime * HoverGrowRate);
        _subT = Mathf.MoveTowards(_subT, wanted ? 1 : 0, Time.unscaledDeltaTime * SubSlideRate);
        float spread = Mathf.SmoothStep(0, 1, _subT);
        // Tucked in, the links sit exactly on each other: off, so they don't flicker through the root tab.
        if (_firstSub != null && _firstSub.activeSelf != (_subT > 0)) _firstSub.SetActive(_subT > 0);
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
                o.Grow = Mathf.MoveTowards(o.Grow, lit ? 1 : 0, step);
                float k = 1 + (HoverScale - 1) * Mathf.SmoothStep(0, 1, o.Grow);
                ft.localScale = o.BaseScale * k;
            }
            o.Out = wanted;
            if (o.Root != null) o.Root.localPosition = o.Rest + o.Offset * spread;
            // The game's slide moves each link's AnimRoot, and the next link rides on it: ours stay level with the link itself.
            if (o.Anim != null) o.Anim.localPosition = Vector3.zero;
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

    /// <summary>Whether a sub-tab is out on the top edge (self-test).</summary>
    public static bool SubSlidOut(BookTab tab) => Subs.TryGetValue(tab, out var o) && o.Out && _subT >= 1;

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
        // Tucked in or still moving, the sub-tabs sit on the root tab: only the root is hit then.
        var cam = Camera.main;
        if (cam == null) return BookTab.None;
        // The screen rectangles of neighbours overlap a little: the tab whose centre is nearest wins, root tab included.
        BookTab best = BookTab.None;
        float bestD = float.MaxValue;
        if (_subT >= SubHitAt)
            foreach (var (t, o) in Subs)
                if (ScreenRect(cam, o) is Rect r && r.Contains(screen) && (r.center - screen).sqrMagnitude < bestD) { best = t; bestD = (r.center - screen).sqrMagnitude; }
        foreach (var (t, o) in Made)
            if (ScreenRect(cam, o) is Rect r && r.Contains(screen) && (r.center - screen).sqrMagnitude < bestD) { best = t; bestD = (r.center - screen).sqrMagnitude; }
        return best;
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
            var px = src.GetPixels32();
            // COS-195: the tab is turned a quarter to stand on the top edge; the icon is turned back so it reads upright.
            if (src.width == src.height)
            {
                int n = src.width;
                var turned = new Color32[px.Length];
                for (int v = 0; v < n; v++)
                    for (int u = 0; u < n; u++)
                        turned[v * n + u] = px[u * n + (n - 1 - v)];
                px = turned;
            }
            result.SetPixels32(px);
            result.Apply(true);
            result.filterMode = FilterMode.Trilinear;
            result.wrapMode = TextureWrapMode.Clamp;
            result.hideFlags = HideFlags.HideAndDontSave;
        }
        FaceCache[iconId] = result;
        return result;
    }
}
