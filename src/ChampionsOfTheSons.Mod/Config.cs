using ChampionsOfTheSons.Core.Rules;
using RedLoader;
using SonsSdk;

namespace ChampionsOfTheSons.Mod;

public static class Config
{
    /// <summary>Spell slot defaults since COS-104: keys the game does not use (checked against the game's input maps on 2026-11-20:
    /// Z sleeps, X opens building, V is push-to-talk, B the guide book, M the GPS, P the player list).</summary>
    public static readonly string[] SpellDefaults = { "f", "y", "j", "n", "comma", "period" };

    /// <summary>The defaults before COS-104, moved once by <see cref="MigrateKeys"/> when a player still has them.</summary>
    private static readonly string[] OldSpellDefaults = { "z", "x", "v", "b", "n", "m" };
    private const string OldPerksDefault = "p", PerksDefault = "u";

    /// <summary>Bump when a default key changes again and add the move to <see cref="MigrateKeys"/>.</summary>
    private const int KeysVersion = 1;

    public static ConfigCategory Category { get; private set; } = null!;

    public static KeybindConfigEntry InventoryKey { get; private set; } = null!;
    public static KeybindConfigEntry PerksKey { get; private set; } = null!;
    public static KeybindConfigEntry SettingsKey { get; private set; } = null!;
    public static KeybindConfigEntry[] SpellKeys { get; } = new KeybindConfigEntry[SpellBook.Slots];
    public static ConfigEntry<bool> ShowNameplates { get; private set; } = null!;
    public static ConfigEntry<bool> ShowDamageNumbers { get; private set; } = null!;
    public static ConfigEntry<bool> ShowScanner { get; private set; } = null!;
    public static ConfigEntry<bool> ShowBossBar { get; private set; } = null!;
    public static ConfigEntry<bool> InventoryInBook { get; private set; } = null!;

    /// <summary>Mod sound volume, 0..1, on top of the game's SFX volume (COS-101).</summary>
    public static ConfigEntry<float> SoundVolume { get; private set; } = null!;

    private static ConfigEntry<int> _keysVersion = null!;

    public static void Init()
    {
        Category = ConfigSystem.CreateFileCategory("ChampionsOfTheSons", "Champions of the Sons", "ChampionsOfTheSons.cfg");

        InventoryKey = Category.CreateKeybindEntry("inventory_key", "i", "Inventory", "Open the Champions inventory.");
        InventoryKey.SetScope(needsPlayerControllable: true);

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
        InventoryInBook = Category.CreateEntry("inventory_in_book", true, "Inventory in the guide book", "Show gear, bag and character on the guide book pages. Off: the screen panel.");

        SoundVolume = Category.CreateEntry("sound_volume", 1f, "Sound volume", "Volume of the mod's sounds, 0 to 1, on top of the game's sound effects volume.");
        SoundVolume.SetRange(0f, 1f);

        _keysVersion = Category.CreateEntry("keys_version", 0, "Key defaults version", "Internal: which key defaults this file was moved to. Do not edit.");
        _keysVersion.IsHidden = true;
        MigrateKeys();
    }

    /// <summary>
    /// COS-104: once per config file, keys still on the old defaults that clash with the game move to the new defaults. Keys the player
    /// changed stay, and a key whose new default another mod key already uses stays too (no new clash is made).
    /// </summary>
    private static void MigrateKeys()
    {
        if (_keysVersion.Value >= KeysVersion) return;
        var moves = new List<(KeybindConfigEntry Entry, string Old, string New)> { (PerksKey, OldPerksDefault, PerksDefault) };
        for (int i = 0; i < SpellBook.Slots; i++) moves.Add((SpellKeys[i], OldSpellDefaults[i], SpellDefaults[i]));

        var plan = KeyMigration.Plan(moves.Select(m => (m.Entry.Identifier, Value(m.Entry), m.Old, m.New)).ToList());
        int moved = 0;
        foreach (var m in moves)
        {
            if (!plan.TryGetValue(m.Entry.Identifier, out var to) || to == Value(m.Entry)) continue;
            m.Entry.Value = to;
            moved++;
        }
        _keysVersion.Value = KeysVersion;
        try { Category.SaveToFile(false); } catch (Exception e) { RLog.Warning($"Champions of the Sons: could not save keys: {e.Message}"); }
        if (moved > 0) RLog.Msg($"Champions of the Sons: moved {moved} keys off the game's keys (COS-104)");
    }

    private static string Value(KeybindConfigEntry e)
    {
        try { return e.Value ?? ""; } catch { return ""; }
    }
}
