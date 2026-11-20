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
/// </summary>
internal static class ModKeys
{
    public static readonly IReadOnlyList<ModKey> All = BuildList();

    /// <summary>Game action maps that are live while the player plays; menus, debug, console and cutscenes are left out.</summary>
    private static readonly HashSet<string> GameMaps = new(StringComparer.OrdinalIgnoreCase)
    {
        "default", "Inventory", "StructureCrafting", "Construction", "Multiplayer", "ItemHotkey", "Vehicle",
    };

    /// <summary>Known clashes when the game's input maps cannot be read (the 2026-11-20 scan).</summary>
    private static readonly Dictionary<string, string> FallbackGameKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        ["b"] = "Book", ["c"] = "DismantleElement", ["e"] = "Interact", ["g"] = "Drop", ["i"] = "OpenInventory", ["l"] = "Lighter",
        ["m"] = "GpsTracker", ["p"] = "PlayerList", ["q"] = "Rotate", ["r"] = "Reload", ["t"] = "VoiceChat", ["v"] = "PushToTalk",
        ["x"] = "GoToBuilding", ["z"] = "Sleep", ["w"] = "Vertical", ["a"] = "Horizontal", ["s"] = "Vertical", ["d"] = "Horizontal",
        ["space"] = "Jump", ["tab"] = "AutoComplete", ["escape"] = "Back", ["leftShift"] = "Run", ["leftCtrl"] = "Crouch",
        ["1"] = "Hotkey1", ["2"] = "Hotkey2", ["3"] = "Hotkey3", ["4"] = "Hotkey4", ["5"] = "Hotkey5",
        ["6"] = "Hotkey6", ["7"] = "Hotkey7", ["8"] = "Hotkey8", ["9"] = "Hotkey9", ["0"] = "Hotkey0",
    };

    private static Dictionary<string, List<string>>? _gameKeys;

    private static List<ModKey> BuildList()
    {
        var list = new List<ModKey>
        {
            new("inventory_key", "ui.settings.key.inventory", "Inventory"),
            new("perks_key", "ui.settings.key.perks", "Perks"),
            new("crafting_key", "ui.settings.key.crafting", "Crafting"),
            new("ping_key", "ui.settings.key.ping", "Ping"),
            new("host_settings_key", "ui.settings.key.host_settings", "Host settings"),
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

    /// <summary>Binds <paramref name="id"/> to <paramref name="key"/> (an input control name: "f", "comma", "f10"), live and saved.</summary>
    public static bool Rebind(string id, string key, bool save = true)
    {
        var entry = Entry(id);
        if (entry == null || string.IsNullOrEmpty(key)) return false;
        try
        {
            entry.Value = key;
            Apply(entry);
            if (save) Save();
            RLog.Msg($"Champions of the Sons: {id} bound to {key}");
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

    /// <summary>Points the input action at the entry's key, so the new key works without a restart.</summary>
    private static void Apply(KeybindConfigEntry entry)
    {
        var action = entry.GetAction();
        if (action == null) return;
        string path = "<Keyboard>/" + entry.Value;
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

    /// <summary>The other mod keys on the same key as <paramref name="id"/>.</summary>
    public static List<ModKey> ModClashes(string id)
    {
        var v = Value(id);
        return v.Length == 0 ? new() : All.Where(k => k.Id != id && string.Equals(Value(k.Id), v, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    /// <summary>The game actions bound to <paramref name="key"/>.</summary>
    public static IReadOnlyList<string> GameClashes(string key)
    {
        var map = GameKeys();
        return map.TryGetValue(key, out var list) ? list : Array.Empty<string>();
    }

    /// <summary>Re-reads the game's bindings on the next query (the player may have changed them in the game's menu).</summary>
    public static void RefreshGameKeys() => _gameKeys = null;

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
                    foreach (var a in m.actions)
                        foreach (var b in a.bindings)
                        {
                            var p = b.effectivePath;
                            if (p == null || !p.StartsWith("<Keyboard>/", StringComparison.OrdinalIgnoreCase)) continue;
                            var key = p.Substring("<Keyboard>/".Length);
                            if (!map.TryGetValue(key, out var list)) map[key] = list = new List<string>();
                            var name = Spaced(a.name);
                            if (!list.Contains(name)) list.Add(name);
                        }
                }
        }
        catch (Exception e)
        {
            RLog.Warning($"Champions of the Sons: could not read the game's keys: {e.Message}");
        }
        if (map.Count == 0)
            foreach (var (k, v) in FallbackGameKeys) map[k] = new List<string> { Spaced(v) };
        _gameKeys = map;
        return map;
    }

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

    public static string Short(string? key)
    {
        if (string.IsNullOrEmpty(key)) return "-";
        if (Symbols.TryGetValue(key, out var s)) return s;
        if (key.StartsWith("numpad", StringComparison.OrdinalIgnoreCase)) return "Num" + key.Substring(6);
        return key.Length <= 3 ? key.ToUpperInvariant() : char.ToUpperInvariant(key[0]) + key.Substring(1);
    }
}
