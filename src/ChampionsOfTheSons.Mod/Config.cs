using RedLoader;
using SonsSdk;

namespace ChampionsOfTheSons.Mod;

public static class Config
{
    public static ConfigCategory Category { get; private set; } = null!;

    public static KeybindConfigEntry InventoryKey { get; private set; } = null!;
    public static KeybindConfigEntry PerksKey { get; private set; } = null!;
    public static ConfigEntry<bool> ShowNameplates { get; private set; } = null!;
    public static ConfigEntry<bool> ShowDamageNumbers { get; private set; } = null!;
    public static ConfigEntry<bool> ShowScanner { get; private set; } = null!;
    public static ConfigEntry<bool> ShowBossBar { get; private set; } = null!;
    public static ConfigEntry<bool> InventoryInBook { get; private set; } = null!;

    public static void Init()
    {
        Category = ConfigSystem.CreateFileCategory("ChampionsOfTheSons", "Champions of the Sons", "ChampionsOfTheSons.cfg");

        InventoryKey = Category.CreateKeybindEntry("inventory_key", "i", "Inventory", "Open the Champions inventory.");
        InventoryKey.SetScope(needsPlayerControllable: true);

        PerksKey = Category.CreateKeybindEntry("perks_key", "p", "Perks", "Open the perk tree.");
        PerksKey.SetScope(needsPlayerControllable: true);

        ShowNameplates = Category.CreateEntry("show_nameplates", true, "Enemy nameplates", "Show level and health above enemies.");
        ShowDamageNumbers = Category.CreateEntry("show_damage_numbers", true, "Damage numbers", "Show floating damage numbers.");
        ShowScanner = Category.CreateEntry("show_scanner", true, "Enemy scanner", "Looking at an enemy shows its armor, damage, bounty and affixes next to its nameplate.");
        ShowBossBar = Category.CreateEntry("show_boss_bar", true, "Boss health bar", "Show a wide health bar at the top of the screen while a boss is fighting you.");
        InventoryInBook = Category.CreateEntry("inventory_in_book", true, "Inventory in the guide book", "Show gear, bag and character on the guide book pages. Off: the screen panel.");
    }
}
