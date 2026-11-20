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
    };

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

    private static readonly Action<JsonObject>[] WorldMigrations =
    {
        CollapseMinLootRarity, // 1 → 2 (COS-109)
        DropDifficultyTier, // 2 → 3 (COS-130)
    };

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
    public int Supported { get; }

    public SaveVersionException(int found, int supported)
        : base($"Save version {found} is newer than this mod supports ({supported}). Update the mod.")
    {
        Found = found;
        Supported = supported;
    }
}
