using ChampionsOfTheSons.Core.Rules;
using ChampionsOfTheSons.Mod.Bridge;
using ChampionsOfTheSons.Mod.Spells;
using RedLoader;
using TheForest.Utils;

namespace ChampionsOfTheSons.Mod.Death;

/// <summary>
/// COS-76: player death. Death itself stays the base game's (no item drops, the game's downed and respawn rules);
/// on top, the moment the local player dies the Core death rule runs (<see cref="DeathPenalty"/>: part of the level's XP
/// and the pending massacre streak are lost), spell buffs and running spells end and debuffs are cleared.
/// Every machine watches its own player, so this works the same for the host, a client and single player; each player's
/// penalty applies only to their own character.
/// </summary>
public sealed class DeathFeature : IModFeature
{
    private static bool _wasDead;

    /// <summary>Deaths seen this session (self-tests).</summary>
    public static int Deaths { get; private set; }

    public static double LastXpLost { get; private set; }

    public void OnGameStart() => _wasDead = false;

    public void Update(float dt, bool inWorld)
    {
        bool dead = IsDeadNow();
        if (dead && !_wasDead) HandleDeath();
        _wasDead = dead;
    }

    /// <summary>
    /// The game's death flag, or (single player, which has no downed state) health at zero: the game sets its flag from a
    /// death routine that can lag behind. In co-op a player at zero health may only be downed, which is not a death.
    /// </summary>
    private static bool IsDeadNow()
    {
        try
        {
            var stats = LocalPlayer.Stats;
            if (stats == null) return false;
            if (stats.IsDead()) return true;
            var health = LocalPlayer.Vitals?.Health;
            return !Net.CoopSession.Active && LocalPlayer.IsInWorld && health != null && health._currentValue <= 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Runs the death rule on the local character. Returns the XP lost.</summary>
    public static double HandleDeath()
    {
        var pc = PlayerState.Character;
        for (int slot = 0; slot < SpellBook.Slots; slot++)
            if (pc.Spells.IsActive(slot)) pc.Spells.Stop(slot);
        SpellWorld.Reset();
        PlayerEffects.ClearDebuffs();
        double lost = pc.OnDeath();
        Deaths++;
        LastXpLost = lost;
        RLog.Msg($"Champions of the Sons: player died at level {pc.Progress.Level}, lost {lost:0} XP (bar now {pc.Progress.Xp:0})");
        return lost;
    }

    public void RegisterSelfTests() => DeathSelfTest.Register();
}
