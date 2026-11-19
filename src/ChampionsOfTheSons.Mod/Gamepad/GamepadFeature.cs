using ChampionsOfTheSons.Core.Rules;
using ChampionsOfTheSons.Mod.HostRules;
using ChampionsOfTheSons.Mod.Perks;
using ChampionsOfTheSons.Mod.Spells;
using ChampionsOfTheSons.Mod.UI;
using RedLoader;
using UnityEngine;

namespace ChampionsOfTheSons.Mod.Pad;

/// <summary>
/// COS-30: gamepad support for the mod's panels. In the world, bindable pad buttons open the panels and cast spells.
/// With a panel open, the d-pad or left stick moves a focus frame between its buttons, A confirms, B goes back, X is a right click
/// (item menu). Bindings are text in the mod settings: "LeftShoulder+DpadUp" means DpadUp while LeftShoulder is held
/// (buttons: South East West North LeftShoulder RightShoulder LeftTrigger RightTrigger DpadUp DpadDown DpadLeft DpadRight Start Select LeftStick RightStick).
/// </summary>
internal sealed class GamepadFeature : IModFeature
{
    internal sealed record PadPanel(string Name, Func<bool> IsOpen, Action Toggle, Action Back, Func<GameObject?> Root, Func<GameObject?>? Scope = null);

    private static readonly string[] SpellDefaults =
    {
        "RightShoulder+South", "RightShoulder+East", "RightShoulder+West", "RightShoulder+North", "RightShoulder+DpadLeft", "RightShoulder+DpadRight",
    };

    private static ConfigEntry<string>[] _spellEntries = Array.Empty<ConfigEntry<string>>();
    private static readonly Dictionary<string, ConfigEntry<string>> PanelEntries = new();
    private static readonly Dictionary<string, PadBinding?> Parsed = new();

    private static List<PadPanel> _panels = new();
    private static PadPanel? _focusPanel;
    private static GameObject? _focusScope;

    /// <summary>The spell slot the last pad binding cast (-1: none yet).</summary>
    internal static int LastPadCast { get; set; } = -1;

    internal static IReadOnlyList<PadPanel> Panels => _panels;
    internal static PadPanel? OpenPanel => _panels.FirstOrDefault(p => p.IsOpen());

    public void Init()
    {
        var cat = Config.Category;
        var defaults = new (string Name, string Binding, string Title)[]
        {
            ("inventory", "LeftShoulder+DpadUp", "Inventory"),
            ("perks", "LeftShoulder+DpadRight", "Perks"),
            ("crafting", "LeftShoulder+DpadDown", "Crafting"),
            ("host_settings", "LeftShoulder+DpadLeft", "Host settings"),
        };
        foreach (var (name, binding, title) in defaults)
            PanelEntries[name] = cat.CreateEntry($"pad_{name}", binding, $"Gamepad: {title}", $"Pad buttons that open the {title.ToLowerInvariant()} panel, e.g. LeftShoulder+DpadUp.");
        _spellEntries = new ConfigEntry<string>[SpellBook.Slots];
        for (int i = 0; i < SpellBook.Slots; i++)
            _spellEntries[i] = cat.CreateEntry($"pad_spell_{i + 1}", SpellDefaults[Math.Min(i, SpellDefaults.Length - 1)], $"Gamepad: spell slot {i + 1}", $"Pad buttons that cast the spell in slot {i + 1}.");

        _panels = new List<PadPanel>
        {
            new("inventory", () => CotsUi.Inventory.IsOpen, () => CotsUi.Inventory.Toggle(),
                () => { if (!CotsUi.Inventory.CloseMenu()) CotsUi.Inventory.Close(); },
                () => CotsUi.Inventory.PadRoot, () => CotsUi.Inventory.PadMenuRoot),
            new("perks", () => PerksFeature.Panel.IsOpen, () => PerksFeature.Panel.Toggle(), () => PerksFeature.Panel.Close(), () => PerksFeature.Panel.PadRoot),
            new("crafting", () => Crafting.CraftingFeature.Panel.IsOpen, () => Crafting.CraftingFeature.Panel.Toggle(), () => Crafting.CraftingFeature.Panel.Close(),
                () => Crafting.CraftingFeature.Panel.PadRoot),
            new("host_settings", () => WorldRulesFeature.Settings.IsOpen, () => { if (WorldRulesFeature.Settings.IsOpen) WorldRulesFeature.Settings.Close(); else WorldRulesFeature.Settings.Open(); }, () => WorldRulesFeature.Settings.Close(),
                () => WorldRulesFeature.Settings.PadRoot),
            new("picker", () => WorldRulesFeature.Picker.IsOpen, () => { if (WorldRulesFeature.Picker.IsOpen) WorldRulesFeature.Picker.Close(); else WorldRulesFeature.Picker.Open(); }, () => WorldRulesFeature.Picker.Close(),
                () => WorldRulesFeature.Picker.PadRoot),
        };
    }

    private static PadBinding? Binding(ConfigEntry<string> entry, string fallback)
    {
        string text;
        try
        {
            text = entry.Value ?? fallback;
        }
        catch
        {
            text = fallback;
        }
        if (!Parsed.TryGetValue(text, out var b))
        {
            b = PadBinding.TryParse(text, out var parsed) ? parsed : null;
            Parsed[text] = b;
        }
        return b;
    }

    public void Update(float dt, bool inWorld)
    {
        PadInput.Poll(dt);
        PadPointer.Tick();
        if (SelfTest.Enabled && Enum.GetValues<PadButton>().Any(PadInput.Pressed))
            SelfTest.Log($"pad: pressed {string.Join("+", Enum.GetValues<PadButton>().Where(PadInput.Held))}; open={OpenPanel?.Name ?? "-"}; inWorld={inWorld}; controllable={WorldRulesFeature.PlayerControllable}");
        if (_panels.Count == 0) return;

        var open = OpenPanel;
        if (open != null)
        {
            Navigate(open);
            return;
        }
        if (_focusPanel != null) { _focusPanel = null; _focusScope = null; PadFocus.Clear(); PadPointer.Release(); }

        if (!inWorld || !WorldRulesFeature.PlayerControllable) return;
        foreach (var p in _panels)
        {
            if (!PanelEntries.TryGetValue(p.Name, out var entry)) continue;
            var b = Binding(entry, "");
            if (b != null && PadInput.IsTriggered(b))
            {
                if (p.Name == "host_settings" && !Bridge.EnemyRegistry.IsAuthority) continue; // as the keybind: the host only
                p.Toggle();
                return;
            }
        }
        for (int i = 0; i < _spellEntries.Length; i++)
        {
            var b = Binding(_spellEntries[i], SpellDefaults[Math.Min(i, SpellDefaults.Length - 1)]);
            if (b != null && PadInput.IsTriggered(b))
            {
                LastPadCast = i;
                SpellCaster.Cast(i);
                return;
            }
        }
    }

    private static void Navigate(PadPanel panel)
    {
        var root = panel.Root();
        if (root == null) return;
        var scope = panel.Scope?.Invoke() ?? root;

        // The same buttons that opened the panel close it again.
        if (PanelEntries.TryGetValue(panel.Name, out var entry))
        {
            var b = Binding(entry, "");
            if (b != null && PadInput.IsTriggered(b)) { Close(panel); return; }
        }
        if (PadInput.Pressed(PadButton.East) || PadInput.Pressed(PadButton.Select)) { panel.Back(); AfterBack(panel); return; }

        bool panelChanged = _focusPanel != panel || _focusScope != scope;
        _focusPanel = panel;
        _focusScope = scope;
        if (panelChanged)
        {
            PadFocus.Clear();
            if (PadInput.RecentlyUsed) PadFocus.Enter(scope);
        }

        bool any = false;
        foreach (var dir in new[] { FocusDirection.Up, FocusDirection.Down, FocusDirection.Left, FocusDirection.Right })
        {
            if (!PadInput.NavPressed(dir)) continue;
            any = true;
            if (PadFocus.Current == null) PadFocus.Enter(scope);
            else PadFocus.Move(scope, dir);
        }
        if (PadInput.Pressed(PadButton.South) || PadInput.Pressed(PadButton.West))
        {
            any = true;
            if (PadFocus.Current == null) PadFocus.Enter(scope);
            else if (PadInput.Pressed(PadButton.South)) PadFocus.Confirm();
            else PadFocus.Secondary();
        }
        if (!any && PadFocus.Current == null && PadInput.RecentlyUsed) PadFocus.Enter(scope);
        PadFocus.Refresh();
    }

    private static void Close(PadPanel panel)
    {
        panel.Toggle();
        AfterBack(panel);
    }

    private static void AfterBack(PadPanel panel)
    {
        if (!panel.IsOpen()) { PadFocus.Clear(); _focusPanel = null; _focusScope = null; }
    }

    public void RegisterSelfTests() => GamepadSelfTest.Register();
}
