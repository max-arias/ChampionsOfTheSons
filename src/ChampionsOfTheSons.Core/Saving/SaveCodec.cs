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
        RemoveDeletedPerks, // 1 → 2 (COS-86/COS-87)
    };

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

    private static readonly Action<JsonObject>[] WorldMigrations = Array.Empty<Action<JsonObject>>();

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
