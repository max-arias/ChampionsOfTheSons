using ChampionsOfTheSons.Core.Networking;
using ChampionsOfTheSons.Core.Rules;
using RedLoader;
using Sons.Multiplayer;
using SonsSdk;

namespace ChampionsOfTheSons.Mod.Net;

/// <summary>
/// COS-15: a dedicated server has nobody at the host settings panel, so it reads its world rules from
/// <c>UserData/ChampionsOfTheSonsServer.cfg</c> (created with defaults on first start) and applies them when the world loads.
/// The game's difficulty comes from the server's own game mode (COS-130); clients take whatever the host's <see cref="WorldSettings"/> says.
/// </summary>
public sealed class ServerConfig : IModFeature
{
    private static ConfigEntry<int>? _extraLevels;
    private static ConfigEntry<bool>? _friendlyFire;
    private static ConfigEntry<string>? _lootLevel;

    /// <summary>True when this process is a dedicated server.</summary>
    public static bool IsDedicatedServer
    {
        get
        {
            try { return GameServerManager.IsDedicatedServer; }
            catch { return false; }
        }
    }

    /// <summary>True in the dedicated server's process (SonsOfTheForestDS), known from the start and cheap: for per-frame player patches,
    /// which must not run there (no local player stats, content may not be loaded yet).</summary>
    public static readonly bool OnDedicatedServer =
        System.Diagnostics.Process.GetCurrentProcess().ProcessName.StartsWith("SonsOfTheForestDS", StringComparison.OrdinalIgnoreCase);

    public void Init()
    {
        var category = ConfigSystem.CreateFileCategory("ChampionsOfTheSonsServer", "Champions of the Sons (dedicated server)", "ChampionsOfTheSonsServer.cfg");
        _extraLevels = category.CreateEntry("extra_enemy_levels", 0, "Extra enemy levels",
            "Levels added to every enemy on top of the players' level and the game's difficulty (0-100).");
        _friendlyFire = category.CreateEntry("friendly_fire", false, "Friendly fire", "Players can hurt each other.");
        _lootLevel = category.CreateEntry("loot_level_policy", LootLevelPolicy.Highest.ToString(), "Loot level",
            "Level of dropped items: " + string.Join(", ", Enum.GetNames<LootLevelPolicy>()));
    }

    public void OnGameStart()
    {
        if (!IsDedicatedServer) return;
        Apply();
    }

    /// <summary>Applies the config to the world rules (dedicated server start).</summary>
    public static void Apply()
    {
        int extra = Math.Clamp(_extraLevels?.Value ?? 0, 0, 100);
        WorldState.Update(r => r with { ExtraEnemyLevels = extra });
        WorldState.SetFriendlyFire(_friendlyFire?.Value ?? false);
        if (Enum.TryParse<LootLevelPolicy>(_lootLevel?.Value, true, out var policy))
            WorldState.Update(r => r with { LootLevel = policy });
        RLog.Msg($"{CoopTestJoin.Tag} server config applied: extra enemy levels {extra}, game difficulty {WorldState.Rules.GameDifficulty.Mode}, friendly fire {WorldState.FriendlyFire}, loot level {WorldState.Rules.LootLevel}");
    }
}
