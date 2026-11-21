using System.Text.Json;
using System.Text.Json.Nodes;

namespace ChampionsOfTheSons.Core.Saving;

/// <summary>
/// JSON (de)serialization of the save DTOs with versioned migrations.
/// Older versions are migrated step by step up to the current one; newer versions are refused
/// with <see cref="SaveVersionException"/> so an old mod never silently drops data it doesn't know.
/// </summary>
public static class SaveCodec
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    /// <summary>Migrations for <see cref="PlayerSave"/>: index i upgrades version i+1 → i+2 (in place).</summary>
    private static readonly Action<JsonObject>[] PlayerMigrations =
    {
        MarkStatSlotsUnknown, // 1 → 2 (COS-73)
        RemoveDeletedPerks,   // 2 → 3 (COS-86/COS-87)
        CollapseItemRarities, // 3 → 4 (COS-109)
        RemoveCraftingContent, // 4 → 5 (COS-145)
        RemoveIslandFoods, // 5 → 6 (COS-147; the grant day defaults to none, COS-100)
    };

    /// <summary>The four island foods never had a use and are gone (COS-147, Max: remove).</summary>
    private static readonly string[] IslandFoods =
        { "item.canned_pasta_ration", "item.cult_sacrament_wafer", "item.minibar_whiskey", "item.bottled_cave_water" };

    private static void RemoveIslandFoods(JsonObject save)
    {
        if (save["slots"] is not JsonObject slots) return;
        foreach (var key in slots.Select(kv => kv.Key).ToList())
            if (slots[key] is JsonObject item && item["baseId"]?.GetValue<string>() is { } id && IslandFoods.Contains(id)) slots.Remove(key);
    }

    /// <summary>3 → 4: eight rarities became four (<see cref="Rarities.FromLegacy"/>). Uniques are raised to Legendary on load,
    /// from content (<see cref="Rules.PlayerCharacter.Load"/>).</summary>
    private static void CollapseItemRarities(JsonObject save)
    {
        if (save["slots"] is not JsonObject slots) return;
        foreach (var (_, item) in slots)
            if (item is JsonObject o)
                o["rarity"] = (int)Rarities.FromLegacy(o["rarity"]?.GetValue<int>() ?? 0);
    }

    /// <summary>1 → 2: rolled stats gained a slot index. Old ones are marked unknown (-1); <see cref="Rules.PlayerCharacter.Load"/>
    /// infers each from the base item's stat pools, which a save alone cannot.</summary>
    private static void MarkStatSlotsUnknown(JsonObject save)
    {
        if (save["slots"] is not JsonObject slots) return;
        foreach (var (_, item) in slots)
            if (item?["stats"] is JsonArray stats)
                foreach (var stat in stats)
                    if (stat is JsonObject s) s["slot"] = -1;
    }

    /// <summary>Perks deleted from the tree because they cannot work in Sons of the Forest (docs/mechanics.md Deviations).
    /// A save that bought one drops it; spent points are derived from the bought perks, so the points come back.</summary>
    public static readonly IReadOnlyList<string> DeletedPerks = new[]
    {
        "perk.charge_pushback", // needs a heavy-attack signal the game does not give
    };

    private static void RemoveDeletedPerks(JsonObject save)
    {
        if (save["perks"] is not JsonObject perks) return;
        foreach (var id in DeletedPerks) perks.Remove(id);
    }

    /// <summary>The crafting perks, removed with crafting (COS-145). They cost 0 points, so nothing is refunded.</summary>
    public static readonly IReadOnlyList<string> CraftingPerks = new[] { "perk.rerolling", "perk.reforging", "perk.polishing", "perk.empowering", "perk.rerolling_individual_stats" };

    /// <summary>Materials and upgrade items removed with crafting (COS-145): socket gems, the drill, enzymes, acids, solutions,
    /// mixtures, waters and the island materials.</summary>
    public static readonly IReadOnlyList<string> DeletedItems = new[]
    {
        "item.crude_feather",
        "item.soft_feather",
        "item.ornate_feather",
        "item.wonderful_feather",
        "item.white_crow_s_feather",
        "item.reef_shark",
        "item.tiger_shark_tooth",
        "item.whale_shark_tooth",
        "item.great_white_shark_tooth",
        "item.megalodon_s_tooth",
        "item.uncut_sapphire",
        "item.clear_sapphire",
        "item.shiny_sapphire",
        "item.enchanted_sapphire",
        "item.celestial_sapphire",
        "item.uncut_moonstone",
        "item.clear_moonstone",
        "item.shiny_moonstone",
        "item.enchanted_moonstone",
        "item.celestial_moonstone",
        "item.lead_ore",
        "item.vanadium_ore",
        "item.titanium_ore",
        "item.chromium_ore",
        "item.tungsten_ore",
        "item.socket_drill",
        "item.enzyme_str_34",
        "item.enzyme_int_33",
        "item.enzyme_agi_39",
        "item.enzyme_vit_449",
        "item.stomach_acid",
        "item.elite_stomach_acid",
        "item.crimson_solution",
        "item.weak_armor_hardening_mixture",
        "item.upgraded_armor_hardening_mixture",
        "item.chaos_water",
        "item.upgraded_chaos_water",
        "item.golden_tooth",
        "item.printer_filament_spool",
        "item.mutant_marrow",
        "item.cube_shard",
    };

    /// <summary>4 → 5: crafting, materials and sockets are gone (COS-145). Drops the crafting perks, every saved stack of a deleted
    /// item, and socket stats (filled or empty) from the remaining items.</summary>
    private static void RemoveCraftingContent(JsonObject save)
    {
        if (save["perks"] is JsonObject perks)
            foreach (var id in CraftingPerks) perks.Remove(id);
        if (save["slots"] is not JsonObject slots) return;
        var deleted = new HashSet<string>(DeletedItems, StringComparer.Ordinal);
        foreach (var key in slots.Select(kv => kv.Key).ToList())
        {
            if (slots[key] is not JsonObject item) continue;
            if (item["baseId"]?.GetValue<string>() is { } baseId && deleted.Contains(baseId)) { slots.Remove(key); continue; }
            if (item["stats"] is not JsonArray stats) continue;
            for (int i = stats.Count - 1; i >= 0; i--)
                if (stats[i]?["id"]?.GetValue<string>() is { } statId && Rules.LootRoller.IsSocketStat(statId)) stats.RemoveAt(i);
        }
    }

    private static readonly Action<JsonObject>[] WorldMigrations =
    {
        CollapseMinLootRarity, // 1 → 2 (COS-109)
        DropDifficultyTier, // 2 → 3 (COS-130)
        AddLootLevel, // 3 → 4 (COS-77)
    };

    /// <summary>COS-77: the loot level policy is saved with the world; older worlds used the default, Highest.</summary>
    private static void AddLootLevel(JsonObject save)
    {
        if (save["lootLevel"] is null) save["lootLevel"] = nameof(Rules.LootLevelPolicy.Highest);
    }

    /// <summary>
    /// COS-130: worlds no longer store a difficulty tier; enemy levels follow the party and the game's difficulty. The old tier is
    /// dropped, not turned into extra levels: its fixed levels do not translate into an offset over the party (a Master world's
    /// level 30-39 enemies are what a level-30 party now meets anyway). The host's own extra levels are kept.
    /// </summary>
    private static void DropDifficultyTier(JsonObject save) => save.Remove("difficulty");

    private static void CollapseMinLootRarity(JsonObject save)
    {
        if (save["minLootRarity"] is JsonNode n) save["minLootRarity"] = (int)Rarities.FromLegacy(n.GetValue<int>());
    }

    public static string Write(PlayerSave save)
    {
        save.Version = PlayerSave.CurrentVersion;
        return JsonSerializer.Serialize(save, Options);
    }

    public static string Write(WorldSave save)
    {
        save.Version = WorldSave.CurrentVersion;
        return JsonSerializer.Serialize(save, Options);
    }

    public static PlayerSave ReadPlayer(string json) =>
        Read<PlayerSave>(json, PlayerSave.CurrentVersion, PlayerMigrations);

    public static WorldSave ReadWorld(string json) =>
        Read<WorldSave>(json, WorldSave.CurrentVersion, WorldMigrations);

    private static T Read<T>(string json, int current, Action<JsonObject>[] migrations) where T : class
    {
        JsonObject obj;
        try
        {
            obj = JsonNode.Parse(json) as JsonObject ?? throw new SaveFormatException("Save is not a JSON object");
        }
        catch (JsonException e)
        {
            throw new SaveFormatException("Save is not valid JSON: " + e.Message);
        }

        int version = obj["version"]?.GetValue<int>() ?? 1;
        if (version > current) throw new SaveVersionException(version, current);
        if (version < 1) throw new SaveFormatException($"Invalid save version {version}");

        for (int v = version; v < current; v++)
        {
            migrations[v - 1](obj);
            obj["version"] = v + 1;
        }

        try
        {
            return obj.Deserialize<T>(Options) ?? throw new SaveFormatException("Save deserialized to null");
        }
        catch (JsonException e)
        {
            throw new SaveFormatException("Save has an unexpected shape: " + e.Message);
        }
    }
}

public class SaveFormatException : Exception
{
    public SaveFormatException(string message) : base(message) { }
}

/// <summary>The save was written by a newer mod version.</summary>
public sealed class SaveVersionException : SaveFormatException
{
    public int Found { get; }

    public SaveVersionException(int found, int supported)
        : base($"Save version {found} is newer than this mod supports ({supported}). Update the mod.")
    {
        Found = found;
    }
}
