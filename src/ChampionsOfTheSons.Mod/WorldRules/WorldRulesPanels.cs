using ChampionsOfTheSons.Core;
using ChampionsOfTheSons.Core.Rules;
using ChampionsOfTheSons.Mod.UI;
using SUI;
using TMPro;
using UnityEngine;
using static SUI.SUI;

namespace ChampionsOfTheSons.Mod.HostRules;

/// <summary>Base for the modal panels: a dimmed full-screen root with a centered window and a title.</summary>
internal abstract class ModalPanel
{
    protected SPanelOptions Root = null!;
    protected SContainerOptions Window = null!;

    protected static Color C(string hex) => ColorFromString(hex);

    public bool IsOpen => Root != null && Root.Root.activeSelf;
    public GameObject? PadRoot => Root?.Root;

    protected void BuildShell(string id, float w, float h, string title)
    {
        Root = RegisterNewPanel(id, enableInput: true);
        Root.Add(SContainer.Anchor(AnchorType.Fill).Background(C(UiTheme.Dim), EBackground.None));
        Window = SContainer.Anchor(AnchorType.MiddleCenter).Pivot(0.5f, 0.5f).Size(w, h).Background(C(UiTheme.Window), EBackground.Round10);
        Root.Add(Window);
        Window.Add(Label(title, 30, UiTheme.Gold, TextAlignmentOptions.MidlineLeft).TopLeft(28, 14).Size(w - 120, 44));
        Root.Active(false);
    }

    protected static SLabelOptions Label(string text, int size, string hex, TextAlignmentOptions align)
    {
        var l = SLabel.Text(text).FontSize(size).FontColor(hex).Alignment(align).Wrap(true);
        l.TextObject.raycastTarget = false;
        l.TextObject.richText = true;
        return l;
    }

    protected static SBgButtonOptions Button(string text, int size, Action click, string normal = "#2B333E", string hover = "#3C4755")
    {
        var b = SBgButton.Text(text).FontSize(size).FontColor(UiTheme.Text).Color(C(normal), C(hover)).Notify(() => { Audio.ModAudio.Play(Audio.SoundCues.UiClick, 0.6f); click(); });
        b.TextObject.enableWordWrapping = false;
        return b;
    }

    public void Open()
    {
        Root.Active(true);
        Refresh();
    }

    public void Close() => Root.Active(false);

    protected abstract void Refresh();
}

/// <summary>Host settings: the game's difficulty (read only, COS-130), the multipliers, elites on/off and which abilities may roll. Edits apply at once and are saved with the world.</summary>
internal sealed class HostSettingsPanel : ModalPanel
{
    public const string PanelId = "cots-host-settings";
    private const float W = 1180, H = 860;

    private sealed record Row(string Label, double Step, double Min, double Max, Func<WorldRules, double> Get, Func<WorldRules, double, WorldRules> Set, bool Integer = false);

    private static readonly Row[] Rows =
    {
        new("ui.worldrules.row.drop_quantity", 0.25, 0, 10, r => r.DropQuantity, (r, v) => r with { DropQuantity = v }),
        new("ui.worldrules.row.drop_chance", 0.25, 0, 10, r => r.DropChance, (r, v) => r with { DropChance = v }),
        new("ui.worldrules.row.xp", 0.25, 0, 10, r => r.Xp, (r, v) => r with { Xp = v }),
        new("ui.worldrules.row.extra_levels", 1, 0, 100, r => r.ExtraEnemyLevels, (r, v) => r with { ExtraEnemyLevels = (int)v }, true),
        new("ui.worldrules.row.enemy_damage", 0.25, 0, 10, r => r.EnemyDamage, (r, v) => r with { EnemyDamage = v }),
        new("ui.worldrules.row.enemy_health", 0.25, 0.25, 10, r => r.EnemyHealth, (r, v) => r with { EnemyHealth = v }),
        new("ui.worldrules.row.enemy_armor", 0.25, 0, 10, r => r.EnemyArmor, (r, v) => r with { EnemyArmor = v }),
        new("ui.worldrules.row.enemy_speed", 0.25, 0.25, 4, r => r.EnemySpeed, (r, v) => r with { EnemySpeed = v }),
    };

    private readonly List<SLabelOptions> _values = new();
    private readonly Dictionary<EnemyAbility, SBgButtonOptions> _abilityButtons = new();
    private SBgButtonOptions _elites = null!, _friendlyFire = null!, _lootLevel = null!, _minRarity = null!;
    private SLabelOptions _difficulty = null!;

    public void Build()
    {
        BuildShell(PanelId, W, H, UiText.T("ui.worldrules.host.title", "Host settings"));
        Window.Add(Button("X", 24, Close, "#2B333E", "#4A3036").TopLeft(W - 64, 16).Size(40, 40));

        // the game's difficulty: read from the game, not editable here (COS-130)
        Window.Add(Label(UiText.T("ui.worldrules.row.game_difficulty", "Game difficulty"), 20, UiTheme.Text, TextAlignmentOptions.MidlineLeft).TopLeft(28, 76).Size(300, 36));
        _difficulty = Label("", 18, UiTheme.Gold, TextAlignmentOptions.MidlineLeft);
        Window.Add(_difficulty.TopLeft(330, 76).Size(290, 36));

        // multipliers
        for (int i = 0; i < Rows.Length; i++)
        {
            var row = Rows[i];
            float y = 124 + i * 44;
            Window.Add(Label(UiText.T(row.Label, row.Label), 20, UiTheme.Text, TextAlignmentOptions.MidlineLeft).TopLeft(28, y).Size(300, 36));
            Window.Add(Button("-", 22, () => Nudge(row, -1)).TopLeft(330, y).Size(40, 36));
            var value = Label("", 20, UiTheme.Stat, TextAlignmentOptions.Midline);
            Window.Add(value.TopLeft(374, y).Size(190, 36));
            _values.Add(value);
            Window.Add(Button("+", 22, () => Nudge(row, 1)).TopLeft(568, y).Size(40, 36));
        }

        float elitesY = 124 + Rows.Length * 44;
        Window.Add(Label(UiText.T("ui.worldrules.row.elites", "Elite enemies"), 20, UiTheme.Text, TextAlignmentOptions.MidlineLeft).TopLeft(28, elitesY).Size(300, 36));
        _elites = Button("", 20, ToggleElites);
        Window.Add(_elites.TopLeft(330, elitesY).Size(278, 36));
        Window.Add(Label(UiText.T("ui.worldrules.level_hint",
                "Enemy levels follow the level of the players near them. The game's difficulty adds levels and elites; extra levels add more on top."),
            17, UiTheme.TextDim, TextAlignmentOptions.TopLeft).TopLeft(28, elitesY + 56).Size(580, 90));

        // COS-77: friendly fire, loot level, minimum loot rarity, reset
        float y2 = elitesY + 150;
        _friendlyFire = ToggleRow(y2, "ui.worldrules.row.friendly_fire", "Friendly fire", ToggleFriendlyFire);
        _lootLevel = ToggleRow(y2 + 44, "ui.worldrules.row.loot_level", "Loot level", CycleLootLevel);
        _minRarity = ToggleRow(y2 + 88, "ui.worldrules.row.min_rarity", "Lowest drop rarity", CycleMinRarity);
        Window.Add(Button(UiText.T("ui.worldrules.reset", "Reset to defaults"), 18, ResetDefaults, "#3A2A20", "#4E382A").TopLeft(28, y2 + 140).Size(580, 36));

        // abilities
        Window.Add(Label(UiText.T("ui.worldrules.abilities.title", "Enemy abilities (click to switch off or on)"), 20, UiTheme.Gold, TextAlignmentOptions.MidlineLeft)
            .TopLeft(640, 76).Size(W - 668, 36));
        var all = (EnemyAbility[])Enum.GetValues(typeof(EnemyAbility));
        const float cw = 168, ch = 36, gx = 8, gy = 8;
        for (int i = 0; i < all.Length; i++)
        {
            var ability = all[i];
            var btn = Button(AbilityName(ability), 15, () => ToggleAbility(ability));
            btn.TopLeft(640 + i % 3 * (cw + gx), 124 + i / 3 * (ch + gy)).Size(cw, ch);
            Window.Add(btn);
            _abilityButtons[ability] = btn;
        }
    }

    /// <summary>"Hard: +5 levels, 15% elites".</summary>
    public static string GameDifficultyText(GameDifficulty game)
    {
        string mode = UiText.T("ui.worldrules.mode." + ModeKey(game.Mode), Spaced(game.Mode.ToString()));
        string levels = (game.LevelOffset >= 0 ? "+" : "") + game.LevelOffset;
        int elites = (int)Math.Round(game.EliteChance * 100);
        return string.Format(UiText.T("ui.worldrules.mode.format", "{0}: {1} levels, {2}% elites"), mode, levels, elites);
    }

    private static string ModeKey(GameMode mode) => mode switch
    {
        GameMode.HardSurvival => "hard_survival",
        _ => mode.ToString().ToLowerInvariant(),
    };

    public static string AbilityName(EnemyAbility a) => UiText.T("ui.worldrules.ability." + a.ToString().ToLowerInvariant(), Spaced(a.ToString()));

    private static string Spaced(string pascal)
    {
        var sb = new System.Text.StringBuilder();
        foreach (char c in pascal)
        {
            if (sb.Length > 0 && char.IsUpper(c)) sb.Append(' ');
            sb.Append(c);
        }
        return sb.ToString();
    }

    private void Nudge(Row row, int direction)
    {
        double v = row.Get(WorldState.Rules) + direction * row.Step;
        v = Math.Round(Math.Clamp(v, row.Min, row.Max), 2);
        WorldState.Update(r => row.Set(r, v));
        Changed();
    }

    private SBgButtonOptions ToggleRow(float y, string key, string fallback, Action onClick)
    {
        Window.Add(Label(UiText.T(key, fallback), 20, UiTheme.Text, TextAlignmentOptions.MidlineLeft).TopLeft(28, y).Size(300, 36));
        var btn = Button("", 18, onClick);
        Window.Add(btn.TopLeft(330, y).Size(278, 36));
        return btn;
    }

    private void ToggleFriendlyFire()
    {
        WorldState.SetFriendlyFire(!WorldState.FriendlyFire);
        FriendlyFireRule.Apply(WorldState.FriendlyFire);
        Changed();
    }

    private void CycleLootLevel()
    {
        var all = Enum.GetValues<LootLevelPolicy>();
        WorldState.Update(r => r with { LootLevel = all[(Array.IndexOf(all, r.LootLevel) + 1) % all.Length] });
        Changed();
    }

    private void CycleMinRarity()
    {
        WorldState.Update(r => r with { MinLootRarity = (r.MinLootRarity + 1) % ((int)Rarity.Legendary + 1) });
        Changed();
    }

    /// <summary>Every host setting back to its default (the game's difficulty is the game's and stays).</summary>
    public void ResetDefaults()
    {
        WorldState.Update(r => new WorldRules { GameDifficulty = r.GameDifficulty });
        WorldState.SetFriendlyFire(false);
        FriendlyFireRule.Apply(false);
        Changed();
    }

    public static string LootLevelName(LootLevelPolicy p) => UiText.T("ui.worldrules.loot_level." + p.ToString().ToLowerInvariant(), p.ToString());

    private void ToggleElites()
    {
        WorldState.Update(r => r with { AllowElites = !r.AllowElites });
        Changed();
    }

    public void ToggleAbility(EnemyAbility a)
    {
        WorldState.Update(r =>
        {
            var set = r.DisabledAbilities.ToHashSet();
            if (!set.Remove(a)) set.Add(a);
            return r with { DisabledAbilities = set.OrderBy(x => x).ToArray() };
        });
        Changed();
    }

    private void Changed()
    {
        WorldState.NotifyChanged();
        Refresh();
    }

    protected override void Refresh()
    {
        var rules = WorldState.Rules;
        _difficulty.Text(GameDifficultyText(rules.GameDifficulty));
        for (int i = 0; i < Rows.Length; i++)
        {
            double v = Rows[i].Get(rules);
            _values[i].Text(Rows[i].Integer ? $"+{v:0}" : $"x{v:0.##}");
        }
        _elites.Text(rules.AllowElites ? UiText.T("ui.worldrules.on", "On") : UiText.T("ui.worldrules.off", "Off"));
        _elites.Color(C(rules.AllowElites ? "#2F5A3A" : "#5A2F36"), C(rules.AllowElites ? "#3C7048" : "#703A43"));
        bool ff = WorldState.FriendlyFire;
        _friendlyFire.Text(ff ? UiText.T("ui.worldrules.on", "On") : UiText.T("ui.worldrules.off", "Off"));
        _friendlyFire.Color(C(ff ? "#5A2F36" : "#2B333E"), C(ff ? "#703A43" : "#3C4755"));
        _lootLevel.Text(LootLevelName(rules.LootLevel));
        _minRarity.Text(UiText.T("ui.rarity." + ((Rarity)rules.MinLootRarity).ToString().ToLowerInvariant(), ((Rarity)rules.MinLootRarity).ToString()));
        foreach (var (ability, btn) in _abilityButtons)
        {
            bool off = rules.DisabledAbilities.Contains(ability);
            btn.FontColor(off ? UiTheme.TextFaint : UiTheme.Text);
            btn.Color(C(off ? "#3A2024" : "#2B333E"), C(off ? "#4A2A2F" : "#3C4755"));
        }
    }
}
