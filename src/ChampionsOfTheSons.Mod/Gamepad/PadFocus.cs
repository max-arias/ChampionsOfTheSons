using ChampionsOfTheSons.Core.Rules;
using UnityEngine;
using UnityEngine.UI;

namespace ChampionsOfTheSons.Mod.Pad;

/// <summary>
/// Directional focus over the buttons of one panel: collects the visible, interactable buttons under a root, moves focus with
/// <see cref="FocusNav"/>, draws a frame around the focused one and parks the <see cref="PadPointer"/> on it so hover tooltips follow.
/// Confirm clicks the focused button (its onClick, plus a virtual left click for controls the panel polls with the pointer,
/// such as inventory slots); secondary is a virtual right click.
/// </summary>
internal static class PadFocus
{
    private static GameObject? _current;
    private static GameObject? _frame;
    private static Image? _frameImage;
    private static Vector2 _lastCenter;

    public static GameObject? Current => _current != null && _current.activeInHierarchy ? _current : null;

    private readonly record struct Item(Button Button, FocusRect Rect, Vector2 ScreenCenter, Vector2 ScreenSize);

    /// <summary>Screen rectangle (pixels, origin bottom-left) of a UI object, worked out in the SUI canvas's own units
    /// (the same basis the panels' hit-tests use), so it does not depend on the overlay canvas having been laid out for a display.</summary>
    private static bool ScreenRectOf(RectTransform rt, out Vector2 centerPx, out Vector2 sizePx)
    {
        var canvasRt = SUI.SUI.SUIViewport.GetComponent<RectTransform>();
        float scale = SUI.SUI.SUIViewport.scaleFactor;
        var canvasRect = canvasRt.rect;
        var corners = new Vector3[4];
        rt.GetWorldCorners(corners);
        var lo = canvasRt.InverseTransformPoint(corners[0]);
        var hi = canvasRt.InverseTransformPoint(corners[2]);
        centerPx = new Vector2(((lo.x + hi.x) / 2 - canvasRect.xMin) * scale, ((lo.y + hi.y) / 2 - canvasRect.yMin) * scale);
        sizePx = new Vector2(Mathf.Abs(hi.x - lo.x) * scale, Mathf.Abs(hi.y - lo.y) * scale);
        return true;
    }

    private static List<Item> Collect(GameObject root)
    {
        var items = new List<Item>();
        float screenH = Screen.height;
        foreach (var b in root.GetComponentsInChildren<Button>(false))
        {
            if (b == null || !b.interactable || !b.gameObject.activeInHierarchy) continue;
            var rt = b.GetComponent<RectTransform>();
            if (rt == null || !ScreenRectOf(rt, out var c, out var size)) continue;
            if (size.x < 2 || size.y < 2) continue;
            items.Add(new Item(b, new FocusRect(c.x - size.x / 2, screenH - (c.y + size.y / 2), size.x, size.y), c, size));
        }
        return items;
    }

    /// <summary>Logs why controls are or are not found under a root (self-test diagnostics).</summary>
    public static void Diagnose(GameObject root)
    {
        var canvasRt = SUI.SUI.SUIViewport.GetComponent<RectTransform>();
        SelfTest.Log($"pad: diag screen {Screen.width}x{Screen.height} canvasRect {canvasRt.rect} scale {SUI.SUI.SUIViewport.scaleFactor} lossy {canvasRt.lossyScale} mode {SUI.SUI.SUIViewport.renderMode}");
        var all = root.GetComponentsInChildren<Button>(false);
        SelfTest.Log($"pad: diag {all.Length} active buttons under root, {Count(root)} focusable");
        int n = 0;
        foreach (var b in all)
        {
            if (n++ >= 3) break;
            var rt = b.GetComponent<RectTransform>();
            var c = new Vector3[4];
            rt.GetWorldCorners(c);
            ScreenRectOf(rt, out var cp, out var sp);
            SelfTest.Log($"pad: diag button interactable={b.interactable} world {c[0]}..{c[2]} -> px center {cp} size {sp}; rect {rt.rect} localPos {rt.localPosition} anchored {rt.anchoredPosition} anchors {rt.anchorMin}-{rt.anchorMax} lossy {rt.lossyScale}");
        }
    }

    /// <summary>The number of focusable controls under <paramref name="root"/> right now.</summary>
    public static int Count(GameObject root) => Collect(root).Count;

    /// <summary>Focuses the control nearest to the pointer (or the top-left one when there is no pointer yet).</summary>
    public static bool Enter(GameObject root)
    {
        var items = Collect(root);
        if (items.Count == 0) { Clear(); return false; }
        var rects = items.Select(i => i.Rect).ToList();
        int idx = PadPointer.Active
            ? FocusNav.Nearest(rects, PadPointer.Position.x, Screen.height - PadPointer.Position.y)
            : FocusNav.Nearest(rects, 0, 0);
        Focus(items[idx], root);
        return true;
    }

    /// <summary>Moves focus one step. Re-enters when nothing is focused. False when there was nothing that way.</summary>
    public static bool Move(GameObject root, FocusDirection dir)
    {
        var items = Collect(root);
        if (items.Count == 0) { Clear(); return false; }
        int cur = _current == null ? -1 : items.FindIndex(i => i.Button.gameObject == _current);
        if (cur < 0)
        {
            // focus lost (the control was rebuilt or hidden): continue from where it was
            var rects0 = items.Select(i => i.Rect).ToList();
            Focus(items[FocusNav.Nearest(rects0, _lastCenter.x, Screen.height - _lastCenter.y)], root);
            return true;
        }
        int next = FocusNav.Move(items.Select(i => i.Rect).ToList(), cur, dir);
        if (next < 0) return false;
        Focus(items[next], root);
        return true;
    }

    /// <summary>Focuses a specific control under <paramref name="root"/> (self-tests; false when it is not focusable right now).</summary>
    public static bool FocusOn(GameObject root, GameObject target)
    {
        var item = Collect(root).FirstOrDefault(i => i.Button.gameObject == target);
        if (item.Button == null) return false;
        Focus(item, root);
        return true;
    }

    /// <summary>Clicks the focused control. False when nothing is focused.</summary>
    public static bool Confirm()
    {
        var go = Current;
        if (go == null) return false;
        var b = go.GetComponent<Button>();
        if (b == null || !b.interactable) return false;
        PadPointer.MoveTo(CenterOf(go));
        PadPointer.Click(0);
        b.onClick.Invoke();
        return true;
    }

    /// <summary>A right click on the focused control (item menus).</summary>
    public static bool Secondary()
    {
        var go = Current;
        if (go == null) return false;
        PadPointer.MoveTo(CenterOf(go));
        PadPointer.Click(1);
        return true;
    }

    /// <summary>Keeps the frame on the focused control (it can move when a panel relayouts) and hides it when there is none.</summary>
    public static void Refresh()
    {
        var go = Current;
        if (go == null || _frame == null) { if (_frame != null) _frame.SetActive(false); return; }
        PlaceFrame(go);
    }

    public static void Clear()
    {
        _current = null;
        if (_frame != null) _frame.SetActive(false);
    }

    private static Vector2 CenterOf(GameObject go)
    {
        ScreenRectOf(go.GetComponent<RectTransform>(), out var c, out _);
        return c;
    }

    private static void Focus(Item item, GameObject root)
    {
        _current = item.Button.gameObject;
        _lastCenter = item.ScreenCenter;
        PadPointer.MoveTo(item.ScreenCenter);
        EnsureFrame(root);
        PlaceFrame(_current);
    }

    private static void EnsureFrame(GameObject root)
    {
        if (_frame == null)
        {
            _frame = new GameObject("CotsPadFocus");
            _frameImage = _frame.AddComponent<Image>();
            _frameImage.color = new Color(1f, 0.84f, 0.3f, 0.14f);
            _frameImage.raycastTarget = false;
            var outline = _frame.AddComponent<Outline>();
            outline.effectColor = new Color(1f, 0.84f, 0.3f, 1f);
            outline.effectDistance = new Vector2(3f, 3f);
        }
        if (_frame.transform.parent != root.transform) _frame.transform.SetParent(root.transform, false);
        _frame.SetActive(true);
    }

    private static void PlaceFrame(GameObject target)
    {
        if (_frame == null) return;
        var rt = (RectTransform)_frame.transform;
        var canvasRt = SUI.SUI.SUIViewport.GetComponent<RectTransform>();
        var corners = new Vector3[4];
        target.GetComponent<RectTransform>().GetWorldCorners(corners);
        var lo = canvasRt.InverseTransformPoint(corners[0]);
        var hi = canvasRt.InverseTransformPoint(corners[2]);
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.localScale = Vector3.one;
        rt.sizeDelta = new Vector2(Mathf.Abs(hi.x - lo.x), Mathf.Abs(hi.y - lo.y));
        rt.position = canvasRt.TransformPoint(new Vector3((lo.x + hi.x) / 2, (lo.y + hi.y) / 2, 0));
        rt.SetAsLastSibling();
    }
}
