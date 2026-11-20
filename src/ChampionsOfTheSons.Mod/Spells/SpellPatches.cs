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
        if (SpellWorld.Parry.TryParry() || ParryOnHit())
        {
            SpellEffects.Riposte();
            return 0;
        }
        return (float)SpellWorld.Shield.Absorb(damage);
    }

    /// <summary>Alexander's Shield (COS-88): a hit taken can trigger Parry by itself, with Parry equipped and ready; it then cools down as if cast.</summary>
    private static bool ParryOnHit()
    {
        var pc = SpellWorld.Pc;
        if (!SpellWorld.Stats.IsOn(Core.Rules.PerkStatIds.ParryOnHit) || UnityEngine.Random.value >= Core.Rules.PerkRules.ParryOnHitChance) return false;
        for (int slot = 0; slot < Core.Rules.SpellBook.Slots; slot++)
            if (pc.Spells.Loadout[slot] == "spell.parry" && pc.Spells.IsReady(slot))
            {
                pc.Spells.TryCast(slot, double.MaxValue, SpellWorld.Stats, out _);
                return true;
            }
        return false;
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
