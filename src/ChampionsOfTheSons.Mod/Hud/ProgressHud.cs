using ChampionsOfTheSons.Core.Rules;
using ChampionsOfTheSons.Mod.UI;
using SUI;
using TMPro;
using UnityEngine;
using static SUI.SUI;

namespace ChampionsOfTheSons.Mod.Hud;

/// <summary>A centered banner that pops in, holds and fades out. Re-showing restarts it.</summary>
internal sealed class Banner
{
    private readonly SContainerOptions _root;
    private readonly SLabelOptions _title, _body;
    private readonly CanvasGroup _group;
    private readonly RectTransform _rt;
    private float _age = -1, _life;

    public Banner(SContainerOptions layer, float y, int titleSize, string titleColor, int bodySize, float height)
    {
        _root = SContainer.TopLeft(0, 0).Size(620, height).Background(HudKit.C("#0A0D12B0"), EBackground.Round10);
        _title = HudKit.Label("", titleSize, titleColor, TextAlignmentOptions.Center).TopLeft(10, 8).Size(600, titleSize + 12);
        _body = HudKit.Label("", bodySize, UiTheme.Text, TextAlignmentOptions.Top, wrap: true)
            .TopLeft(10, titleSize + 20).Size(600, height - titleSize - 28);
        _root.Add(_title);
        _root.Add(_body);
        layer.Add(_root);
        _rt = _root.RectTransform;
        _group = HudKit.Group(_root.Root);
        HudKit.NoRaycast(_root.Root);
        HudKit.Place(_rt, new Vector2(0.5f, 1), new Vector2(0.5f, 1), 0, -y);
        _root.Active(false);
    }

    public bool Visible => _age >= 0;
    public string Title { get; private set; } = "";
    public string Body { get; private set; } = "";

    public void Show(string title, string body, float seconds)
    {
        Title = title;
        Body = body;
        _title.Text(title);
        _body.Text(body);
        _age = 0;
        _life = seconds;
        _group.alpha = 0;
        _root.Active(true);
    }

    public void Hide()
    {
        _age = -1;
        _root.Active(false);
    }

    public void Update(float dt)
    {
        if (_age < 0) return;
        _age += dt;
        if (_age >= _life) { Hide(); return; }
        float fadeIn = Mathf.Clamp01(_age / 0.25f);
        float fadeOut = Mathf.Clamp01((_life - _age) / 0.8f);
        _group.alpha = Mathf.Min(fadeIn, fadeOut);
        float s = 1f + 0.25f * (1f - fadeIn);
        _rt.localScale = new Vector3(s, s, 1);
    }
}

/// <summary>The XP bar along the bottom edge, the level-up banner and the massacre counter and payout banner.</summary>
internal sealed class ProgressHud
{
    // Same width as the action bar's slot groups, centered under it; the level disc sits on the strap's left end (COS-151).
    private const float BarW = ActionBar.ActionBar.InnerWidth;
    private const float XpBottom = 6;

    private static PlayerCharacter Pc => PlayerState.Character;

    // xp bar
    private SContainerOptions _xpRoot = null!, _xpFill = null!;
    private SLabelOptions _xpLevel = null!, _xpText = null!;
    private int _shownLevel = -1;
    private long _shownXp = -1;
    private float _shownFraction;
    private float _targetFraction;

    // massacre counter
    private SContainerOptions _mRoot = null!, _mFill = null!;
    private SLabelOptions _mHead = null!, _mKills = null!, _mInfo = null!;
    private CanvasGroup _mGroup = null!;
    private int _mKillsShown = -1;
    private long _mBankShown = -1;
    private float _mFraction = -1;

    private Banner _levelBanner = null!, _payoutBanner = null!;

    // pending level-up while one is showing
    private int _pendingOld, _pendingNew;
    private readonly List<string> _pendingRewards = new();
    private float _pendingTimer = -1;

    public bool MassacreVisible => _mRoot.Root.activeSelf;
    public string MassacreText => _mKills.TextObject.text + " | " + _mInfo.TextObject.text;
    public string MassacreTitle => _mHead.TextObject.text;
    public string XpText => _xpLevel.TextObject.text + " | " + _xpText.TextObject.text;
    public Banner LevelBanner => _levelBanner;
    public Banner PayoutBanner => _payoutBanner;

    /// <summary>Self-test only: raise the XP bar so a pause-menu overlay does not cover it in screenshots.</summary>
    public void LiftXpBar(float y) => HudKit.Place(_xpRoot.RectTransform, new Vector2(0.5f, 0), new Vector2(0.5f, 0), 0, y);

    public void Build(SContainerOptions layer)
    {
        BuildXpBar(layer);
        BuildMassacre(layer);
        _levelBanner = new Banner(layer, 150, 52, UiTheme.Gold, 24, 190);
        _payoutBanner = new Banner(layer, 360, 36, "#FF8A4A", 26, 120);
    }

    private void BuildXpBar(SContainerOptions layer)
    {
        // COS-151: a stitched leather strap under the action bar with a carved bone disc for the level at its left end.
        const float h = 24, scale = h / 40f, disc = 54;
        _xpRoot = SContainer.TopLeft(0, 0).Size(BarW, h);
        var strap = ActionBar.HudArt.Piece(ActionBar.HudArt.BarStrap, scale, "#2A1B12E0").Anchor(AnchorType.Fill);
        _xpRoot.Add(strap);
        // The recess inside the strap (tools/hud_art.py bar_strap) holds the fill.
        var recess = SContainer.TopLeft(18 * scale + disc / 2, 12 * scale).Size(BarW - 36 * scale - disc / 2, h - 24 * scale);
        strap.Add(recess);
        _xpFill = ActionBar.HudArt.Piece(ActionBar.HudArt.BarFill, 0.5f, UiTheme.XpFill, UiTheme.XpFill);
        recess.Add(_xpFill);
        _xpText = HudKit.Label("", 12, UiTheme.Bone, TextAlignmentOptions.Center).Anchor(AnchorType.Fill);
        _xpText.TextObject.fontStyle = FontStyles.Bold;
        ActionBar.BarSlot.Outline(_xpText, 0.3f);
        strap.Add(_xpText);
        var levelBack = ActionBar.HudArt.Piece(ActionBar.HudArt.LevelDisc, disc / 128f, "#0A0D12D0").TopLeft(-disc / 2, (h - disc) / 2).Size(disc, disc);
        _xpLevel = HudKit.Label("", 18, UiTheme.Gold, TextAlignmentOptions.Center).Anchor(AnchorType.Fill);
        _xpLevel.TextObject.fontStyle = FontStyles.Bold;
        ActionBar.BarSlot.Outline(_xpLevel, 0.25f);
        levelBack.Add(_xpLevel);
        _xpRoot.Add(levelBack);
        layer.Add(_xpRoot);
        HudKit.NoRaycast(_xpRoot.Root);
        HudKit.Place(_xpRoot.RectTransform, new Vector2(0.5f, 0), new Vector2(0.5f, 0), 0, XpBottom);
        HudKit.SetFill(_xpFill, 0);
    }

    private void BuildMassacre(SContainerOptions layer)
    {
        _mRoot = SContainer.TopLeft(0, 0).Size(300, 150).Background(HudKit.C("#0A0D12B0"), EBackground.Round10);
        _mHead = HudKit.Label("", 17, "#FF6B4A", TextAlignmentOptions.MidlineRight).UpperCase().TopLeft(12, 8).Size(276, 24);
        _mKills = HudKit.Label("", 44, UiTheme.Text, TextAlignmentOptions.MidlineRight).TopLeft(12, 30).Size(276, 56);
        _mInfo = HudKit.Label("", 20, UiTheme.Gold, TextAlignmentOptions.MidlineRight).TopLeft(12, 88).Size(276, 26);
        var back = SContainer.TopLeft(12, 124).Size(276, 10).Background(HudKit.C(UiTheme.BarBack), EBackground.None);
        _mFill = SContainer.Background(HudKit.C("#FF6B4A"), EBackground.None);
        back.Add(_mFill);
        _mRoot.Add(_mHead);
        _mRoot.Add(_mKills);
        _mRoot.Add(_mInfo);
        _mRoot.Add(back);
        layer.Add(_mRoot);
        _mGroup = HudKit.Group(_mRoot.Root);
        HudKit.NoRaycast(_mRoot.Root);
        HudKit.Place(_mRoot.RectTransform, new Vector2(1, 0.5f), new Vector2(1, 0.5f), -24, 120);
        _mHead.Text(UiText.T("ui.hud.massacre", "Massacre"));
        _mRoot.Active(false);
    }

    // ------------------------------------------------------------------ events

    public void OnLeveledUp(LevelUpResult r)
    {
        if (_pendingTimer < 0)
        {
            _pendingOld = r.OldLevel;
            _pendingRewards.Clear();
        }
        _pendingNew = r.NewLevel;
        _pendingRewards.AddRange(r.RewardItemIds);
        _pendingTimer = 0; // shown on the next Update (Unity objects are only touched from there)
    }

    /// <summary>The streak's title by kill count (COS-95), our own words.</summary>
    public static string StreakTitle(int kills)
    {
        int tier = Massacre.TitleTier(kills);
        return tier == 0 ? UiText.T("ui.hud.massacre", "Massacre") : UiText.T($"ui.hud.streak.{tier}", StreakDefaults[tier]);
    }

    private static readonly string[] StreakDefaults =
        { "", "Bloodlust", "Savagery", "Rampage", "Carnage", "Slaughter", "Butchery", "Massacre", "Annihilation" };

    public void OnPaid(int kills, double banked, double mult, double payout)
    {
        string title = $"{StreakTitle(kills)}!";
        string body = $"{kills} {UiText.T("ui.hud.kills", "kills")}  ×{mult.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)}\n" +
                      $"<color={UiTheme.Gold}>+{HudKit.Grouped(banked * mult)} XP</color>"; // the streak's whole worth (COS-95)
        _payoutBanner.Show(title, body, 4f);
    }

    /// <summary>COS-76: the death XP penalty, on the payout banner.</summary>
    public void OnDied(double xpLost)
    {
        if (xpLost <= 0) return;
        _payoutBanner.Show(UiText.T("ui.death.title", "You died"), $"<color={UiTheme.Bad}>-{HudKit.Grouped(xpLost)} XP</color>", 4f);
    }

    private void ShowLevelUp()
    {
        var sb = new System.Text.StringBuilder();
        string lv = UiText.T("ui.hud.level_long", "Level");
        sb.Append($"{lv} {_pendingOld} → <color={UiTheme.Gold}><b>{_pendingNew}</b></color>");
        int points = _pendingNew - _pendingOld;
        sb.Append($"\n<color={UiTheme.Good}>+{points} {UiText.T(points == 1 ? "ui.hud.perk_point" : "ui.hud.perk_points", points == 1 ? "perk point" : "perk points")}</color>");
        foreach (var id in _pendingRewards)
            sb.Append($"\n<color={UiTheme.Unique}>{UiText.T("ui.hud.reward", "Reward")}: {HudText.ItemName(id)}</color>");
        _levelBanner.Show(UiText.T("ui.hud.level_up", "LEVEL UP!"), sb.ToString(), 5f + 0.8f * _pendingRewards.Count);
        _pendingTimer = -1;
        _pendingRewards.Clear();
    }

    // ------------------------------------------------------------------ per frame

    public void Update(float dt, bool inWorld)
    {
        _xpRoot.Active(inWorld);
        if (!inWorld)
        {
            _mRoot.Active(false);
            return;
        }
        if (_pendingTimer >= 0) ShowLevelUp();
        _levelBanner.Update(dt);
        _payoutBanner.Update(dt);
        UpdateXp(dt);
        UpdateMassacre();
    }

    private void UpdateXp(float dt)
    {
        var p = Pc.Progress;
        double goal = XpCurve.Goal(p.Level);
        long xp = (long)p.Xp;
        if (p.Level != _shownLevel || xp != _shownXp)
        {
            if (p.Level != _shownLevel) _xpLevel.Text($"<size=9>{UiText.T("ui.hud.level", "Lv")}</size>\n{p.Level}");
            _xpText.Text($"{HudKit.Grouped(p.Xp)} / {HudKit.Grouped(goal)} XP");
            _targetFraction = (float)Math.Clamp(p.Xp / goal, 0, 1);
            // COS-95: a level-up sweeps the bar to full once per level gained (at most 5), then on to the new fraction.
            if (_shownLevel >= 0 && p.Level > _shownLevel) _sweeps = Math.Min(5, _sweeps + p.Level - _shownLevel);
            else if (p.Level != _shownLevel) { _sweeps = 0; _shownFraction = _targetFraction; }
            _shownLevel = p.Level;
            _shownXp = xp;
        }
        if (_sweeps > 0)
        {
            _shownFraction = Mathf.MoveTowards(_shownFraction, 1f, dt * 3f);
            if (_shownFraction >= 1f)
            {
                _sweeps--;
                SweepsDone++;
                _shownFraction = 0;
            }
            HudKit.SetFill(_xpFill, _shownFraction);
        }
        else if (!Mathf.Approximately(_shownFraction, _targetFraction))
        {
            _shownFraction = Mathf.MoveTowards(_shownFraction, _targetFraction, dt * 1.5f);
            HudKit.SetFill(_xpFill, _shownFraction);
        }
    }

    private int _sweeps;

    /// <summary>Full-bar sweeps shown since start (self-test).</summary>
    public int SweepsDone { get; private set; }

    private void UpdateMassacre()
    {
        var m = Pc.Massacre;
        if (!m.Active)
        {
            if (_mRoot.Root.activeSelf) _mRoot.Active(false);
            _mKillsShown = -1;
            return;
        }
        if (!_mRoot.Root.activeSelf) _mRoot.Active(true);
        double mult = Massacre.Multiplier(m.Kills);
        long bank = (long)(m.PendingXp * mult);
        if (m.Kills != _mKillsShown || bank != _mBankShown)
        {
            if (Massacre.TitleTier(m.Kills) != Massacre.TitleTier(Math.Max(0, _mKillsShown))) _mHead.Text(StreakTitle(m.Kills));
            _mKillsShown = m.Kills;
            _mBankShown = bank;
            _mKills.Text($"{m.Kills} <size=24>{UiText.T("ui.hud.kills", "kills")}</size>");
            _mInfo.Text($"×{mult.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)}   {HudKit.Grouped(bank)} XP");
        }
        double max = Math.Max(1, Pc.Stats.Get(StatIds.MaxMassacreTime));
        float f = (float)Math.Clamp(m.TimeLeft / max, 0, 1);
        if (!Mathf.Approximately(f, _mFraction))
        {
            _mFraction = f;
            HudKit.SetFill(_mFill, f);
        }
        _mGroup.alpha = m.TimeLeft < 2 ? 0.6f + 0.4f * Mathf.Abs(Mathf.Sin(Time.time * 6f)) : 1f;
    }
}
