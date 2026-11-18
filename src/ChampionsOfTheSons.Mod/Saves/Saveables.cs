using ChampionsOfTheSons.Core.Saving;
using RedLoader;
using SonsSdk;

namespace ChampionsOfTheSons.Mod.Saves;

/// <summary>What RedLoader stores for us: our own versioned JSON, so the format is ours to migrate.</summary>
public class SaveBlob
{
    public string Json = "";
}

/// <summary>Per-player progress. Included in multiplayer client saves so each player keeps their character.</summary>
public sealed class PlayerProgressSaveable : ICustomSaveable<SaveBlob>
{
    public string Name => "ChampionsOfTheSons.Player";
    public bool IncludeInPlayerSave => true;

    public SaveBlob Save() => new() { Json = SaveCodec.Write(PlayerState.ToSave()) };

    public void Load(SaveBlob obj)
    {
        try
        {
            PlayerState.FromSave(SaveCodec.ReadPlayer(obj.Json));
        }
        catch (SaveFormatException e)
        {
            // Never crash the game over our data: start fresh and say why.
            RLog.Error($"Champions of the Sons: could not load player progress ({e.Message}). Starting a new character.");
            PlayerState.FromSave(new PlayerSave());
        }
    }
}

/// <summary>World settings owned by the host (difficulty). Not part of client saves.</summary>
public sealed class WorldSettingsSaveable : ICustomSaveable<SaveBlob>
{
    public string Name => "ChampionsOfTheSons.World";
    public bool IncludeInPlayerSave => false;

    public SaveBlob Save() => new() { Json = SaveCodec.Write(WorldState.ToSave()) };

    public void Load(SaveBlob obj)
    {
        try
        {
            WorldState.FromSave(SaveCodec.ReadWorld(obj.Json));
        }
        catch (SaveFormatException e)
        {
            RLog.Error($"Champions of the Sons: could not load world settings ({e.Message}). Using defaults.");
            WorldState.FromSave(new WorldSave());
        }
    }
}
