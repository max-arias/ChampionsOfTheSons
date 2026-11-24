using RedLoader;
using Sons.Weapon;
using TheForest.Utils;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ChampionsOfTheSons.Mod.Book;

internal enum BookTab
{
    None,
    Gear,
    Bag,
    Character,
    /// <summary>The in-game guide (COS-97): what the mod is, the keys, and one page per system.</summary>
    Help,
    /// <summary>Spell list and bar slots (COS-168).</summary>
    Spells,
    /// <summary>Perk trees (COS-168).</summary>
    Perks,
}

/// <summary>
/// The guide book hook (COS-59, from the COS-62 spike). The game's book stays in charge of the hand, animation,
/// cursor and its own tabs; while one of our tabs is selected the page texture is our RenderTexture, the game's page
/// texts are hidden and its page actions are skipped (<see cref="BookPatches"/>).
/// </summary>
internal static class BookHost
{
    /// <summary>The game's "BlueprintBook" item (the building guide book).</summary>
    public const int GuideBookId = 552;
    /// <summary>The game's "TutorialBook" item: a new world's starting package holds it, B raises it, and the blueprint book
    /// switches to it (2026-11-22). Our tab and pages go on both books.</summary>
    public const int TutorialBookId = 589;
    /// <summary>The part of the page texture the page mesh shows (COS-62 ruler).</summary>
    private static readonly Rect PageUv = Rect.MinMaxRect(0f, 0f, 0.65f, 1f);
    private const string TexProp = "_BaseColorMap";

    /// <summary>The book our pages are on: the player's blueprint book or tutorial book, whichever is up.</summary>
    private static HeldControllerBase? _book;
    private static BlueprintBookController? _blueprint;
    private static TutorialBookController? _tutorial;
    private static Material? _pageMat;
    private static Texture? _gameTexture;
    private static Renderer? _pageRenderer;
    private static Bounds _pageLocal;
    private static int _uAxis, _vAxis, _nAxis;
    private static float _uSign = 1, _vSign = 1;
    private static RenderTexture? _rt;
    private static GameObject? _rig;
    private static readonly List<GameObject> Hidden = new();
    private static BookTab _pending;
    private static float _pendingSince;
    private static bool _ready;
    /// <summary>The book item <see cref="Open"/> equipped and is waiting for, and when the searches for it may run next.</summary>
    private static int _pendingId;
    private static float _nextSearch, _nextSceneSearch;

    /// <summary>Scene-wide book searches run so far (self-test, COS-200).</summary>
    public static int SceneSearches { get; private set; }

    /// <summary>The page canvas content (page pixels, origin top-left), built once.</summary>
    public static RectTransform? Page { get; private set; }

    public static BookTab Tab { get; private set; }

    public static bool ModPageActive => Tab != BookTab.None;

    public static HeldControllerBase? Book => Up(_book) ? _book : null;

    private static bool Up(HeldControllerBase? b) => b != null && b.gameObject != null && b.gameObject.activeInHierarchy;

    private static bool OnTutorial => _book != null && _tutorial != null && _book.Pointer == _tutorial.Pointer;

    /// <summary>The book's virtual cursor in screen pixels (moves with mouse and pad); settable for self-tests.</summary>
    public static Vector2 CursorScreen
    {
        get => Book == null ? Vector2.zero : OnTutorial ? _tutorial!._mouseScreenPosition : _blueprint!._mouseScreenPosition;
        set
        {
            if (Book == null) return;
            if (OnTutorial) _tutorial!._mouseScreenPosition = value; else _blueprint!._mouseScreenPosition = value;
        }
    }

    private static Material? BookPageMaterial => OnTutorial ? _tutorial!._pageMaterial : _blueprint?._pageMaterial;

    private static string? BookPageRendererName => OnTutorial ? _tutorial!._pageRendererName : _blueprint?._pageRendererName;

    /// <summary>The game's own tabs on the book that is up.</summary>
    internal static Il2CppSystem.Collections.Generic.List<Sons.Crafting.HeldBookInteraction>? GameTabs =>
        Book == null ? null : OnTutorial ? _tutorial!._tabs : _blueprint?._tabs;

    /// <summary>Which game book is up (for logs and self-tests).</summary>
    public static string Which => Book == null ? "none" : OnTutorial ? "tutorial" : "blueprint";

    public static bool IsOpen => Book != null;

    public static event Action<BookTab>? TabShown;

    // ------------------------------------------------------------------ open / close

    /// <summary>Why the last <see cref="Open"/> failed, for the log and the self-test (Max 2026-11-21: no fallback panel, so every
    /// failure says why).</summary>
    public static string LastFailure { get; private set; } = "";

    internal static bool Fail(string why)
    {
        LastFailure = why;
        return false;
    }

    /// <summary>Logs the last failure with what the player held, and tells the player.</summary>
    internal static void ReportFailure()
    {
        string held;
        try
        {
            held = LocalPlayer.Inventory?.RightHandItem?._itemID.ToString() ?? "nothing";
        }
        catch
        {
            held = "unknown";
        }
        RLog.Warning($"Champions of the Sons: the guide book did not open: {LastFailure} (right hand: {held}, in world: {LocalPlayer.IsInWorld})");
        Loot.LootFeature.Message(UI.UiText.T("ui.book.cannot_open", "The guide book can't open right now."));
    }

    /// <summary>Opens the book on one of our tabs. False, with <see cref="LastFailure"/>, when it cannot.</summary>
    public static bool Open(BookTab tab)
    {
        var inv = LocalPlayer.Inventory;
        if (inv == null) return Fail("no player inventory yet");
        if (Book != null) { ShowModPage(tab); return true; }
        int id = inv.AmountOf(GuideBookId, false, false) > 0 ? GuideBookId : inv.AmountOf(TutorialBookId, false, false) > 0 ? TutorialBookId : 0;
        if (id == 0) return Fail("the player has no guide book");
        _pending = tab;
        _pendingId = id;
        _pendingSince = Time.realtimeSinceStartup;
        _nextSceneSearch = _pendingSince + 0.3f; // the held book is normally found under the player first
        if (!inv.TryEquip(id, false, true, false)) { _pending = BookTab.None; return Fail("the game refused to equip the guide book"); }
        LastFailure = "";
        return true;
    }

    public static void Close()
    {
        ShowGamePage();
        if (Book != null) LocalPlayer.Inventory?.StashHeldItems(false, false);
    }

    /// <summary>Per frame: follows the held book, opens a pending tab once the book is up, keeps our texture on.</summary>
    public static void Tick()
    {
        if (_blueprint == null || _blueprint.gameObject == null) _blueprint = null;
        if (_tutorial == null || _tutorial.gameObject == null) _tutorial = null;
        if (_book == null || _book.gameObject == null) _book = null;
        // The player's own (possibly stowed) books, found once; the scene-wide search only while a tab is waiting to open, only
        // for the book that was asked for, and twice a second at most (COS-132: a scene search costs ~65 ms. COS-200: a player
        // owns one of the two books, so the search for the other ran every frame while the weapon in hand was being put away,
        // and the game crawled at ~7 fps until the book was up: longest with the spear, whose put-away takes longest).
        float now = Time.realtimeSinceStartup;
        if ((_blueprint == null || _tutorial == null) && (_pending != BookTab.None ? now >= _nextSearch : Time.frameCount % 15 == 0))
        {
            _nextSearch = now + 0.1f;
            var t = LocalPlayer.Transform;
            if (_blueprint == null && t != null) _blueprint = t.GetComponentInChildren<BlueprintBookController>(true);
            if (_tutorial == null && t != null) _tutorial = t.GetComponentInChildren<TutorialBookController>(true);
            if (_pending != BookTab.None && now >= _nextSceneSearch)
            {
                _nextSceneSearch = now + 0.5f;
                SceneSearches++;
                if (_pendingId == GuideBookId && _blueprint == null) _blueprint = UnityEngine.Object.FindObjectOfType<BlueprintBookController>();
                if (_pendingId == TutorialBookId && _tutorial == null) _tutorial = UnityEngine.Object.FindObjectOfType<TutorialBookController>();
            }
        }
        // Follow whichever book is up: B raises one of them and the books switch to each other in hand.
        if (!Up(_book))
        {
            if (Up(_blueprint)) Attach(_blueprint);
            else if (Up(_tutorial)) Attach(_tutorial);
        }
        if (Book == null)
        {
            if (Tab != BookTab.None) { Tab = BookTab.None; BookPatches.Block = false; }
            // Put away with one of our pages up: the tabs must not come back spread over the game's page.
            BookTabs.Closed();
            if (_pending != BookTab.None && Time.realtimeSinceStartup - _pendingSince > 5)
            {
                _pending = BookTab.None;
                Fail("the guide book was equipped but never came up within 5 s");
                ReportFailure();
            }
            return;
        }
        // Set up right away (no waiting for the raise animation, so our page never pops in): the page axes are a
        // property of the book mesh, not of its pose.
        if (!_ready || _pageRenderer == null) Setup();
        if (_pending != BookTab.None)
        {
            var t = _pending;
            ShowModPage(t);
            if (Tab == t) _pending = BookTab.None; // else the page material is not there yet: try next frame
        }
        if (ModPageActive && _pageMat != null && _rt != null && _pageMat.GetTexture(TexProp) != _rt) _pageMat.SetTexture(TexProp, _rt);
    }

    private static void Attach(HeldControllerBase? book)
    {
        if (book == null || (_book != null && book.Pointer == _book.Pointer)) return;
        if (Tab != BookTab.None) ShowGamePage(); // gives the old book its page texture and texts back
        _book = book;
        _ready = false;
        _pageMat = null;
        _gameTexture = null;
        _pageRenderer = null;
        Tab = BookTab.None;
        BookPatches.Block = false;
    }

    /// <summary>Once the book is up in the hand: its page material, page axes and our tabs (they need the final pose).</summary>
    private static void Setup()
    {
        if (_book == null) return;
        _pageMat ??= BookPageMaterial;
        string pageName = string.IsNullOrEmpty(BookPageRendererName) ? "Page" : BookPageRendererName!;
        _pageRenderer = _book.GetComponentsInChildren<Renderer>(true).FirstOrDefault(r => r.gameObject.name == pageName);
        if (_pageRenderer == null) return; // the book builds its meshes a frame or two after it is enabled
        _ready = true;
        PageAxes();
        BookTabs.Attach(_book, PageUpWorld, PageRightWorld, PagePointWorld(1, 1));
        LogState($"set up on the {Which} book: page material {(_pageMat != null)}, page renderer {(_pageRenderer != null)}, tabs {BookTabs.Tabs.Count}");
    }

    // ------------------------------------------------------------------ pages

    public static void ShowModPage(BookTab tab)
    {
        if (Book == null) return;
        if (!_ready) Setup();
        if (_pageMat == null) _pageMat = BookPageMaterial;
        if (_pageMat == null) { _pending = tab; _pendingSince = Time.realtimeSinceStartup; return; }
        EnsureRig();
        if (Tab == BookTab.None)
        {
            _gameTexture = _pageMat.GetTexture(TexProp);
            HideGamePageObjects();
        }
        Tab = tab;
        BookPatches.Block = true;
        _pageMat.SetTexture(TexProp, _rt);
        BookTabs.Select(tab);
        TabShown?.Invoke(tab);
    }

    /// <summary>Back to the game's own page (a game tab was clicked or the book closes).</summary>
    public static void ShowGamePage()
    {
        if (Tab == BookTab.None) return;
        Tab = BookTab.None;
        BookPatches.Block = false;
        if (_pageMat != null && _gameTexture != null) _pageMat.SetTexture(TexProp, _gameTexture);
        foreach (var go in Hidden) if (go != null) go.SetActive(true);
        Hidden.Clear();
        BookTabs.Select(BookTab.None);
        TabShown?.Invoke(BookTab.None);
    }

    private static void HideGamePageObjects()
    {
        Hidden.Clear();
        if (_book == null) return;
        foreach (var t in _book.GetComponentsInChildren<TMP_Text>(false))
            if (t.gameObject.activeSelf) { Hidden.Add(t.gameObject); t.gameObject.SetActive(false); }
        foreach (var r in _book.GetComponentsInChildren<Renderer>(false))
            if (r.gameObject.name.Contains("Outline") && r.gameObject.activeSelf) { Hidden.Add(r.gameObject); r.gameObject.SetActive(false); }
    }

    /// <summary>The camera and canvas that draw our page into the RenderTexture (layer <see cref="PageKit.Layer"/> only).</summary>
    private static void EnsureRig()
    {
        if (_rig != null) return;
        _rt = new RenderTexture(1024, 1024, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { name = "cots-book-page" };
        _rt.Create();
        _rig = new GameObject("cots-book-rig");
        UnityEngine.Object.DontDestroyOnLoad(_rig);
        _rig.transform.position = new Vector3(0, -5000, 0);
        var camGo = new GameObject("cam");
        camGo.transform.SetParent(_rig.transform, false);
        var cam = camGo.AddComponent<Camera>();
        cam.orthographic = true;
        cam.cullingMask = 1 << PageKit.Layer;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = PageKit.Paper;
        cam.targetTexture = _rt;
        cam.allowHDR = false;
        // No exposure, tonemapping or fog: without this the page comes out grey-blue (COS-62).
        var hd = camGo.AddComponent<UnityEngine.Rendering.HighDefinition.HDAdditionalCameraData>();
        hd.volumeLayerMask = 0;
        hd.clearColorMode = UnityEngine.Rendering.HighDefinition.HDAdditionalCameraData.ClearColorMode.Color;
        hd.backgroundColorHDR = cam.backgroundColor;
        hd.antialiasing = UnityEngine.Rendering.HighDefinition.HDAdditionalCameraData.AntialiasingMode.None;

        var canvasGo = new GameObject("canvas") { layer = PageKit.Layer };
        canvasGo.transform.SetParent(_rig.transform, false);
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceCamera;
        canvas.worldCamera = cam;
        canvas.planeDistance = 10;
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;

        // The page area of the texture, sized in page pixels so layouts can use absolute numbers.
        var areaGo = new GameObject("page-area") { layer = PageKit.Layer };
        areaGo.transform.SetParent(canvasGo.transform, false);
        var area = areaGo.AddComponent<RectTransform>();
        area.anchorMin = PageUv.min;
        area.anchorMax = PageUv.max;
        area.offsetMin = area.offsetMax = Vector2.zero;
        var pageGo = new GameObject("page") { layer = PageKit.Layer };
        pageGo.transform.SetParent(area, false);
        var page = pageGo.AddComponent<RectTransform>();
        page.anchorMin = page.anchorMax = new Vector2(0.5f, 0.5f);
        page.sizeDelta = new Vector2(PageKit.W, PageKit.H);
        // Scale the fixed-size page to whatever size the area has in the 1024 px texture.
        float sx = PageUv.width * 1024 / PageKit.W, sy = PageUv.height * 1024 / PageKit.H;
        page.localScale = new Vector3(sx, sy, 1);
        Page = page;
        BookPages.Build(page);
    }

    // ------------------------------------------------------------------ cursor

    /// <summary>
    /// The page mesh's axes, measured in COS-62: page right = -local x, page up = +local y, out of the page = local z.
    /// They belong to the mesh, so they hold in any pose (the book can be set up before it is raised).
    /// </summary>
    private static void PageAxes()
    {
        var mesh = _pageRenderer?.GetComponent<MeshFilter>()?.sharedMesh;
        if (_pageRenderer == null || mesh == null) return;
        _pageLocal = mesh.bounds;
        _nAxis = 2;
        _uAxis = 0;
        _uSign = -1;
        _vAxis = 1;
        _vSign = 1;
    }

    public static Vector3 PageUpWorld => _pageRenderer != null ? _pageRenderer.transform.TransformDirection(Vector3.up) * _vSign : Vector3.up;

    public static Vector3 PageRightWorld => _pageRenderer != null ? _pageRenderer.transform.TransformDirection(Vector3.right) * _uSign : Vector3.right;

    /// <summary>A point of the page in world space: u 0..1 left to right, v 0..1 bottom to top.</summary>
    public static Vector3 PagePointWorld(float u, float v)
    {
        if (_pageRenderer == null) return default;
        if (_uSign < 0) u = 1 - u;
        if (_vSign < 0) v = 1 - v;
        var local = _pageLocal.center;
        local[_uAxis] = _pageLocal.min[_uAxis] + u * _pageLocal.size[_uAxis];
        local[_vAxis] = _pageLocal.min[_vAxis] + v * _pageLocal.size[_vAxis];
        return _pageRenderer.transform.TransformPoint(local);
    }

    /// <summary>The cursor as page pixels (origin top-left), by intersecting the camera ray with the page plane.</summary>
    public static bool CursorOnPage(out Vector2 px) => CursorOnPage(CursorScreen, out px);

    /// <summary>A screen point as page pixels (see <see cref="CursorOnPage(out Vector2)"/>).</summary>
    public static bool CursorOnPage(Vector2 screen, out Vector2 px)
    {
        px = default;
        var cam = Camera.main;
        if (Book == null || _pageRenderer == null || cam == null) return false;
        var t = _pageRenderer.transform;
        var normal = t.TransformDirection(_nAxis == 0 ? Vector3.right : _nAxis == 1 ? Vector3.up : Vector3.forward);
        var plane = new Plane(normal, t.TransformPoint(_pageLocal.center));
        var ray = cam.ScreenPointToRay(screen);
        if (!plane.Raycast(ray, out float d)) return false;
        var local = t.InverseTransformPoint(ray.GetPoint(d));
        float u = (local[_uAxis] - _pageLocal.min[_uAxis]) / _pageLocal.size[_uAxis];
        float v = (local[_vAxis] - _pageLocal.min[_vAxis]) / _pageLocal.size[_vAxis];
        if (_uSign < 0) u = 1 - u;
        if (_vSign < 0) v = 1 - v;
        px = PageKit.ToPagePx(new Vector2(u, v));
        return u >= 0 && u <= 1 && v >= 0 && v <= 1;
    }

    /// <summary>Screen pixels of a page point (page pixels), for tests and placing the card.</summary>
    public static Vector2 ScreenOfPagePx(Vector2 px)
    {
        var cam = Camera.main;
        if (_pageRenderer == null || cam == null) return default;
        float u = px.x / PageKit.W, v = 1 - px.y / PageKit.H;
        if (_uSign < 0) u = 1 - u;
        if (_vSign < 0) v = 1 - v;
        var local = _pageLocal.center;
        local[_uAxis] = _pageLocal.min[_uAxis] + u * _pageLocal.size[_uAxis];
        local[_vAxis] = _pageLocal.min[_vAxis] + v * _pageLocal.size[_vAxis];
        return cam.WorldToScreenPoint(_pageRenderer.transform.TransformPoint(local));
    }

    /// <summary>The page's rectangle on screen (pixels, origin bottom-left).</summary>
    public static Rect PageScreenRect()
    {
        var a = ScreenOfPagePx(new Vector2(0, 0));
        var b = ScreenOfPagePx(new Vector2(PageKit.W, PageKit.H));
        return Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
    }

    public static void LogState(string why) => RLog.Msg($"Champions of the Sons book: {why} (book {(Book != null)}, tab {Tab})");
}
