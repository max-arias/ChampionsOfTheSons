using ChampionsOfTheSons.Core;
using ChampionsOfTheSons.Core.Rules;
using SUI;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using static SUI.SUI;

namespace ChampionsOfTheSons.Mod.UI;

/// <summary>Small SUI helpers shared by the perk, crafting and spell panels.</summary>
internal static class PanelKit
{
    public const float WinW = 1640, WinH = 900;

    public static Color C(string hex) => ColorFromString(hex);

    public static SLabelOptions Label(string text, int size, string colorHex, TextAlignmentOptions align, bool wrap = false)
    {
        var l = SLabel.Text(text).FontSize(size).FontColor(colorHex).Alignment(align).Wrap(wrap);
        l.TextObject.raycastTarget = false;
        l.TextObject.richText = true;
        return l;
    }

    /// <summary>A button whose label never steals the pointer; <paramref name="text"/> auto-sizes between the given font sizes.</summary>
    public static SBgButtonOptions Button(string text, float x, float y, float w, float h, Action click, int minFont = 9, int maxFont = 16)
    {
        var btn = SBgButton.Text(text).FontColor(UiTheme.Text).Notify(click);
        btn.TopLeft(x, y).Size(w, h);
        btn.Color(C(UiTheme.SlotFrameEmpty), C("#3C4756"));
        var t = btn.TextObject;
        t.enableWordWrapping = true;
        t.enableAutoSizing = true;
        t.fontSizeMin = minFont;
        t.fontSizeMax = maxFont;
        t.overflowMode = TextOverflowModes.Truncate;
        t.raycastTarget = false;
        return btn;
    }

    /// <summary>Dark plate under a button's label; the button's own image is the colored frame around it.</summary>
    public static void InnerPlate(SBgButtonOptions btn, float margin = 2f)
    {
        var inner = SContainer.Anchor(AnchorType.Fill).Margin(margin).Background(C(UiTheme.SlotInner), EBackground.None);
        btn.Add(inner);
        inner.Root.transform.SetAsFirstSibling();
        inner.Root.GetComponent<Image>().raycastTarget = false;
    }

    /// <summary>A straight line between two points (top-left origin, canvas units) as a rotated thin rectangle.</summary>
    public static SContainerOptions Line(float x1, float y1, float x2, float y2, string colorHex, float thickness)
    {
        float dx = x2 - x1, dy = y2 - y1;
        var line = SContainer.TopLeft(0, 0).Size(Mathf.Sqrt(dx * dx + dy * dy), thickness).Background(C(colorHex), EBackground.None);
        var rt = line.RectTransform;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = new Vector2((x1 + x2) / 2, -(y1 + y2) / 2);
        rt.localRotation = Quaternion.Euler(0, 0, Mathf.Atan2(-dy, dx) * Mathf.Rad2Deg);
        line.Root.GetComponent<Image>().raycastTarget = false;
        return line;
    }

    /// <summary>Finds the registered object under a screen point with a UI raycast (how a real pointer would).</summary>
    public static string? RaycastKey(IReadOnlyDictionary<int, string> byObjectId, Vector2 screenPoint)
    {
        if (EventSystem.current == null || SUIRaycaster == null) return null;
        var hits = new Il2CppSystem.Collections.Generic.List<RaycastResult>();
        SUIRaycaster.Raycast(new PointerEventData(EventSystem.current) { position = screenPoint }, hits);
        foreach (var hit in hits)
        {
            var t = hit.gameObject.transform;
            while (t != null)
            {
                if (byObjectId.TryGetValue(t.gameObject.GetInstanceID(), out var key)) return key;
                t = t.parent;
            }
        }
        return null;
    }

    /// <summary>Screen point (pixels, origin bottom-left) of a point given in window coordinates (top-left origin) of a centered window.</summary>
    public static Vector2 ScreenPointInWindow(float winX, float winY, float winW = WinW, float winH = WinH)
    {
        var canvas = SUIViewport.GetComponent<RectTransform>().rect;
        float cx = (canvas.width - winW) / 2 + winX;
        float cy = (canvas.height - winH) / 2 + winY;
        float scale = SUIViewport.scaleFactor;
        return new Vector2(cx * scale, (canvas.height - cy) * scale);
    }

    /// <summary>Removes one piece of a base item from the bag (one from a stack). False when none is there.</summary>
    public static bool ConsumeOne(Inventory inv, string baseId)
    {
        foreach (var (slot, item) in inv.Slots.ToList())
        {
            if (!slot.StartsWith("bag.", StringComparison.Ordinal) || item.BaseId != baseId) continue;
            if (item.Amount > 1) inv.Restore(inv.Slots.Select(kv => kv.Key == slot ? new KeyValuePair<string, ItemInstance>(slot, item with { Amount = item.Amount - 1 }) : kv).ToList());
            else inv.Remove(slot);
            return true;
        }
        return false;
    }

    public static int Count(Inventory inv, string baseId) =>
        inv.Slots.Where(kv => kv.Key.StartsWith("bag.", StringComparison.Ordinal) && kv.Value.BaseId == baseId).Sum(kv => Math.Max(1, kv.Value.Amount));
}
