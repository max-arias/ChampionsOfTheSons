using ChampionsOfTheSons.Core.Rules;
using RedLoader;
using SonsSdk;

namespace ChampionsOfTheSons.Mod;

public static class Config
{
    /// <summary>Spell slot defaults since COS-133: Alt+1..Alt+6, in a row on the number keys. Plain 1-9 stay the game's item hotkeys;
    /// the game's own Alt+number (set hotkey) is held back while the player plays (<see cref="Settings.ModKeys.Tick"/>).</summary>
    public static readonly string[] SpellDefaults = { "alt+1", "alt+2", "alt+3", "alt+4", "alt+5", "alt+6" };

    /// <summary>COS-104's defaults (keys the game does not use, scattered over the board), moved once to <see cref="SpellDefaults"/>.</summary>
    private static readonly string[] V1SpellDefaults = { "f", "y", "j", "n", "comma", "period" };

    /// <summary>The defaults before COS-104 (Z sleeps, X opens building, V is push-to-talk, B the guide book, M the GPS).</summary>
    private static readonly string[] OldSpellDefaults = { "z", "x", "v", "b", "n", "m" };
    private const string OldPerksDefault = "p", PerksDefault = "u";

    /// <summary>Bump when a default key changes again and add the move to <see cref="MigrateKeys"/>.</summary>
    private const int KeysVersion = 3;

    public static ConfigCategory Category { get; private set; } = null!;

    public static KeybindConfigEntry PerksKey { get; private set; } = null!;
    public static KeybindConfigEntry SettingsKey { get; private set; } = null!;
    public static KeybindConfigEntry[] SpellKeys { get; } = new KeybindConfigEntry[SpellBook.Slots];
    public static ConfigEntry<bool> ShowNameplates { get; private set; } = null!;
    public static ConfigEntry<bool> ShowDamageNumbers { get; private set; } = null!;
    public static ConfigEntry<bool> ShowScanner { get; private set; } = null!;
    public static ConfigEntry<bool> ShowBossBar { get; private set; } = null!;

    /// <summary>COS-95: one switch for the whole mod HUD (action bar, XP bar, streak, buffs, nameplates, numbers, boss bar).</summary>
    public static ConfigEntry<bool> ShowHud { get; private set; } = null!;

    /// <summary>COS-95: quick hits on one enemy add up into one running number.</summary>
    public static ConfigEntry<bool> MergeDamageNumbers { get; private set; } = null!;

    /// <summary>COS-97: one-time hint toasts.</summary>
    public static ConfigEntry<bool> ShowHints { get; private set; } = null!;

    /// <summary>Mod sound volume, 0..1, on top of the game's SFX volume (COS-101).</summary>
    public static ConfigEntry<float> SoundVolume { get; private set; } = null!;

    private static ConfigEntry<int> _keysVersion = null!;

    public static void Init()
    {
        Category = ConfigSystem.CreateFileCategory("ChampionsOfTheSons", "Champions of the Sons", "ChampionsOfTheSons.cfg");

        // No inventory key: the game's own book key (B) opens the guide book, and the mod's pages are its tab. A mod key on I
        // stopped the game's inventory from opening (2026-11-21).
        PerksKey = Category.CreateKeybindEntry("perks_key", PerksDefault, "Perks", "Open the perk tree.");
        PerksKey.SetScope(needsPlayerControllable: true);

        SettingsKey = Category.CreateKeybindEntry("settings_key", "f10", "Mod settings", "Open the Champions settings (keys, sound).");
        SettingsKey.SetScope(needsPlayerControllable: true);

        for (int i = 0; i < SpellBook.Slots; i++)
        {
            SpellKeys[i] = Category.CreateKeybindEntry($"spell_slot_{i + 1}", SpellDefaults[i], $"Spell slot {i + 1}", $"Cast the spell in slot {i + 1}.");
            SpellKeys[i].SetScope(needsPlayerControllable: true);
        }

        ShowNameplates = Category.CreateEntry("show_nameplates", true, "Enemy nameplates", "Show level and health above enemies.");
        ShowDamageNumbers = Category.CreateEntry("show_damage_numbers", true, "Damage numbers", "Show floating damage numbers.");
        ShowScanner = Category.CreateEntry("show_scanner", true, "Enemy scanner", "Looking at an enemy shows its armor, damage, bounty and affixes next to its nameplate.");
        ShowBossBar = Category.CreateEntry("show_boss_bar", true, "Boss health bar", "Show a wide health bar at the top of the screen while a boss is fighting you.");
        ShowHud = Category.CreateEntry("show_hud", true, "Mod HUD", "Show the mod's HUD: action bar, XP bar, kill streak, buffs, nameplates, damage numbers and boss bar.");
        MergeDamageNumbers = Category.CreateEntry("merge_damage_numbers", true, "Merge damage numbers", "Quick hits on the same enemy add up into one number.");
        ShowHints = Category.CreateEntry("show_hints", true, "Hints", "Show a short hint the first time something new comes up.");

        SoundVolume = Category.CreateEntry("sound_volume", 1f, "Sound volume", "Volume of the mod's sounds, 0 to 1, on top of the game's sound effects volume.");
        SoundVolume.SetRange(0f, 1f);

        _keysVersion = Category.CreateEntry("keys_version", 0, "Key defaults version", "Internal: which key defaults this file was moved to. Do not edit.");
        _keysVersion.IsHidden = true;
        MigrateKeys();
        Settings.ModKeys.ApplyAll(); // combos bind their base key (COS-133); feature keys follow at game start
    }

    /// <summary>
    /// Once per config file, keys still on an old default move to the new one, step by step: version 1 (COS-104) off the game's keys,
    /// version 2 (COS-133) onto Alt+1..Alt+6. Keys the player changed stay, and a key whose new default another mod key already uses
    /// stays too (no new clash is made).
    /// </summary>
    private static void MigrateKeys()
    {
        int from = _keysVersion.Value;
        if (from >= KeysVersion) return;
        int moved = 0;
        if (from < 1)
        {
            var moves = new List<(KeybindConfigEntry Entry, string Old, string New)> { (PerksKey, OldPerksDefault, PerksDefault) };
            for (int i = 0; i < SpellBook.Slots; i++) moves.Add((SpellKeys[i], OldSpellDefaults[i], V1SpellDefaults[i]));
            moved += Move(moves);
        }
        if (from < 2)
        {
            var moves = new List<(KeybindConfigEntry Entry, string Old, string New)>();
            for (int i = 0; i < SpellBook.Slots; i++) moves.Add((SpellKeys[i], V1SpellDefaults[i], SpellDefaults[i]));
            moved += Move(moves);
        }
        // Version 3 moved the draw-weapon key (COS-107); that key is gone since the weapon slot gives the weapon (COS-187).
        _keysVersion.Value = KeysVersion;
        try { Category.SaveToFile(false); } catch (Exception e) { RLog.Warning($"Champions of the Sons: could not save keys: {e.Message}"); }
        if (moved > 0) RLog.Msg($"Champions of the Sons: moved {moved} keys to the new defaults (keys version {from} -> {KeysVersion})");
    }

    private static int Move(List<(KeybindConfigEntry Entry, string Old, string New)> moves)
    {
        var plan = KeyMigration.Plan(moves.Select(m => (m.Entry.Identifier, Value(m.Entry), m.Old, m.New)).ToList());
        int moved = 0;
        foreach (var m in moves)
        {
            if (!plan.TryGetValue(m.Entry.Identifier, out var to) || to == Value(m.Entry)) continue;
            m.Entry.Value = to;
            moved++;
        }
        return moved;
    }

    private static string Value(KeybindConfigEntry e)
    {
        try { return e.Value ?? ""; } catch { return ""; }
    }
}
