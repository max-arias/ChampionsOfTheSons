using ChampionsOfTheSons.Core.Rules;
using ChampionsOfTheSons.Mod.UI;
using RedLoader;
using SonsSdk;
using UnityEngine.InputSystem;

namespace ChampionsOfTheSons.Mod.Settings;

/// <summary>One rebindable mod key: its config entry id and the text key of its label.</summary>
internal sealed record ModKey(string Id, string TextKey, string English);

/// <summary>
/// COS-104: every keyboard key of the mod, rebinding that applies at once (config value, the live input action, the cfg file), reset to
/// defaults, and the conflicts with other mod keys and with the game's own bindings (read from its input maps).
/// COS-133: a key may be a combo ("alt+1"). RedLoader's keybind only watches one key, so the action is bound to the base key and gated on
/// the modifiers here, and the game's item hotkeys on that key are held back while the combo's modifiers are down.
/// </summary>
internal static class ModKeys
{
    public static readonly IReadOnlyList<ModKey> All = BuildList();

    /// <summary>Game action maps that are live while the player plays; menus, debug, console and cutscenes are left out.</summary>
    private static readonly HashSet<string> GameMaps = new(StringComparer.OrdinalIgnoreCase)
    {
        "default", "Inventory", "StructureCrafting", "Construction", "Multiplayer", "ItemHotkey", "Vehicle",
    };

    private const string HotkeyMap = "ItemHotkey";

    /// <summary>Known clashes when the game's input maps cannot be read (the 2026-11-20 scan).</summary>
    private static readonly Dictionary<string, string> FallbackGameKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        ["b"] = "Book", ["c"] = "DismantleElement", ["e"] = "Interact", ["g"] = "Drop", ["i"] = "OpenInventory", ["l"] = "Lighter",
        ["m"] = "GpsTracker", ["p"] = "PlayerList", ["q"] = "Rotate", ["r"] = "Reload", ["t"] = "VoiceChat", ["v"] = "PushToTalk",
        ["x"] = "GoToBuilding", ["z"] = "Sleep", ["w"] = "Vertical", ["a"] = "Horizontal", ["s"] = "Vertical", ["d"] = "Horizontal",
        ["space"] = "Jump", ["tab"] = "AutoComplete", ["escape"] = "Back", ["leftShift"] = "Run", ["leftCtrl"] = "Crouch",
        ["1"] = "Hotkey1", ["2"] = "Hotkey2", ["3"] = "Hotkey3", ["4"] = "Hotkey4", ["5"] = "Hotkey5",
        ["6"] = "Hotkey6", ["7"] = "Hotkey7", ["8"] = "Hotkey8", ["9"] = "Hotkey9", ["0"] = "Hotkey0",
        ["alt+1"] = "SetHotkey1", ["alt+2"] = "SetHotkey2", ["alt+3"] = "SetHotkey3", ["alt+4"] = "SetHotkey4", ["alt+5"] = "SetHotkey5",
        ["alt+6"] = "SetHotkey6", ["alt+7"] = "SetHotkey7", ["alt+8"] = "SetHotkey8", ["alt+9"] = "SetHotkey9", ["alt+0"] = "SetHotkey0",
    };

    private static Dictionary<string, List<string>>? _gameKeys;

    /// <summary>Display names of the game's item hotkey actions, which never count as a clash for a combo (they are held back).</summary>
    private static readonly HashSet<string> HeldBackNames = new(StringComparer.OrdinalIgnoreCase);

    private static readonly List<InputAction> _heldBack = new();

    private static List<ModKey> BuildList()
    {
        var list = new List<ModKey>
        {
            new("inventory_key", "ui.settings.key.inventory", "Inventory"),
            new("perks_key", "ui.settings.key.perks", "Perks"),
            new("ping_key", "ui.settings.key.ping", "Ping"),
            new("host_settings_key", "ui.settings.key.host_settings", "Host settings"),
            new("weapon_key", "ui.settings.key.weapon", "Draw weapon"),
            new("settings_key", "ui.settings.key.settings", "Mod settings"),
        };
        for (int i = 0; i < SpellBook.Slots; i++) list.Add(new($"spell_slot_{i + 1}", "ui.settings.key.spell", "Spell slot {0}"));
        return list;
    }

    public static string Label(ModKey k)
    {
        var text = UiText.T(k.TextKey, k.English);
        return k.Id.StartsWith("spell_slot_", StringComparison.Ordinal) ? string.Format(text, k.Id.Substring("spell_slot_".Length)) : text;
    }

    public static KeybindConfigEntry? Entry(string id)
    {
        try { return Config.Category.GetKeybindEntry(id); } catch { return null; }
    }

    public static string Value(string id)
    {
        try { return Entry(id)?.Value ?? ""; } catch { return ""; }
    }

    public static string Default(string id)
    {
        try { return Entry(id)?.DefaultValue ?? ""; } catch { return ""; }
    }

    public static KeyCombo Combo(string id) => KeyCombo.Parse(Value(id));

    /// <summary>Alt, Ctrl and Shift as held now (either side).</summary>
    private static (bool Ctrl, bool Shift, bool Alt) Held()
    {
        var kb = Keyboard.current;
        if (kb == null) return (false, false, false);
        try { return (kb.ctrlKey.isPressed, kb.shiftKey.isPressed, kb.altKey.isPressed); } catch { return (false, false, false); }
    }

    /// <summary>
    /// Whether a mod key may fire now: its modifiers are held and no other mod key on the same key needs more of them, so a plain 1 does
    /// not fire Alt+1 and Alt+1 does not fire a plain 1. Set as the keybind's CustomCheck and used by <see cref="PressedThisFrame"/>.
    /// </summary>
    public static bool ModifiersOk(string id)
    {
        var (ctrl, shift, alt) = Held();
        return Combo(id).Fires(All.Where(k => k.Id != id).Select(k => Combo(k.Id)), ctrl, shift, alt);
    }

    /// <summary>The key was pressed this frame with its modifiers (for panels that close on their own key outside the keybind's scope).</summary>
    public static bool PressedThisFrame(KeybindConfigEntry entry)
    {
        try { return entry.GetAction().WasPressedThisFrame() && ModifiersOk(entry.Identifier); } catch { return false; }
    }

    /// <summary>The live input action's key path (self-tests).</summary>
    public static string? BoundPath(string id)
    {
        try
        {
            var a = Entry(id)?.GetAction();
            return a != null && a.bindings.Count > 0 ? a.bindings[0].effectivePath : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Binds <paramref name="id"/> to <paramref name="key"/> (an input control name: "f", "comma", "f10", or a combo: "alt+1"),
    /// live and saved.</summary>
    public static bool Rebind(string id, string key, bool save = true)
    {
        var entry = Entry(id);
        if (entry == null || string.IsNullOrEmpty(key)) return false;
        try
        {
            entry.Value = KeyCombo.Parse(key).ToString();
            Apply(entry);
            if (save) Save();
            RLog.Msg($"Champions of the Sons: {id} bound to {entry.Value}");
            return true;
        }
        catch (Exception e)
        {
            RLog.Error($"Champions of the Sons: rebinding {id} to {key} failed: {e.Message}");
            return false;
        }
    }

    /// <summary>Every mod key back to its default.</summary>
    public static void ResetAll()
    {
        foreach (var k in All)
        {
            var d = Default(k.Id);
            if (d.Length > 0) Rebind(k.Id, d, save: false);
        }
        Save();
    }

    /// <summary>Points the input action at the entry's base key, so the new key works without a restart, and gates it on the modifiers.</summary>
    private static void Apply(KeybindConfigEntry entry)
    {
        var bind = entry.GetKeybind();
        var action = bind?.Action;
        if (bind == null || action == null) return;
        string id = entry.Identifier;
        bind.CustomCheck ??= () => ModifiersOk(id);
        string path = "<Keyboard>/" + KeyCombo.Parse(entry.Value).Key;
        if (action.bindings.Count == 0) return;
        if (string.Equals(action.bindings[0].effectivePath, path, StringComparison.OrdinalIgnoreCase)) return;
        InputActionRebindingExtensions.ApplyBindingOverride(action, 0, path);
    }

    /// <summary>On load: actions follow what the cfg file says (a hand edit or the key move in <see cref="Config"/>).</summary>
    public static void ApplyAll()
    {
        foreach (var k in All)
        {
            var e = Entry(k.Id);
            if (e == null) continue;
            try { Apply(e); } catch (Exception ex) { RLog.Warning($"Champions of the Sons: key {k.Id} not applied: {ex.Message}"); }
        }
    }

    public static void Save()
    {
        try { Config.Category.SaveToFile(false); } catch (Exception e) { RLog.Warning($"Champions of the Sons: could not save settings: {e.Message}"); }
    }

    /// <summary>The other mod keys on the same key and modifiers as <paramref name="id"/>.</summary>
    public static List<ModKey> ModClashes(string id)
    {
        var c = Combo(id);
        return c.Key.Length == 0 ? new() : All.Where(k => k.Id != id && Combo(k.Id).SameAs(c)).ToList();
    }

    /// <summary>
    /// The game actions that also fire on <paramref name="key"/>. A combo clashes with the game's own combo and with a plain game binding
    /// on its base key (the game ignores modifiers), except the item hotkeys, which <see cref="Tick"/> holds back while a combo is down.
    /// </summary>
    public static IReadOnlyList<string> GameClashes(string key, string? modKeyId) =>
        modKeyId != null && IntendedOverlaps.TryGetValue(modKeyId, out var ok) && string.Equals(KeyCombo.Parse(key).ToString(), ok, StringComparison.OrdinalIgnoreCase)
            ? Array.Empty<string>()
            : GameClashes(key);

    /// <summary>Mod keys that share a game key on purpose, so the settings do not warn about it. COS-115 (Max 2026-11-20): I opens the
    /// game's inventory and guide book (the game's Open Inventory, Inventory and Open Quick Select on I), and the mod's page with it.</summary>
    private static readonly Dictionary<string, string> IntendedOverlaps = new(StringComparer.Ordinal)
    {
        ["inventory_key"] = "i",
    };

    public static IReadOnlyList<string> GameClashes(string key)
    {
        var c = KeyCombo.Parse(key);
        var map = GameKeys();
        if (!c.HasModifiers) return map.TryGetValue(c.Key, out var plain) ? plain : Array.Empty<string>();
        var list = new List<string>();
        foreach (var k in new[] { c.ToString(), c.Key })
            if (map.TryGetValue(k, out var acts))
                foreach (var a in acts)
                    if (!HeldBackNames.Contains(a) && !list.Contains(a)) list.Add(a);
        return list;
    }

    /// <summary>
    /// Each frame: while the player plays and holds the modifiers of a mod combo, the game's item hotkey actions on that combo's key are
    /// disabled, so Alt+1 casts the spell without also equipping hotkey 1 or setting it (the game binds Alt+number to SetHotkey). Plain
    /// 1-9, and Alt+number while a menu or the inventory is open, reach the game as before.
    /// </summary>
    public static void Tick()
    {
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        bool controllable = false;
        try { controllable = GameState.IsPlayerControllable; } catch { /* not in a world */ }
        if (controllable)
        {
            var (ctrl, shift, alt) = Held();
            if (ctrl || shift || alt)
                foreach (var k in All)
                {
                    var c = Combo(k.Id);
                    if (c.HasModifiers && c.ModifiersHeld(ctrl, shift, alt)) keys.Add(c.Key);
                }
        }
        for (int i = _heldBack.Count - 1; i >= 0; i--)
        {
            var a = _heldBack[i];
            if (keys.Contains(BaseKey(a))) continue;
            _heldBack.RemoveAt(i);
            try { if (MapStillOn(a)) a.Enable(); } catch { /* map gone */ }
        }
        if (keys.Count == 0) return;
        try
        {
            var map = Sons.Input.InputSystem.InputMapping?.asset?.FindActionMap(HotkeyMap);
            if (map == null) return;
            foreach (var a in map.actions)
            {
                if (!a.enabled || !keys.Contains(BaseKey(a))) continue;
                a.Disable();
                _heldBack.Add(a);
            }
        }
        catch (Exception e)
        {
            RLog.Warning($"Champions of the Sons: could not hold back the game's hotkeys: {e.Message}");
        }
    }

    /// <summary>Whether the game action <paramref name="gameAction"/> (e.g. "SetHotkey1") is held back now (self-tests).</summary>
    public static bool HeldBack(string gameAction) => _heldBack.Any(a => string.Equals(a.name, gameAction, StringComparison.OrdinalIgnoreCase));

    /// <summary>The game action <paramref name="name"/> of the item hotkey map (self-tests).</summary>
    public static InputAction? GameHotkey(string name)
    {
        try { return Sons.Input.InputSystem.InputMapping?.asset?.FindActionMap(HotkeyMap)?.FindAction(name); } catch { return null; }
    }

    /// <summary>The action's main keyboard key: "1" for Hotkey1 and for SetHotkey1 (Alt+1).</summary>
    private static string BaseKey(InputAction a)
    {
        foreach (var b in a.bindings)
        {
            if (b.isComposite) continue;
            var key = KeyOf(b.effectivePath);
            if (key == null || KeyCombo.IsModifierKey(key)) continue;
            return key;
        }
        return "";
    }

    /// <summary>A held-back action is turned back on only while the game still has other actions of its map on (it may have switched maps).</summary>
    private static bool MapStillOn(InputAction a)
    {
        var m = a.actionMap;
        if (m == null) return true;
        foreach (var o in m.actions)
            if (o.enabled) return true;
        return false;
    }

    /// <summary>Re-reads the game's bindings on the next query (the player may have changed them in the game's menu).</summary>
    public static void RefreshGameKeys() => _gameKeys = null;

    /// <summary>Game key value ("i", "alt+1") to the game actions on it.</summary>
    public static Dictionary<string, List<string>> GameKeys()
    {
        if (_gameKeys != null) return _gameKeys;
        var map = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var maps = Sons.Input.InputSystem.InputMapping?.asset?.actionMaps;
            if (maps != null)
                foreach (var m in maps)
                {
                    if (!GameMaps.Contains(m.name)) continue;
                    bool hotkeys = string.Equals(m.name, HotkeyMap, StringComparison.OrdinalIgnoreCase);
                    foreach (var a in m.actions)
                    {
                        var name = Spaced(a.name);
                        if (hotkeys) HeldBackNames.Add(name);
                        foreach (var key in ActionKeys(a)) Add(map, key, name);
                    }
                }
        }
        catch (Exception e)
        {
            RLog.Warning($"Champions of the Sons: could not read the game's keys: {e.Message}");
        }
        if (map.Count == 0)
            foreach (var (k, v) in FallbackGameKeys)
            {
                Add(map, k, Spaced(v));
                if (v.Contains("Hotkey", StringComparison.Ordinal)) HeldBackNames.Add(Spaced(v));
            }
        _gameKeys = map;
        return map;
    }

    private static void Add(Dictionary<string, List<string>> map, string key, string name)
    {
        if (!map.TryGetValue(key, out var list)) map[key] = list = new List<string>();
        if (!list.Contains(name)) list.Add(name);
    }

    /// <summary>The keyboard keys of an action as mod key values: plain keys ("i"), and a modifier composite as a combo ("alt+1").</summary>
    private static List<string> ActionKeys(InputAction a)
    {
        var keys = new List<string>();
        var bs = new List<InputBinding>();
        foreach (var b in a.bindings) bs.Add(b);
        for (int i = 0; i < bs.Count; i++)
        {
            var b = bs[i];
            if (b.isComposite)
            {
                bool ctrl = false, shift = false, alt = false, keyboard = true;
                string? main = null;
                int j = i + 1;
                for (; j < bs.Count && bs[j].isPartOfComposite; j++)
                {
                    var k = KeyOf(bs[j].effectivePath);
                    if (k == null) { keyboard = false; continue; }
                    switch (KeyCombo.Modifier(k))
                    {
                        case 'c': ctrl = true; break;
                        case 's': shift = true; break;
                        case 'a': alt = true; break;
                        default: main = k; break;
                    }
                }
                if (keyboard && main != null) keys.Add(new KeyCombo(main, ctrl, shift, alt).ToString());
                i = j - 1;
                continue;
            }
            var key = KeyOf(b.effectivePath);
            if (key != null) keys.Add(key);
        }
        return keys;
    }

    private static string? KeyOf(string? path) =>
        path != null && path.StartsWith("<Keyboard>/", StringComparison.OrdinalIgnoreCase) ? path.Substring("<Keyboard>/".Length) : null;

    /// <summary>"PushToTalk" -> "Push To Talk".</summary>
    private static string Spaced(string name)
    {
        var sb = new System.Text.StringBuilder(name.Length + 4);
        for (int i = 0; i < name.Length; i++)
        {
            if (i > 0 && char.IsUpper(name[i]) && !char.IsUpper(name[i - 1])) sb.Append(' ');
            sb.Append(name[i]);
        }
        return sb.ToString();
    }
}

/// <summary>Short display names for key control names.</summary>
internal static class KeyNames
{
    private static readonly Dictionary<string, string> Symbols = new(StringComparer.OrdinalIgnoreCase)
    {
        ["comma"] = ",", ["period"] = ".", ["slash"] = "/", ["backslash"] = "\\", ["semicolon"] = ";", ["quote"] = "'",
        ["backquote"] = "`", ["leftBracket"] = "[", ["rightBracket"] = "]", ["minus"] = "-", ["equals"] = "=",
        ["space"] = "Space", ["enter"] = "Enter", ["tab"] = "Tab", ["backspace"] = "Bksp", ["escape"] = "Esc",
        ["leftShift"] = "LShift", ["rightShift"] = "RShift", ["leftCtrl"] = "LCtrl", ["rightCtrl"] = "RCtrl",
        ["leftAlt"] = "LAlt", ["rightAlt"] = "RAlt", ["upArrow"] = "Up", ["downArrow"] = "Down", ["leftArrow"] = "Left",
        ["rightArrow"] = "Right", ["pageUp"] = "PgUp", ["pageDown"] = "PgDn", ["insert"] = "Ins", ["delete"] = "Del",
    };

    /// <summary>"Alt+1", "F", "," for a mod key value.</summary>
    public static string Short(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "-";
        var c = KeyCombo.Parse(value);
        return c.HasModifiers ? c.Display(ShortKey) : ShortKey(c.Key);
    }

    private static string ShortKey(string key)
    {
        if (string.IsNullOrEmpty(key)) return "-";
        if (Symbols.TryGetValue(key, out var s)) return s;
        if (key.StartsWith("numpad", StringComparison.OrdinalIgnoreCase)) return "Num" + key.Substring(6);
        return key.Length <= 3 ? key.ToUpperInvariant() : char.ToUpperInvariant(key[0]) + key.Substring(1);
    }
}
