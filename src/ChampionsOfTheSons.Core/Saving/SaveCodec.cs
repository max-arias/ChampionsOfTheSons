using System.Text.Json;
using System.Text.Json.Nodes;

namespace ChampionsOfTheSons.Core.Saving;

/// <summary>
/// JSON (de)serialization of the save DTOs. Before release the format stays at version 1 (Max, 2026-11-23: the mod never
/// released, so nobody's save can break): new fields default when missing, removed ones are ignored, and the migration chain
/// is gone. Dev builds once wrote higher numbers (up to <see cref="PlayerSave.LastDevVersion"/> / <see cref="WorldSave.LastDevVersion"/>);
/// those read as version 1. Anything above is refused with <see cref="SaveVersionException"/>, so an old mod never silently
/// drops data it doesn't know. After release: bump the version and add a migration for every format change.
/// </summary>
public static class SaveCodec
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

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
        Read<PlayerSave>(json, PlayerSave.LastDevVersion);

    public static WorldSave ReadWorld(string json) =>
        Read<WorldSave>(json, WorldSave.LastDevVersion);

    private static T Read<T>(string json, int newestKnown) where T : class
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
        if (version > newestKnown) throw new SaveVersionException(version, newestKnown);
        if (version < 1) throw new SaveFormatException($"Invalid save version {version}");
        obj["version"] = 1; // every pre-release number is the same format: version 1

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
