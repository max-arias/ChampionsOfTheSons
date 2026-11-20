using System.Text;
using ChampionsOfTheSons.Core.Rules;
using ChampionsOfTheSons.Mod.Bridge;
using ChampionsOfTheSons.Mod.UI;
using SUI;
using TMPro;
using UnityEngine;
using static SUI.SUI;

namespace ChampionsOfTheSons.Mod.Hud;

/// <summary>
/// The enemy scanner (COS-68): a dark card beside the nameplate of the enemy the player is looking at, with what the plate
/// leaves out: kind and tier, danger, health in numbers, armor and how much of it is stripped, extra damage per hit,
/// XP bounty and a line per affix saying what it does. The static part is written once per target; health and armor
/// refresh four times a second. Nothing is rebuilt per frame.
/// </summary>
internal sealed class ScanCard
{
    internal const float Width = 330;
    private const float Pad = 10;
    private const float Refresh = 0.25f;

    private SContainerOptions _root = null!;
    private SLabelOptions _head = null!, _stats = null!, _affixes = null!;
    private RectTransform _rt = null!;
    private CanvasGroup _group = null!;
    private EnemyRuntime? _target;
    private float _nextRefresh, _alpha;
    private float _height = 120;
    private string _staticStats = "";
    private float _headH, _statsH, _affixH;

    public bool Visible => _target != null && _root.Root.activeSelf;
    public EnemyRuntime? Target => _target;
    public string HeadText { get; private set; } = "";
    public string StatsText { get; private set; } = "";
    public string AffixText { get; private set; } = "";
    public Rect Bounds { get; private set; }

    public void Build(SContainerOptions layer)
    {
        _root = SContainer.TopLeft(0, 0).Size(Width, _height).Background(HudKit.C("#0A0D12E0"), EBackground.Round10);
        _head = HudKit.Label("", 17, UiTheme.Text, TextAlignmentOptions.TopLeft, wrap: true).TopLeft(Pad, 8).Size(Width - 2 * Pad, 44);
        _stats = HudKit.Label("", 15, UiTheme.Text, TextAlignmentOptions.TopLeft, wrap: true).TopLeft(Pad, 52).Size(Width - 2 * Pad, 80);
        _affixes = HudKit.Label("", 14, UiTheme.TextDim, TextAlignmentOptions.TopLeft, wrap: true).TopLeft(Pad, 132).Size(Width - 2 * Pad, 100);
        _root.Add(_head);
        _root.Add(_stats);
        _root.Add(_affixes);
        layer.Add(_root);
        _rt = _root.RectTransform;
        _group = HudKit.Group(_root.Root);
        HudKit.NoRaycast(_root.Root);
        HudKit.Place(_rt, Vector2.zero, new Vector2(0, 1), 0, 0);
        _root.Active(false);
    }

    public void Hide()
    {
        _target = null;
        _alpha = 0;
        _root.Active(false);
    }

    /// <summary>Shows the card for <paramref name="target"/> beside <paramref name="plate"/> (canvas units, y up), or hides it for null.</summary>
    public void Update(EnemyRuntime? target, Rect plate, float dt)
    {
        if (target == null) { if (_target != null) Hide(); return; }
        if (!ReferenceEquals(target, _target))
        {
            _target = target;
            Write(target);
            _alpha = 0;
            _nextRefresh = 0;
            _root.Active(true);
        }
        _nextRefresh -= dt;
        if (_nextRefresh <= 0)
        {
            _nextRefresh = Refresh;
            WriteLive(target);
        }
        _alpha = Mathf.Min(1, _alpha + dt / 0.15f);
        _group.alpha = _alpha;

        // Beside the plate, on whichever side has room; the plate itself never moves for it.
        float k = HudKit.CanvasPerPixel, cw = Screen.width * k, ch = Screen.height * k;
        float x = plate.xMax + 8;
        if (x + Width > cw - 8) x = plate.xMin - 8 - Width;
        x = Mathf.Clamp(x, 8, Mathf.Max(8, cw - Width - 8));
        float y = Mathf.Clamp(plate.yMax, _height + 8, ch - 8);
        _rt.anchoredPosition = new Vector2(x, y);
        Bounds = new Rect(x, y - _height, Width, _height);
    }

    private void Write(EnemyRuntime r)
    {
        var p = r.Profile;
        var danger = EnemyInfo.Danger(p.Tier, p.Level, PlayerState.Character.Progress.Level);
        string title = HudText.KindTitle(p);
        string? name = HudText.PersonalName(p);
        var sb = new StringBuilder();
        if (name != null) sb.Append("<color=").Append(HudKit.TierHex(p.Tier)).Append('>').Append(name).Append("</color>\n");
        sb.Append("<size=14><color=#9AA3AF>").Append(title).Append("  ").Append(HudText.Level).Append(' ').Append(p.Level)
            .Append("</color>  <color=").Append(HudText.DangerHex(danger)).Append('>').Append(HudText.Danger(danger)).Append("</color></size>");
        HeadText = sb.ToString();
        _head.Text(HeadText);

        sb.Clear();
        sb.Append(Row(UiText.T("ui.scanner.damage", "Extra per hit"), "+" + HudKit.Compact(EnemyInfo.ExtraHitDamage(p.DamageMult)))).Append('\n');
        sb.Append(Row(UiText.T("ui.scanner.bounty", "Bounty"), string.Format(UiText.T("ui.scanner.xp", "{0} XP"), HudKit.Compact(p.Bounty))));
        _staticStats = sb.ToString();

        sb.Clear();
        for (int i = 0; i < p.Abilities.Count; i++)
        {
            var a = p.Abilities[i];
            if (i > 0) sb.Append('\n');
            sb.Append("<color=").Append(HudKit.TierHex(p.Tier)).Append('>').Append(HudText.Ability(a)).Append("</color>");
            string why = HudText.AffixExplained(a);
            if (why.Length > 0) sb.Append("  ").Append(why);
        }
        AffixText = sb.ToString();
        _affixes.Text(AffixText);
        _affixes.Active(AffixText.Length > 0);

        _headH = Measure(_head, HeadText);
        _affixH = AffixText.Length > 0 ? Measure(_affixes, AffixText) : 0;
    }

    private void WriteLive(EnemyRuntime r)
    {
        var p = r.Profile;
        var health = r.Stats == null ? null : EnemyRegistry.Health(r.Stats);
        double hp = health?._currentValue ?? p.MaxHealth;
        double armor = Math.Max(0, p.Armor - r.ArmorReduction);
        string armorText = HudKit.Compact(armor);
        if (r.ArmorReduction >= 1)
            armorText += "  <color=#9AA3AF>(" + string.Format(UiText.T("ui.scanner.armor_stripped", "{0} stripped"), HudKit.Compact(Math.Min(p.Armor, r.ArmorReduction))) + ")</color>";
        StatsText = Row(UiText.T("ui.scanner.health", "Health"), HudKit.Compact(Math.Max(0, hp)) + " / " + HudKit.Compact(p.MaxHealth)) + "\n"
            + Row(UiText.T("ui.scanner.armor", "Armor"), armorText) + "\n" + _staticStats;
        _stats.Text(StatsText);
        if (_statsH <= 0 || !ReferenceEquals(r, _measuredFor))
        {
            _measuredFor = r;
            _statsH = Measure(_stats, StatsText);
            Layout();
        }
    }

    private EnemyRuntime? _measuredFor;

    private void Layout()
    {
        float y = 8;
        Set(_head, y, _headH);
        y += _headH + 6;
        Set(_stats, y, _statsH);
        y += _statsH;
        if (_affixH > 0)
        {
            y += 6;
            Set(_affixes, y, _affixH);
            y += _affixH;
        }
        _height = y + 10;
        _rt.sizeDelta = new Vector2(Width, _height);
    }

    private static void Set(SLabelOptions l, float y, float h)
    {
        var rt = l.RectTransform;
        rt.anchoredPosition = new Vector2(rt.anchoredPosition.x, -y);
        rt.sizeDelta = new Vector2(Width - 2 * Pad, h);
    }

    private static float Measure(SLabelOptions l, string text) =>
        Mathf.Ceil(l.TextObject.GetPreferredValues(text, Width - 2 * Pad, 0).y) + 2;

    private static string Row(string label, string value) => "<color=#9AA3AF>" + label + "</color>  " + value;
}
