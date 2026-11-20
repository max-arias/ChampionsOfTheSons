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
    /// <summary>The part of the page texture the page mesh shows (COS-62 ruler).</summary>
    private static readonly Rect PageUv = Rect.MinMaxRect(0f, 0f, 0.65f, 1f);
    private const string TexProp = "_BaseColorMap";

    private static BlueprintBookController? _book;
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

    /// <summary>The page canvas content (page pixels, origin top-left), built once.</summary>
    public static RectTransform? Page { get; private set; }

    public static BookTab Tab { get; private set; }

    public static bool ModPageActive => Tab != BookTab.None;

    public static BlueprintBookController? Book => _book != null && _book.gameObject != null && _book.gameObject.activeInHierarchy ? _book : null;

    public static bool IsOpen => Book != null;

    public static event Action<BookTab>? TabShown;

    // ------------------------------------------------------------------ open / close

    /// <summary>Opens the book on one of our tabs. False when the player has no guide book.</summary>
    public static bool Open(BookTab tab)
    {
        var inv = LocalPlayer.Inventory;
        if (inv == null) return false;
        if (Book != null) { ShowModPage(tab); return true; }
        if (inv.AmountOf(GuideBookId, false, false) == 0) return false;
        _pending = tab;
        _pendingSince = Time.realtimeSinceStartup;
        return inv.TryEquip(GuideBookId, false, true, false);
    }

    public static void Close()
    {
        ShowGamePage();
        if (Book != null) LocalPlayer.Inventory?.StashHeldItems(false, false);
    }

    /// <summary>Per frame: follows the held book, opens a pending tab once the book is up, keeps our texture on.</summary>
    public static void Tick()
    {
        if (_book == null || _book.gameObject == null)
        {
            _book = null;
            if (_pending != BookTab.None || Time.frameCount % 15 == 0) Attach(UnityEngine.Object.FindObjectOfType<BlueprintBookController>());
        }
        if (Book == null)
        {
            if (Tab != BookTab.None) { Tab = BookTab.None; BookPatches.Block = false; }
            if (_pending != BookTab.None && Time.realtimeSinceStartup - _pendingSince > 5) _pending = BookTab.None;
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

    private static void Attach(BlueprintBookController? book)
    {
        if (book == null || book == _book) return;
        _book = book;
        _ready = false;
        Tab = BookTab.None;
    }

    /// <summary>Once the book is up in the hand: its page material, page axes and our tabs (they need the final pose).</summary>
    private static void Setup()
    {
        if (_book == null) return;
        _pageMat ??= _book._pageMaterial;
        string pageName = string.IsNullOrEmpty(_book._pageRendererName) ? "Page" : _book._pageRendererName;
        _pageRenderer = _book.GetComponentsInChildren<Renderer>(true).FirstOrDefault(r => r.gameObject.name == pageName);
        if (_pageRenderer == null) return; // the book builds its meshes a frame or two after it is enabled
        _ready = true;
        PageAxes();
        BookTabs.Attach(_book, PageUpWorld, PageRightWorld);
        LogState($"set up: page material {(_pageMat != null)}, page renderer {(_pageRenderer != null)}, tabs {BookTabs.Tabs.Count}");
    }

    // ------------------------------------------------------------------ pages

    public static void ShowModPage(BookTab tab)
    {
        if (Book == null) return;
        if (!_ready) Setup();
        if (_pageMat == null) _pageMat = _book!._pageMaterial;
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

    /// <summary>"Up" on the page in world space (towards the title).</summary>
    public static Vector3 PageRightWorld => _pageRenderer != null ? _pageRenderer.transform.TransformDirection(Vector3.right) * _uSign : Vector3.right;

    public static Vector3 PageUpWorld => _pageRenderer != null ? _pageRenderer.transform.TransformDirection(Vector3.up) * _vSign : Vector3.up;

    /// <summary>The book's virtual cursor in screen pixels (moves with mouse and pad).</summary>
    public static Vector2 CursorScreen => Book != null ? Book._mouseScreenPosition : Vector2.zero;

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
