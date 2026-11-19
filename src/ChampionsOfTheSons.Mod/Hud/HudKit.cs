using System.Globalization;
using ChampionsOfTheSons.Core.Rules;
using ChampionsOfTheSons.Mod.UI;
using SUI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static SUI.SUI;

namespace ChampionsOfTheSons.Mod.Hud;

/// <summary>Small helpers shared by the HUD widgets: labels, absolute placement, fades.</summary>
internal static class HudKit
{
    /// <summary>The player's own camera (Camera.main can be another camera in this game).</summary>
    public static Camera? PlayerCamera()
    {
        var cam = TheForest.Utils.LocalPlayer.MainCam;
        return cam != null ? cam : Camera.main;
    }

    public static Color C(string hex) => ColorFromString(hex);

    /// <summary>Canvas units per screen pixel (the SUI canvas scales with the window).</summary>
    public static float CanvasPerPixel => 1f / Mathf.Max(0.01f, SUIViewport.scaleFactor);

    public static SLabelOptions Label(string text, int size, string colorHex, TextAlignmentOptions align, bool wrap = false)
    {
        var l = SLabel.Text(text).FontSize(size).FontColor(colorHex).Alignment(align).Wrap(wrap);
        l.TextObject.raycastTarget = false;
        l.TextObject.richText = true;
        return l;
    }

    /// <summary>Places a rect by anchor, pivot and offset (canvas units), independent of SUI's own anchoring helpers.</summary>
    public static void Place(RectTransform rt, Vector2 anchor, Vector2 pivot, float x, float y)
    {
        rt.anchorMin = anchor;
        rt.anchorMax = anchor;
        rt.pivot = pivot;
        rt.anchoredPosition = new Vector2(x, y);
    }

    public static void SetFill(SContainerOptions fill, float fraction)
    {
        var rt = fill.RectTransform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = new Vector2(Mathf.Clamp01(fraction), 1);
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    public static CanvasGroup Group(GameObject go)
    {
        var g = go.GetComponent<CanvasGroup>();
        if (g == null) g = go.AddComponent<CanvasGroup>();
        g.interactable = false;
        g.blocksRaycasts = false;
        return g;
    }

    public static void NoRaycast(GameObject go)
    {
        foreach (var img in go.GetComponentsInChildren<Image>(true)) img.raycastTarget = false;
        foreach (var t in go.GetComponentsInChildren<TMP_Text>(true)) t.raycastTarget = false;
    }

    public static string Compact(double v)
    {
        var inv = CultureInfo.InvariantCulture;
        if (v >= 1e9) return (v / 1e9).ToString("0.#", inv) + "B";
        if (v >= 1e6) return (v / 1e6).ToString("0.#", inv) + "M";
        if (v >= 1e4) return (v / 1e3).ToString("0.#", inv) + "k";
        return Math.Round(v, MidpointRounding.AwayFromZero).ToString("0", inv);
    }

    public static string Grouped(double v) => Math.Round(v, MidpointRounding.AwayFromZero).ToString("N0", CultureInfo.InvariantCulture);

    public static string TierHex(EnemyTier t) => t switch
    {
        EnemyTier.Elite => "#FFD94A",
        EnemyTier.Miniboss => "#B86BFF",
        EnemyTier.Boss => "#FF5A5A",
        _ => UiTheme.Text,
    };

    public static string TierBarHex(EnemyTier t) => t switch
    {
        EnemyTier.Elite => "#E0A800",
        EnemyTier.Miniboss => "#9B4DDB",
        EnemyTier.Boss => "#E02B2B",
        _ => "#C0392B",
    };
}
