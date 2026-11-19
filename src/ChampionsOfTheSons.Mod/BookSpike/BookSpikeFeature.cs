using Sons.Weapon;
using TheForest.Utils;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ChampionsOfTheSons.Mod.BookSpike;

/// <summary>
/// COS-62 spike, self-test only: opens the game's guide book, draws a mod page into the page texture, adds a tab of
/// our own, and maps the book's cursor onto our page (a dot follows it; a click recolors the slot under it).
/// Findings are in COS-62. No effect outside --cots-selftest.
/// </summary>
public sealed class BookSpikeFeature : IModFeature
{
    private const int PageLayer = 31;
    /// <summary>The game's "BlueprintBook" item (the building guide book).</summary>
    private const int GuideBookId = 552;
    /// <summary>The part of the 1024x1024 page texture the page mesh shows (measured with a ruler page).</summary>
    private static readonly Rect PageUv = Rect.MinMaxRect(0f, 0f, 0.65f, 1f);
    private const string TexProp = "_BaseColorMap";
    private const float OpenSeconds = 40f;

    private static RenderTexture? _rt;
    private static Material? _pageMat;
    private static GameObject? _rig, _ourTab;
    private static RectTransform? _content, _dot;
    private static readonly List<(RectTransform Rt, Image Img)> Slots = new();
    private static readonly List<GameObject> Hidden = new();
    private static BlueprintBookController? _book;
    private static Renderer? _pageRenderer;
    private static Bounds _pageLocal;
    private static int _uAxis, _vAxis, _nAxis;
    private static float _uSign = 1, _vSign = 1;
    private static int _clicks;
    /// <summary>Our own cursor in screen pixels; the game's book controller is switched off while our page shows.</summary>
    private static Vector2 _cursor;
    private static bool _ownCursor;
    private static Quaternion _camAtStart;

    public void Update(float dt, bool inWorld)
    {
        if (_rt == null || _pageMat == null) return;
        _pageMat.SetTexture(TexProp, _rt); // the book re-applies its own page art on flips
        var mouse = UnityEngine.InputSystem.Mouse.current;
        if (_ownCursor && mouse != null)
        {
            _cursor += mouse.delta.ReadValue();
            _cursor.x = Mathf.Clamp(_cursor.x, 0, Screen.width);
            _cursor.y = Mathf.Clamp(_cursor.y, 0, Screen.height);
        }
        if (CursorOnPage(out var p))
        {
            if (_dot != null)
            {
                _dot.gameObject.SetActive(true);
                _dot.anchorMin = _dot.anchorMax = p;
            }
            if (mouse != null && mouse.leftButton.wasPressedThisFrame) Click(p);
        }
        else if (_dot != null) _dot.gameObject.SetActive(false);
    }

    public void RegisterSelfTests()
    {
        SelfTest.Add(1f, "book: unpause", () =>
        {
            // The test save is an early-access save: loading it shows a warning over the pause menu, so the game sits paused.
            foreach (var w in UnityEngine.Object.FindObjectsOfType<Sons.Gui.VersionUpgrade.ShowLegacySaveWarning>()) w.gameObject.SetActive(false);
            Sons.Gui.PauseMenu._instance?.Close();
            SelfTest.Log($"book: timeScale {Time.timeScale}");
        });
        SelfTest.Add(3f, "book: open", () =>
        {
            var inv = LocalPlayer.Inventory;
            if (inv == null) { SelfTest.Check("book: inventory", false); return; }
            if (inv.AmountOf(GuideBookId, false, false) == 0) inv.AddItem(GuideBookId, 1, false, false, null);
            SelfTest.Check("book: equipped", inv.TryEquip(GuideBookId, false, true, false));
        });
        SelfTest.Add(6f, "book: mod page", ModPage);
        SelfTest.Add(6.5f, "book: our tab", CloneTab);
        SelfTest.Add(7f, "book: page axes", PageAxes);
        SelfTest.Add(7.2f, "book: controller off", () =>
        {
            if (_book == null) return;
            BookBlockPatches.ModPageActive = true; // keep the controller running; skip only its page actions
            var cam = Camera.main;
            _cursor = cam != null && _pageRenderer != null ? (Vector2)cam.WorldToScreenPoint(_pageRenderer.bounds.center) : new Vector2(Screen.width / 2f, Screen.height / 2f);
            _ownCursor = false; // the game's virtual cursor keeps working
            _camAtStart = cam != null ? cam.transform.rotation : Quaternion.identity;
            SelfTest.Log($"book: page actions blocked, cursor at {_cursor}, held item still the book: {LocalPlayer.Inventory?.HeldOnlyItemController != null}");
        });
        SelfTest.Add(8f, "book: shot mod page", () => TestKit.Screenshot("book-02-mod-page"));
        SelfTest.Add(8.5f, "book: cursor mapping", () =>
        {
            bool on = CursorOnPage(out var p);
            SelfTest.Log($"book: cursor {_book?._mouseScreenPosition} -> page {(on ? p.ToString() : "off page")}. Move the mouse over the book and click slots for {OpenSeconds - 9:0} s.");
        });
        SelfTest.Add(OpenSeconds - 1, "book: shot after use", () => TestKit.Screenshot("book-03-after-use"));
        SelfTest.Add(OpenSeconds, "book: close", () =>
        {
            var cam = Camera.main;
            SelfTest.Log($"book: {_clicks} clicks on the mod page; camera turned {(cam != null ? Quaternion.Angle(_camAtStart, cam.transform.rotation) : -1):0.0} deg while the book was open; book still held: {UnityEngine.Object.FindObjectOfType<BlueprintBookController>() != null}");
            SelfTest.Log($"book: {BookBlockPatches.Blocked} game page actions blocked");
            BookBlockPatches.ModPageActive = false;
            _rt = null;
            if (_ourTab != null) UnityEngine.Object.Destroy(_ourTab);
            foreach (var go in Hidden) if (go != null) go.SetActive(true);
            Hidden.Clear();
            if (_rig != null) UnityEngine.Object.Destroy(_rig);
            LocalPlayer.Inventory?.StashHeldItems(false, false);
        });
    }

    // ------------------------------------------------------------------ page

    /// <summary>A uGUI canvas drawn by its own camera into a RenderTexture that replaces the page art.</summary>
    private static void ModPage()
    {
        _book = UnityEngine.Object.FindObjectOfType<BlueprintBookController>();
        if (_book == null || _book._pageMaterial == null) { SelfTest.Check("book: page material", false); return; }
        _pageMat = _book._pageMaterial;
        _rt = new RenderTexture(1024, 1024, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { name = "cots-book-page" };
        _rt.Create();

        _rig = new GameObject("cots-book-rig");
        _rig.transform.position = new Vector3(0, -5000, 0);
        var camGo = new GameObject("cam");
        camGo.transform.SetParent(_rig.transform, false);
        var cam = camGo.AddComponent<Camera>();
        cam.orthographic = true;
        cam.cullingMask = 1 << PageLayer;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.89f, 0.86f, 0.79f);
        cam.targetTexture = _rt;
        cam.allowHDR = false;
        // No exposure, tonemapping or fog on the page camera: without this the page comes out grey-blue.
        var hd = camGo.AddComponent<UnityEngine.Rendering.HighDefinition.HDAdditionalCameraData>();
        hd.volumeLayerMask = 0;
        hd.clearColorMode = UnityEngine.Rendering.HighDefinition.HDAdditionalCameraData.ClearColorMode.Color;
        hd.backgroundColorHDR = cam.backgroundColor;
        hd.antialiasing = UnityEngine.Rendering.HighDefinition.HDAdditionalCameraData.AntialiasingMode.None;

        var canvasGo = new GameObject("canvas") { layer = PageLayer };
        canvasGo.transform.SetParent(_rig.transform, false);
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceCamera;
        canvas.worldCamera = cam;
        canvas.planeDistance = 10;
        canvasGo.AddComponent<CanvasScaler>();

        var content = Box(canvasGo.transform, "content", PageUv.min, PageUv.max, null);
        _content = content;
        var ink = new Color(0.16f, 0.15f, 0.13f);
        var header = Box(content, "header", new Vector2(0, 0.9f), new Vector2(1, 1), new Color(0.42f, 0.33f, 0.25f));
        Text(header, "CHAMPIONS: GEAR", 54, new Color(0.95f, 0.92f, 0.85f));
        var colors = new[] { "#9A968C", "#6E8B4E", "#4F7FA6", "#E3B33A", "#7A4A82", "#D9622B", "#9E2B2B", "#8C6CFF" };
        Slots.Clear();
        for (int i = 0; i < 20; i++)
        {
            float x = 0.06f + (i % 4) * 0.225f, y = 0.68f - (i / 4) * 0.13f;
            ColorUtility.TryParseHtmlString(colors[i % colors.Length], out var col);
            var slot = Box(content, "slot" + i, new Vector2(x, y), new Vector2(x + 0.2f, y + 0.115f), col);
            Slots.Add((slot, slot.GetComponent<Image>()));
        }
        var note = Box(content, "note", new Vector2(0.05f, 0.02f), new Vector2(0.95f, 0.08f), null);
        Text(note, "click a slot: it turns white", 30, ink);
        _dot = Box(content, "dot", Vector2.zero, Vector2.zero, new Color(0.72f, 0.2f, 0.16f));
        _dot.sizeDelta = new Vector2(18, 18);

        // The game's page text and selection outlines draw over the page.
        foreach (var t in _book.GetComponentsInChildren<TMP_Text>(false))
            if (t.gameObject.activeSelf) { Hidden.Add(t.gameObject); t.gameObject.SetActive(false); }
        foreach (var r in _book.GetComponentsInChildren<Renderer>(false))
            if (r.gameObject.name.Contains("Outline") && r.gameObject.activeSelf) { Hidden.Add(r.gameObject); r.gameObject.SetActive(false); }

        _pageMat.SetTexture(TexProp, _rt);
        SelfTest.Check("book: mod page applied", _pageMat.GetTexture(TexProp) == _rt);
    }

    private static RectTransform Box(Transform parent, string name, Vector2 min, Vector2 max, Color? fill)
    {
        var go = new GameObject(name) { layer = PageLayer };
        go.transform.SetParent(parent, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = min;
        rt.anchorMax = max;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        if (fill is Color f) go.AddComponent<Image>().color = f;
        return rt;
    }

    private static void Text(RectTransform parent, string text, int size, Color color)
    {
        // One Graphic per GameObject: the text goes on its own child.
        var t = Box(parent, "text", Vector2.zero, Vector2.one, null).gameObject.AddComponent<TextMeshProUGUI>();
        t.text = text;
        t.fontSize = size;
        t.color = color;
        t.alignment = TextAlignmentOptions.Center;
    }

    // ------------------------------------------------------------------ tab

    /// <summary>All tabs sit at the same transform and differ by mesh, so the step comes from the two lowest tabs' world bounds.</summary>
    private static void CloneTab()
    {
        if (_book == null) return;
        var tabs = _book.GetComponentsInChildren<Renderer>(false).Where(r => r.gameObject.name == "TactiPadTab")
            .OrderBy(r => Vector3.Dot(r.bounds.center, Camera.main!.transform.up)).ToList();
        if (tabs.Count < 2) { SelfTest.Check("book: tabs found", false); return; }
        var lowest = tabs[0];
        var next = tabs.Skip(1).First(t => Vector3.Distance(t.bounds.center, lowest.bounds.center) > 0.005f);
        var step = lowest.bounds.center - next.bounds.center;
        _ourTab = UnityEngine.Object.Instantiate(lowest.gameObject, lowest.transform.parent, false);
        _ourTab.name = "CotsTab";
        _ourTab.transform.position = lowest.transform.position + step;
        var r = _ourTab.GetComponent<Renderer>();
        var mats = r.materials;
        if (mats.Length > 0) mats[0].SetColor("_BaseColor", new Color(0.85f, 0.38f, 0.17f));
        var icon = Icons.IconCache.GetTexture("spell.blink");
        if (mats.Length > 1 && icon != null) { mats[1].SetTexture("_BaseColorMap", icon); mats[1].SetTexture("_MainTex", icon); }
        r.materials = mats;
        SelfTest.Log($"book: our tab one step ({step.magnitude * 100:0.0} cm) below the lowest of {tabs.Count} tabs");
        SelfTest.Check("book: our tab cloned", true);
    }

    // ------------------------------------------------------------------ cursor

    /// <summary>Which local axes of the page mesh run right, up and out of the page, from the mesh bounds and the camera.</summary>
    private static void PageAxes()
    {
        if (_book == null) return;
        _pageRenderer = _book.GetComponentsInChildren<Renderer>(false).FirstOrDefault(r => r.gameObject.name == _book._pageRendererName);
        var mesh = _pageRenderer?.GetComponent<MeshFilter>()?.sharedMesh;
        var cam = Camera.main;
        if (_pageRenderer == null || mesh == null || cam == null) { SelfTest.Check("book: page mesh", false); return; }
        _pageLocal = mesh.bounds;
        var size = _pageLocal.size;
        _nAxis = size.x <= size.y && size.x <= size.z ? 0 : size.y <= size.z ? 1 : 2;
        var inPlane = new[] { 0, 1, 2 }.Where(a => a != _nAxis).ToArray();
        var t = _pageRenderer.transform;
        Vector3 Dir(int a) => t.TransformDirection(a == 0 ? Vector3.right : a == 1 ? Vector3.up : Vector3.forward);
        float r0 = Vector3.Dot(Dir(inPlane[0]), cam.transform.right), r1 = Vector3.Dot(Dir(inPlane[1]), cam.transform.right);
        _uAxis = Mathf.Abs(r0) >= Mathf.Abs(r1) ? inPlane[0] : inPlane[1];
        _vAxis = _uAxis == inPlane[0] ? inPlane[1] : inPlane[0];
        _uSign = Mathf.Sign(Vector3.Dot(Dir(_uAxis), cam.transform.right));
        _vSign = Mathf.Sign(Vector3.Dot(Dir(_vAxis), cam.transform.up));
        SelfTest.Log($"book: page mesh bounds {_pageLocal}, normal axis {_nAxis}, u axis {_uAxis} ({_uSign:+0;-0}), v axis {_vAxis} ({_vSign:+0;-0})");
    }

    /// <summary>The book cursor as a 0..1 point on our content area, by intersecting the camera ray with the page plane.</summary>
    private static bool CursorOnPage(out Vector2 p)
    {
        p = default;
        var cam = Camera.main;
        if (_book == null || _pageRenderer == null || cam == null) return false;
        var t = _pageRenderer.transform;
        var normal = t.TransformDirection(_nAxis == 0 ? Vector3.right : _nAxis == 1 ? Vector3.up : Vector3.forward);
        var plane = new Plane(normal, t.TransformPoint(_pageLocal.center));
        var ray = cam.ScreenPointToRay(_ownCursor ? (Vector3)_cursor : (Vector3)_book._mouseScreenPosition);
        if (!plane.Raycast(ray, out float d)) return false;
        var local = t.InverseTransformPoint(ray.GetPoint(d));
        float u = (local[_uAxis] - _pageLocal.min[_uAxis]) / _pageLocal.size[_uAxis];
        float v = (local[_vAxis] - _pageLocal.min[_vAxis]) / _pageLocal.size[_vAxis];
        if (_uSign < 0) u = 1 - u;
        if (_vSign < 0) v = 1 - v;
        p = new Vector2(u, v);
        return u >= 0 && u <= 1 && v >= 0 && v <= 1;
    }

    private static void Click(Vector2 p)
    {
        _clicks++;
        foreach (var (rt, img) in Slots)
        {
            if (p.x < rt.anchorMin.x || p.x > rt.anchorMax.x || p.y < rt.anchorMin.y || p.y > rt.anchorMax.y) continue;
            img.color = Color.white;
            SelfTest.Log($"book: click at {p} hit {rt.gameObject.name}");
            return;
        }
        SelfTest.Log($"book: click at {p} hit nothing");
    }
}
