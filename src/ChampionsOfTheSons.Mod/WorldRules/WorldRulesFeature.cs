using Bolt;
using ChampionsOfTheSons.Core.Networking;
using ChampionsOfTheSons.Core.Rules;
using ChampionsOfTheSons.Mod.Bridge;
using ChampionsOfTheSons.Mod.Net;
using RedLoader;
using SonsSdk;
using UnityEngine;

namespace ChampionsOfTheSons.Mod.HostRules;

/// <summary>
/// COS-11 / COS-130: the host settings panel and its keybind (in RedLoader's mod settings), the game's difficulty read from the
/// game by the host, and the host → client sync of both. There is no difficulty picker: enemy levels follow the party
/// (<see cref="EnemyLevels"/>) plus the game's difficulty and the host's extra levels.
/// </summary>
public sealed class WorldRulesFeature : IModFeature
{
    internal static HostSettingsPanel Settings { get; private set; } = null!;

    public static KeybindConfigEntry HostSettingsKey { get; private set; } = null!;

    private static float _syncTimer;

    public static bool PlayerControllable
    {
        get
        {
            try
            {
                return GameState.IsPlayerControllable;
            }
            catch
            {
                return false;
            }
        }
    }

    public void Init()
    {
        Settings = new HostSettingsPanel();
        Settings.Build();

        HostSettingsKey = Config.Category.CreateKeybindEntry("host_settings_key", "o", "Host settings", "Open the host settings (extra levels, multipliers, enemy abilities).");
        HostSettingsKey.SetScope(needsPlayerControllable: true);
        HostSettingsKey.Notify(OpenSettings);

        CotsNetEvent.Received += OnNet;
    }

    public void OnGameStart()
    {
        _syncTimer = 0;
        ReadGameDifficulty();
    }

    public void Update(float dt, bool inWorld)
    {
        if (Settings == null) return;

        // Close with Escape; the panel takes the cursor while open.
        if (Settings.IsOpen && Input.GetKeyDown(KeyCode.Escape)) Settings.Close();
        else if (Settings.IsOpen && HostSettingsKey.GetAction().WasPressedThisFrame()) Settings.Close();

        // The host re-reads the game's difficulty and repeats its settings now and then, so players who join later get them too.
        if (!inWorld || !EnemyRegistry.IsAuthority) return;
        _syncTimer -= dt;
        if (_syncTimer > 0) return;
        _syncTimer = 10;
        ReadGameDifficulty();
        Broadcast();
    }

    /// <summary>Host only: takes the game's current difficulty into the world rules.</summary>
    public static void ReadGameDifficulty()
    {
        if (!EnemyRegistry.IsAuthority) return;
        var game = GameDifficultyReader.Read();
        if (game == WorldState.Rules.GameDifficulty) return;
        WorldState.SetGameDifficulty(game);
        RLog.Msg($"Champions of the Sons: game difficulty {game.Mode} (+{game.LevelOffset} enemy levels, {game.EliteChance:P0} elites)");
    }

    /// <summary>What the host sends: the game's difficulty, its extra levels and friendly fire.</summary>
    public static WorldSettings CurrentSettings()
    {
        var r = WorldState.Rules;
        return new WorldSettings(r.GameDifficulty.Mode, r.GameDifficulty.LevelOffset, r.ExtraEnemyLevels, WorldState.FriendlyFire);
    }

    private static void OpenSettings()
    {
        if (!EnemyRegistry.IsAuthority || Settings.IsOpen) return;
        Settings.Open();
    }

    /// <summary>Sends the host's settings to every client at once (after a change in host settings).</summary>
    public static void Broadcast()
    {
        try
        {
            if (!BoltNetwork.isRunning || !BoltNetwork.isServer) return;
            CotsNetEvent.Instance.Send(CurrentSettings(), GlobalTargets.Others);
        }
        catch (Exception e)
        {
            RLog.Error($"Champions of the Sons: could not send world settings: {e.Message}");
        }
    }

    /// <summary>Client: takes the host's settings.</summary>
    public static void Apply(WorldSettings ws)
    {
        WorldState.SetGameDifficulty(new GameDifficulty(ws.Mode, ws.LevelOffset));
        if (WorldState.Rules.ExtraEnemyLevels != ws.ExtraEnemyLevels)
        {
            WorldState.Update(r => r with { ExtraEnemyLevels = ws.ExtraEnemyLevels });
            WorldState.NotifyChanged();
        }
    }

    private static void OnNet(IMessage message, BoltConnection from)
    {
        if (message is not WorldSettings ws || EnemyRegistry.IsAuthority) return;
        Apply(ws);
    }

    public void RegisterSelfTests() => WorldRulesSelfTest.Register();
}
