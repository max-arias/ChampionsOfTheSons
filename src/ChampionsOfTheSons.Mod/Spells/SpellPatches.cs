using HarmonyLib;
using RedLoader;
using TheForest.Utils;

namespace ChampionsOfTheSons.Mod.Spells;

/// <summary>Incoming damage on the local player: Parry negates a hit, Sustain Shield soaks what is left. Runs after the stat-sheet defenses.</summary>
[HarmonyPatch]
internal static class SpellPatches
{
    /// <summary>Applies the spells to one incoming hit and returns what gets through.</summary>
    public static float Incoming(float damage)
    {
        if (damage <= 0) return damage;
        if (SpellWorld.Parry.TryParry())
        {
            SpellEffects.Riposte();
            return 0;
        }
        return (float)SpellWorld.Shield.Absorb(damage);
    }

    [HarmonyPatch(typeof(PlayerStats), nameof(PlayerStats.Hit))]
    [HarmonyPriority(Priority.Low)]
    [HarmonyPrefix]
    private static void BeforePlayerHit(ref float damage)
    {
        try
        {
            damage = Incoming(damage);
        }
        catch (Exception e)
        {
            RLog.Error($"Champions of the Sons: spell defense hook failed: {e}");
        }
    }
}
