using System.Text;
using Sons.Weapon;
using TheForest.Utils;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ChampionsOfTheSons.Mod.BookSpike;

/// <summary>
/// COS-62 spike, self-test only: opens the game's guide book, logs how its pages are drawn, then renders a mod
/// canvas into a RenderTexture and puts it on the page material. No effect outside --cots-selftest.
/// </summary>
public sealed class BookSpikeFeature : IModFeature
{
    private const int PageLayer = 31;
    /// <summary>The game's "BlueprintBook" item (the building guide book).</summary>
    private const int GuideBookId = 552;
    private static RenderTexture? _rt;
    private static Material? _pageMat;
    private static string _texProp = "_BaseColorMap";
    private static GameObject? _rig;

    public void Update(float dt, bool inWorld)
    {
        // The book may re-apply its own page texture; keep ours on while the spike runs.
        if (_rt != null && _pageMat != null) _pageMat.SetTexture(_texProp, _rt);
    }

    public void RegisterSelfTests()
    {
        SelfTest.Add(1f, "book: unpause", () =>
        {
            // The test save is an early-access save: loading it shows a warning over the pause menu, so the game sits paused.
            var pause = Sons.Gui.PauseMenu._instance;
            SelfTest.Log($"book: pause menu {(pause == null ? "none" : Sons.Gui.PauseMenu.IsActive ? "open" : "closed")}, timeScale {Time.timeScale}");
            foreach (var w in UnityEngine.Object.FindObjectsOfType<Sons.Gui.VersionUpgrade.ShowLegacySaveWarning>()) w.gameObject.SetActive(false);
            pause?.Close();
            SelfTest.Log($"book: after close: pause {(pause == null ? "none" : Sons.Gui.PauseMenu.IsActive ? "open" : "closed")}, timeScale {Time.timeScale}");
        });
        SelfTest.Add(3f, "book: open", () =>
        {
            var inv = LocalPlayer.Inventory;
            SelfTest.Log($"book: inventory {(inv == null ? "null" : "ok")}, guide books held {inv?.AmountOf(GuideBookId, false, false)}");
            if (inv == null) return;
            if (inv.AmountOf(GuideBookId, false, false) == 0) inv.AddItem(GuideBookId, 1, false, false, null);
            bool ok = inv.TryEquip(GuideBookId, false, true, false);
            SelfTest.Log($"book: TryEquip({GuideBookId}) = {ok}");
        });
        SelfTest.Add(7f, "book: inspect", Inspect);
        SelfTest.Add(7.5f, "book: shot open", () => TestKit.Screenshot("book-01-open"));
        SelfTest.Add(8f, "book: mod page", ModPage);
        SelfTest.Add(10f, "book: shot mod page", () => TestKit.Screenshot("book-02-mod-page"));
        SelfTest.Add(11f, "book: close", () =>
        {
            _rt = null;
            var c = Controller();
            if (c != null) HideGamePageObjects(c, false);
            if (_rig != null) UnityEngine.Object.Destroy(_rig);
            LocalPlayer.Inventory?.StashHeldItems(false, false);
        });
    }

    private static BlueprintBookController? Controller() => UnityEngine.Object.FindObjectOfType<BlueprintBookController>();

    private static void Inspect()
    {
        var c = Controller();
        SelfTest.Check("book: controller found", c != null);
        if (c == null) return;
        var sb = new StringBuilder();
        sb.Append($"book: go={c.gameObject.name} active={c.gameObject.activeInHierarchy} page={c._currentPageIndex} rendererName={c._pageRendererName} padRenderer={c._padRendererName}");
        var pm = c._pageMaterial;
        if (pm != null)
        {
            sb.Append($"\n  pageMaterial={pm.name} shader={pm.shader?.name}");
            foreach (var p in pm.GetTexturePropertyNames())
            {
                var t = pm.GetTexture(p);
                if (t != null) sb.Append($"\n    tex {p} = {t.name} {t.width}x{t.height} ({t.GetIl2CppType().Name})");
            }
        }
        foreach (var r in c.GetComponentsInChildren<Renderer>(true))
            sb.Append($"\n  renderer {r.gameObject.name} ({r.GetIl2CppType().Name}) layer={r.gameObject.layer} mats={string.Join(",", r.sharedMaterials.Select(m => m == null ? "null" : m.name))}");
        foreach (var cv in c.GetComponentsInChildren<Canvas>(true))
            sb.Append($"\n  canvas {cv.gameObject.name} mode={cv.renderMode}");
        foreach (var t in c.GetComponentsInChildren<TMP_Text>(true))
            sb.Append($"\n  text {t.gameObject.name} '{t.text}' ({t.GetIl2CppType().Name})");
        sb.Append($"\n  mouse={c._mouseScreenPosition} virtualMouse={(c._virtualMouse != null)} tabs={c._tabs?.Count} pageObjects={c._pageObjectsList?.Count}");
        SelfTest.Log(sb.ToString());
    }

    /// <summary>A uGUI canvas drawn by its own camera into a RenderTexture, swapped onto the book's page material.</summary>
    private static void ModPage()
    {
        var c = Controller();
        if (c == null || c._pageMaterial == null) { SelfTest.Check("book: page material", false); return; }
        _pageMat = c._pageMaterial;
        var current = _pageMat.GetTexture(_texProp) ?? _pageMat.mainTexture;
        int w = current != null ? current.width : 1024, h = current != null ? current.height : 1024;
        _rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { name = "cots-book-page" };
        _rt.Create();

        _rig = new GameObject("cots-book-rig");
        _rig.transform.position = new Vector3(0, -5000, 0);
        var camGo = new GameObject("cam");
        camGo.transform.SetParent(_rig.transform, false);
        var cam = camGo.AddComponent<Camera>();
        cam.orthographic = true;
        cam.cullingMask = 1 << PageLayer;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.86f, 0.83f, 0.76f);
        cam.targetTexture = _rt;
        cam.nearClipPlane = 0.1f;
        cam.farClipPlane = 100f;
        cam.allowHDR = false;
        // No exposure, tonemapping, fog or other volume effects on the page camera.
        var hd = camGo.AddComponent<UnityEngine.Rendering.HighDefinition.HDAdditionalCameraData>();
        hd.volumeLayerMask = 0;
        hd.clearColorMode = UnityEngine.Rendering.HighDefinition.HDAdditionalCameraData.ClearColorMode.Color;
        hd.backgroundColorHDR = cam.backgroundColor;
        hd.antialiasing = UnityEngine.Rendering.HighDefinition.HDAdditionalCameraData.AntialiasingMode.None;

        var canvasGo = new GameObject("canvas");
        canvasGo.layer = PageLayer;
        canvasGo.transform.SetParent(_rig.transform, false);
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceCamera;
        canvas.worldCamera = cam;
        canvas.planeDistance = 10;
        canvasGo.AddComponent<CanvasScaler>();

        // The page mesh only uses part of the texture: lay everything out inside its UV rectangle.
        var uv = PageUvRect(c);
        SelfTest.Log($"book: page uv rect {uv}");
        var content = new GameObject("content");
        content.layer = PageLayer;
        content.transform.SetParent(canvasGo.transform, false);
        var crt = content.AddComponent<RectTransform>();
        crt.anchorMin = uv.min;
        crt.anchorMax = uv.max;
        crt.offsetMin = Vector2.zero;
        crt.offsetMax = Vector2.zero;
        canvasGo = content;
        HideGamePageObjects(c, true);

        Add(canvasGo, "title", new Vector2(0.08f, 0.88f), new Vector2(0.92f, 0.96f), null, "CHAMPIONS: GEAR", 64, new Color(0.16f, 0.15f, 0.13f));
        var colors = new[] { "#9A968C", "#6E8B4E", "#4F7FA6", "#E3B33A", "#7A4A82", "#D9622B", "#9E2B2B", "#8C6CFF" };
        for (int i = 0; i < 24; i++)
        {
            float x = 0.1f + (i % 6) * 0.135f, y = 0.72f - (i / 6) * 0.135f;
            ColorUtility.TryParseHtmlString(colors[i % colors.Length], out var col);
            Add(canvasGo, "slot" + i, new Vector2(x, y), new Vector2(x + 0.115f, y + 0.115f), col, null, 0, Color.clear);
        }
        Add(canvasGo, "note", new Vector2(0.08f, 0.08f), new Vector2(0.92f, 0.16f), null, "drawn by the mod into the book's page texture", 36, new Color(0.16f, 0.15f, 0.13f));

        _pageMat.SetTexture(_texProp, _rt);
        SelfTest.Log($"book: page texture swapped to {w}x{h} RenderTexture via {_texProp}");
        SelfTest.Check("book: mod page applied", _pageMat.GetTexture(_texProp) == _rt);
    }

    private static readonly List<GameObject> Hidden = new();

    /// <summary>The game's page text and selection outlines draw over the page; hide them while the mod page shows.</summary>
    private static void HideGamePageObjects(BlueprintBookController c, bool hide)
    {
        if (!hide)
        {
            foreach (var go in Hidden) if (go != null) go.SetActive(true);
            Hidden.Clear();
            return;
        }
        foreach (var t in c.GetComponentsInChildren<TMP_Text>(false))
            if (t.gameObject.activeSelf) { Hidden.Add(t.gameObject); t.gameObject.SetActive(false); }
        foreach (var r in c.GetComponentsInChildren<Renderer>(false))
            if (r.gameObject.name.Contains("Outline") && r.gameObject.activeSelf) { Hidden.Add(r.gameObject); r.gameObject.SetActive(false); }
        SelfTest.Log($"book: hid {Hidden.Count} game page objects");
    }

    /// <summary>The UV bounds of the page mesh (0..1), or the whole texture when the mesh is not readable.</summary>
    private static Rect PageUvRect(BlueprintBookController c)
    {
        foreach (var mf in c.GetComponentsInChildren<MeshFilter>(true))
        {
            if (mf.gameObject.name != c._pageRendererName) continue;
            var mesh = mf.sharedMesh;
            SelfTest.Log($"book: page mesh {mesh?.name} readable={mesh?.isReadable} verts={mesh?.vertexCount}");
            if (mesh == null || !mesh.isReadable) break;
            var uvs = mesh.uv;
            float x0 = 1, y0 = 1, x1 = 0, y1 = 0;
            foreach (var u in uvs) { x0 = Mathf.Min(x0, u.x); y0 = Mathf.Min(y0, u.y); x1 = Mathf.Max(x1, u.x); y1 = Mathf.Max(y1, u.y); }
            return Rect.MinMaxRect(x0, y0, x1, y1);
        }
        return new Rect(0, 0, 1, 1);
    }

    private static void Add(GameObject parent, string name, Vector2 min, Vector2 max, Color? fill, string? text, int size, Color textColor)
    {
        var go = new GameObject(name);
        go.layer = PageLayer;
        go.transform.SetParent(parent.transform, false);
        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = min;
        rt.anchorMax = max;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        if (fill is Color f)
        {
            var img = go.AddComponent<Image>();
            img.color = f;
        }
        if (text != null)
        {
            var t = go.AddComponent<TextMeshProUGUI>();
            t.text = text;
            t.fontSize = size;
            t.color = textColor;
            t.alignment = TextAlignmentOptions.Center;
            t.enableWordWrapping = true;
        }
    }
}
