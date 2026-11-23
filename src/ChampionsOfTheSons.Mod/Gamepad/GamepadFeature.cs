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

    // COS-151: the action bar's weapon and consumable groups. The game binds the whole plain d-pad in the world, so these are chords.
    private static readonly (string Name, string Binding, string Title, string Help, Action Run)[] BarActions =
    {
        ("next_weapon", "RightShoulder+DpadUp", "next weapon", "Pad buttons that equip the next weapon on the action bar's weapon keys.", () => ActionBar.ActionBarFeature.Bar.NextWeapon()),
        ("use_consumable", "RightShoulder+DpadDown", "use consumable", "Pad buttons that use the picked item on the action bar's consumable keys.", () => ActionBar.ActionBarFeature.Bar.UseConsumable()),
        ("next_consumable", "LeftShoulder+DpadDown", "next consumable", "Pad buttons that pick the next item on the action bar's consumable keys.", () => ActionBar.ActionBarFeature.Bar.NextConsumable()),
    };
    private static ConfigEntry<string>[] _barEntries = Array.Empty<ConfigEntry<string>>();
    private static readonly Dictionary<string, ConfigEntry<string>> PanelEntries = new();
    private static readonly Dictionary<string, PadBinding?> Parsed = new();

    private static List<PadPanel> _panels = new();
    private static PadPanel? _focusPanel;
    private static GameObject? _focusScope;

    /// <summary>The spell slot the last pad binding cast (-1: none yet).</summary>
    internal static int LastPadCast { get; set; } = -1;

    /// <summary>The action bar pad action that ran last ("next_weapon", ...), for tests.</summary>
    internal static string LastBarAction { get; set; } = "";

    internal static PadPanel? OpenPanel => _panels.FirstOrDefault(p => p.IsOpen());

    public void Init()
    {
        var cat = Config.Category;
        var defaults = new (string Name, string Binding, string Title)[]
        {
            ("perks", "LeftShoulder+DpadRight", "Perks"),
            ("host_settings", "LeftShoulder+DpadLeft", "Host settings"),
            ("settings", "LeftShoulder+Select", "Mod settings"),
        };
        foreach (var (name, binding, title) in defaults)
            PanelEntries[name] = cat.CreateEntry($"pad_{name}", binding, $"Gamepad: {title}", $"Pad buttons that open the {title.ToLowerInvariant()} panel, e.g. LeftShoulder+DpadUp.");
        _spellEntries = new ConfigEntry<string>[SpellBook.Slots];
        for (int i = 0; i < SpellBook.Slots; i++)
            _spellEntries[i] = cat.CreateEntry($"pad_spell_{i + 1}", SpellDefaults[Math.Min(i, SpellDefaults.Length - 1)], $"Gamepad: spell slot {i + 1}", $"Pad buttons that cast the spell in slot {i + 1}.");

        _barEntries = BarActions.Select(a => cat.CreateEntry($"pad_{a.Name}", a.Binding, $"Gamepad: {a.Title}", a.Help)).ToArray();

        _panels = new List<PadPanel>
        {
            // COS-168: perks live on the guide book now; the binding opens it on the Perks page (the book has its own pad cursor).
            new("perks", () => false, PerksFeature.TogglePerksPage, () => { }, () => null),
            new("host_settings", () => WorldRulesFeature.Settings.IsOpen, () => { if (WorldRulesFeature.Settings.IsOpen) WorldRulesFeature.Settings.Close(); else WorldRulesFeature.Settings.Open(); }, () => WorldRulesFeature.Settings.Close(),
                () => WorldRulesFeature.Settings.PadRoot),
            new("settings", () => Settings.SettingsFeature.Panel.IsOpen, () => Settings.SettingsFeature.Panel.Toggle(), () => Settings.SettingsFeature.Panel.Back(),
                () => Settings.SettingsFeature.Panel.PadRoot),
        };
    }

    /// <summary>Whether slot <paramref name="slot"/>'s pad binding is held (main button and its modifiers), for SpellControl (COS-82).
    /// False while a mod panel has the pad.</summary>
    public static bool SpellHeld(int slot)
    {
        if (slot < 0 || slot >= _spellEntries.Length || OpenPanel != null) return false;
        var b = Binding(_spellEntries[slot], SpellDefaults[Math.Min(slot, SpellDefaults.Length - 1)]);
        return b != null && PadInput.Held(b.Main) && b.Modifiers.All(PadInput.Held);
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
                LastPadCast = i; // the cast itself is SpellControl's, from SpellHeld (press, hold, release; COS-82)
                return;
            }
        }
        for (int i = 0; i < _barEntries.Length; i++)
        {
            var b = Binding(_barEntries[i], BarActions[i].Binding);
            if (b != null && PadInput.IsTriggered(b))
            {
                LastBarAction = BarActions[i].Name;
                BarActions[i].Run();
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
        if (PadInput.Pressed(PadButton.South))
        {
            any = true;
            if (PadFocus.Current == null) PadFocus.Enter(scope);
            else PadFocus.Confirm();
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
