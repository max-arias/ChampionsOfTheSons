using ChampionsOfTheSons.Core.Rules;
using ChampionsOfTheSons.Mod.UI;
using SUI;
using TMPro;
using UnityEngine;
using static SUI.SUI;

namespace ChampionsOfTheSons.Mod.HostRules;

/// <summary>Base for the two modal panels: a dimmed full-screen root with a centered window and a title.</summary>
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
        var b = SBgButton.Text(text).FontSize(size).FontColor(UiTheme.Text).Color(C(normal), C(hover)).Notify(click);
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

/// <summary>
/// Shown once for a world whose host has not picked a difficulty. Never opens by itself unless the player is controllable
/// (the original opened it over cutscenes and the ending): see <see cref="WorldRulesFeature.ShouldAutoOpen"/>.
/// </summary>
internal sealed class DifficultyPicker : ModalPanel
{
    public const string PanelId = "cots-difficulty-picker";
    private const float W = 980, H = 720;

    private readonly List<(DifficultyTier Tier, SBgButtonOptions Button)> _buttons = new();

    /// <summary>Raised with the tier the host chose.</summary>
    public event Action<DifficultyTier>? Chosen;

    public IReadOnlyList<(DifficultyTier Tier, SBgButtonOptions Button)> Buttons => _buttons;

    public void Build()
    {
        BuildShell(PanelId, W, H, UiText.T("ui.worldrules.picker.title", "Choose your difficulty"));
        Window.Add(Label(UiText.T("ui.worldrules.picker.hint", "This sets enemy levels for the whole world. The host can change it later in host settings."),
            18, UiTheme.TextDim, TextAlignmentOptions.TopLeft).TopLeft(28, 64).Size(W - 56, 52));

        const float cellW = 440, cellH = 66, gap = 14;
        for (int i = 0; i < Difficulties.All.Count; i++)
        {
            var tier = Difficulties.All[i];
            float x = 28 + i % 2 * (cellW + gap), y = 128 + i / 2 * (cellH + gap);
            string name = UiText.T("ui.worldrules.difficulty." + tier.Id.Substring("difficulty.".Length), tier.Name);
            string levels = tier.BaseLevelMin == tier.BaseLevelMax ? $"Lv {tier.BaseLevelMin}" : $"Lv {tier.BaseLevelMin}-{tier.BaseLevelMax}";
            var captured = tier;
            var btn = Button($"{name}   <color={UiTheme.TextDim}>{levels}</color>", 24, () => Choose(captured));
            btn.TopLeft(x, y).Size(cellW, cellH);
            Window.Add(btn);
            _buttons.Add((tier, btn));
        }
    }

    public void Choose(DifficultyTier tier)
    {
        Close();
        Chosen?.Invoke(tier);
    }

    protected override void Refresh() { }
}

/// <summary>Host settings: difficulty, the multipliers, elites on/off and which abilities may roll. Edits apply at once and are saved with the world.</summary>
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
    private SBgButtonOptions _elites = null!;
    private SLabelOptions _difficulty = null!;

    public void Build()
    {
        BuildShell(PanelId, W, H, UiText.T("ui.worldrules.host.title", "Host settings"));
        Window.Add(Button("X", 24, Close, "#2B333E", "#4A3036").TopLeft(W - 64, 16).Size(40, 40));

        // difficulty
        Window.Add(Label(UiText.T("ui.worldrules.row.difficulty", "Difficulty"), 20, UiTheme.Text, TextAlignmentOptions.MidlineLeft).TopLeft(28, 76).Size(300, 36));
        Window.Add(Button("<", 22, () => StepDifficulty(-1)).TopLeft(330, 76).Size(40, 36));
        _difficulty = Label("", 20, UiTheme.Gold, TextAlignmentOptions.Midline);
        Window.Add(_difficulty.TopLeft(374, 76).Size(190, 36));
        Window.Add(Button(">", 22, () => StepDifficulty(1)).TopLeft(568, 76).Size(40, 36));

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

    private void StepDifficulty(int delta)
    {
        int i = Math.Clamp(WorldState.Rules.Difficulty.Index + delta, 0, Difficulties.All.Count - 1);
        WorldState.SetDifficulty(Difficulties.All[i]);
        Refresh();
    }

    private void Nudge(Row row, int direction)
    {
        double v = row.Get(WorldState.Rules) + direction * row.Step;
        v = Math.Round(Math.Clamp(v, row.Min, row.Max), 2);
        WorldState.Update(r => row.Set(r, v));
        Changed();
    }

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
        _difficulty.Text(WorldState.DifficultyChosen
            ? UiText.T("ui.worldrules.difficulty." + rules.Difficulty.Id.Substring("difficulty.".Length), rules.Difficulty.Name)
            : UiText.T("ui.worldrules.not_chosen", "not chosen"));
        for (int i = 0; i < Rows.Length; i++)
        {
            double v = Rows[i].Get(rules);
            _values[i].Text(Rows[i].Integer ? $"+{v:0}" : $"x{v:0.##}");
        }
        _elites.Text(rules.AllowElites ? UiText.T("ui.worldrules.on", "On") : UiText.T("ui.worldrules.off", "Off"));
        _elites.Color(C(rules.AllowElites ? "#2F5A3A" : "#5A2F36"), C(rules.AllowElites ? "#3C7048" : "#703A43"));
        foreach (var (ability, btn) in _abilityButtons)
        {
            bool off = rules.DisabledAbilities.Contains(ability);
            btn.FontColor(off ? UiTheme.TextFaint : UiTheme.Text);
            btn.Color(C(off ? "#3A2024" : "#2B333E"), C(off ? "#4A2A2F" : "#3C4755"));
        }
    }
}
