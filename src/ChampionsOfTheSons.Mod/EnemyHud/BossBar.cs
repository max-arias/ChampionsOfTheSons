using ChampionsOfTheSons.Mod.Bridge;
using ChampionsOfTheSons.Mod.Hud;
using ChampionsOfTheSons.Mod.UI;
using SUI;
using TMPro;
using UnityEngine;
using static SUI.SUI;

namespace ChampionsOfTheSons.Mod.EnemyHud;

/// <summary>
/// One wide boss health bar at the top centre (COS-67): name on the left, kind and level on the right, a long bar with a
/// pale "damage taken" chunk that drains after the hit, and the affixes in a small line under it. Bosses only; the bar spans
/// <see cref="ScreenShare"/> of the screen width and draws over nameplates and other bars (Max 2026-11-21).
/// </summary>
internal sealed class BossBar
{
    internal const float MaxWidth = 640, Height = 64;
    /// <summary>The bar's share of the screen width (Max 2026-11-21: 80%).</summary>
    internal const float ScreenShare = 0.8f;
    private readonly SContainerOptions _frame, _back;
    private float _width = MaxWidth;
    private const float TrailHold = 0.6f, TrailDrain = 0.45f;

    private readonly SContainerOptions _root, _fill, _trail;
    private readonly SLabelOptions _name, _sub, _affixes, _more;
    private readonly RectTransform _rt;
    private readonly CanvasGroup _group;
    private float _shownFrac = -1, _trailFrac = 1, _trailWait, _alpha;

    public EnemyRuntime? Target { get; private set; }
    public string NameText { get; private set; } = "";
    public float Fraction => _shownFrac;
    public bool Visible => Target != null && _root.Root.activeSelf;

    public BossBar(SContainerOptions layer)
    {
        _root = SContainer.TopLeft(0, 0).Size(MaxWidth, Height);
        _name = HudKit.Label("", 21, "#E8DCC8", TextAlignmentOptions.BottomLeft).TopLeft(2, 0).Size(MaxWidth * 0.62f, 28);
        _name.TextObject.enableWordWrapping = false;
        _name.TextObject.overflowMode = TextOverflowModes.Ellipsis;
        _sub = HudKit.Label("", 14, "#9AA3AF", TextAlignmentOptions.BottomRight).TopLeft(MaxWidth * 0.62f, 0).Size(MaxWidth * 0.38f - 2, 26);
        _sub.TextObject.enableWordWrapping = false;
        _sub.TextObject.overflowMode = TextOverflowModes.Ellipsis;
        var frame = _frame = SContainer.TopLeft(0, 30).Size(MaxWidth, 14).Background(HudKit.C("#0A0D12D8"), EBackground.None);
        var back = _back = SContainer.TopLeft(2, 2).Size(MaxWidth - 4, 10).Background(HudKit.C("#2A2622"), EBackground.None);
        _trail = SContainer.Background(HudKit.C("#D9CFB8C0"), EBackground.None);
        _fill = SContainer.Background(HudKit.C("#B8322A"), EBackground.None);
        back.Add(_trail);
        back.Add(_fill);
        frame.Add(back);
        _affixes = HudKit.Label("", 13, "#B9B2A3", TextAlignmentOptions.Center).TopLeft(0, 46).Size(MaxWidth, 18);
        _affixes.TextObject.enableWordWrapping = false;
        _affixes.TextObject.overflowMode = TextOverflowModes.Ellipsis;
        _root.Add(_name);
        _root.Add(_sub);
        _root.Add(frame);
        _root.Add(_affixes);
        // "+1 boss": other engaged bosses that get no bar of their own (Max 2026-11-21: one bar only).
        _more = HudKit.Label("", 14, "#E8DCC8", TextAlignmentOptions.Right).TopLeft(MaxWidth * 0.7f, 46).Size(MaxWidth * 0.3f - 2, 18);
        _more.TextObject.enableWordWrapping = false;
        _root.Add(_more);
        layer.Add(_root);
        _rt = _root.RectTransform;
        _group = HudKit.Group(_root.Root);
        HudKit.NoRaycast(_root.Root);
        _root.Active(false);
    }

    /// <summary>Canvas rectangle of the bar (y up), for the self-test.</summary>
    public Rect Bounds
    {
        get
        {
            float k = HudKit.CanvasPerPixel, cw = Screen.width * k, ch = Screen.height * k;
            var p = _rt.anchoredPosition;
            return new Rect(cw / 2 - _width / 2, ch + p.y - Height, _width, Height);
        }
    }

    public void Show(EnemyRuntime r, int slot)
    {
        float k = HudKit.CanvasPerPixel, cw = Screen.width * k;
        Layout(cw * ScreenShare);
        HudKit.Place(_rt, new Vector2(0.5f, 1), new Vector2(0.5f, 1), 0, -(18 + slot * (Height + 8)));
        if (ReferenceEquals(r, Target)) return;
        Target = r;
        var p = r.Profile;
        string kind = HudText.KindTitle(p);
        string? personal = HudText.PersonalName(p);
        NameText = personal ?? kind;
        _name.Text(NameText);
        _sub.Text(personal != null ? $"{kind}  {HudText.Level} {p.Level}" : $"{HudText.Level} {p.Level}");
        _affixes.Text(HudText.Affixes(p.Abilities));
        _shownFrac = -1;
        _trailFrac = 1;
        _trailWait = 0;
        _alpha = 0;
        _root.Active(true);
    }

    /// <summary>Sizes the bar and its rows to <paramref name="width"/> canvas units.</summary>
    private void Layout(float width)
    {
        if (Mathf.Approximately(width, _width)) return;
        _width = width;
        static void Size(RectTransform rt, float w) => rt.sizeDelta = new Vector2(w, rt.sizeDelta.y);
        Size(_rt, width);
        Size(_name.RectTransform, width * 0.62f);
        Size(_sub.RectTransform, width * 0.38f - 2);
        _sub.RectTransform.anchoredPosition = new Vector2(width * 0.62f, _sub.RectTransform.anchoredPosition.y);
        Size(_frame.RectTransform, width);
        Size(_back.RectTransform, width - 4);
        Size(_affixes.RectTransform, width);
        Size(_more.RectTransform, width * 0.3f - 2);
        _more.RectTransform.anchoredPosition = new Vector2(width * 0.7f, _more.RectTransform.anchoredPosition.y);
    }

    /// <summary>Text of the "+N bosses" tag (self-test).</summary>
    public string MoreText { get; private set; } = "";

    /// <summary>Shows how many other engaged bosses have no bar.</summary>
    public void SetMore(int others)
    {
        string text = others <= 0 ? "" : others == 1 ? UiText.T("ui.enemyhud.one_more_boss", "+1 boss") : string.Format(UiText.T("ui.enemyhud.more_bosses", "+{0} bosses"), others);
        if (text == MoreText) return;
        MoreText = text;
        _more.Text(text);
    }

    public void Hide()
    {
        if (Target == null) return;
        Target = null;
        _root.Active(false);
    }

    public void Update(float dt)
    {
        var r = Target;
        if (r == null) return;
        var health = r.Stats == null ? null : EnemyRegistry.Health(r.Stats);
        float frac = health == null ? 1f : Mathf.Clamp01(health._currentValue / (float)Math.Max(1, r.Profile.MaxHealth));
        if (_shownFrac < 0) _trailFrac = frac;
        if (!Mathf.Approximately(frac, _shownFrac))
        {
            if (frac < _shownFrac) _trailWait = TrailHold;
            _shownFrac = frac;
            HudKit.SetFill(_fill, frac);
        }
        // The pale chunk shows the damage just taken, then drains down to the bar.
        if (_trailFrac < frac) _trailFrac = frac;
        if (_trailWait > 0) _trailWait -= dt;
        else if (_trailFrac > frac) _trailFrac = Mathf.Max(frac, _trailFrac - TrailDrain * dt);
        HudKit.SetFill(_trail, _trailFrac);
        _alpha = Mathf.Min(1, _alpha + dt / 0.3f);
        _group.alpha = _alpha;
    }

    /// <summary>The trailing chunk's width as a fraction of the bar (self-test).</summary>
    public float TrailFraction => _trailFrac;
}
