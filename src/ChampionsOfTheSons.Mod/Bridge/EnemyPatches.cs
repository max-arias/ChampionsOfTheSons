using ChampionsOfTheSons.Core;
using ChampionsOfTheSons.Core.Rules;
using Bolt;
using HarmonyLib;
using RedLoader;
using Sons.Ai.Vail;
using Sons.Ai.Vail.StimuliTypes;
using Sons.StatSystem;
using UnityEngine;

namespace ChampionsOfTheSons.Mod.Bridge;

/// <summary>
/// Enemy hooks (host / single-player). Hook points verified against the game's interop assemblies (2026-11-18):
/// spawn → VailActor.SetMaxHealthStat, player damage → VailActor.ReceivedDamage, death → VailWorldSimulation.OnActorDied,
/// enemy melee → Sons.Gameplay.MeleeWeapon.GetNewImpactData.
/// </summary>
[HarmonyPatch]
public static class EnemyPatches
{
    private static readonly Rng Rng = Rng.FromTime();

    /// <summary>Last hit, for damage numbers and self-tests.</summary>
    public static (double Before, double After, bool Crit)? LastHit { get; private set; }

    public static event Action<VailActor, EnemyRuntime, double, bool>? EnemyHit;
    public static event Action<EnemyRuntime, double>? EnemyKilled;

    [HarmonyPatch(typeof(VailActor), nameof(VailActor.SetMaxHealthStat))]
    [HarmonyPostfix]
    private static void AfterSetMaxHealth(VailActor __instance, StatsManager statsManager)
    {
        try
        {
            if (!EnemyRegistry.IsAuthority || statsManager == null || __instance == null) return;
            var kind = EnemyRegistry.KindFor(__instance.TypeId);
            if (kind == null) return;
            var health = EnemyRegistry.Health(statsManager);
            if (health == null) return;

            float vanillaMax = health.GetMax();
            if (vanillaMax <= 0) return;
            float ratio = Mathf.Clamp01(health._currentValue / vanillaMax);
            var runtime = EnemyRegistry.GetOrRoll(statsManager, kind, vanillaMax);
            float newMax = (float)runtime.Profile.MaxHealth;
            health.SetMax(newMax);
            health.SetCurrentValue(ratio * newMax);
        }
        catch (Exception e)
        {
            RLog.Error($"Champions of the Sons: enemy scaling failed: {e}");
        }
    }

    [HarmonyPatch(typeof(VailActor), nameof(VailActor.ReceivedDamage))]
    [HarmonyPrefix]
    private static void BeforeReceivedDamage(VailActor __instance, ImpactType impactType, MonoBehaviourStimuli sourceStimuli, ref float amount, bool fromPlayer)
    {
        try
        {
            if (!EnemyRegistry.IsAuthority || amount <= 0) return;
            var runtime = EnemyRegistry.Get(__instance);
            if (runtime == null) return;

            bool local = fromPlayer && IsLocalPlayer(sourceStimuli);
            OutgoingHit hit;
            if (local)
            {
                var weapon = impactType switch
                {
                    ImpactType.Projectile => WeaponClass.Ranged,
                    ImpactType.Explosion => WeaponClass.Thrown,
                    _ => WeaponClass.Melee,
                };
                hit = PlayerAttack.Weapon(PlayerState.Character.Stats, weapon, amount, Rng);
            }
            else
            {
                // A remote player's client already applied their own stats (COS-13); traps/environment: raw.
                hit = new OutgoingHit(amount, DamageKind.Physical, false, 0);
            }

            var (damage, reduction) = EnemyDefense.Apply(runtime.Profile, runtime.ArmorReduction, hit);
            runtime.ArmorReduction = reduction;
            runtime.LastHitByLocalPlayer = local;
            runtime.LastHitWasCrit = hit.Crit;
            LastHit = (amount, damage, hit.Crit);
            amount = (float)damage;
            EnemyHit?.Invoke(__instance, runtime, damage, hit.Crit);
        }
        catch (Exception e)
        {
            RLog.Error($"Champions of the Sons: damage hook failed: {e}");
        }
    }

    [HarmonyPatch(typeof(VailWorldSimulation), nameof(VailWorldSimulation.OnActorDied))]
    [HarmonyPostfix]
    private static void AfterActorDied(WorldSimActor deadActor, bool killedByPlayer)
    {
        try
        {
            if (!EnemyRegistry.IsAuthority || deadActor == null) return;
            var stats = deadActor.StatManager;
            var runtime = EnemyRegistry.Get(stats);
            if (SelfTest.Enabled) SelfTest.Log($"OnActorDied {deadActor.TypeId} killedByPlayer={killedByPlayer} tracked={runtime != null}");
            if (runtime == null) return;
            EnemyRegistry.Remove(stats);
            // The game's flag misses some player kills (e.g. deaths finished by its own state machine); our hook saw the last hit.
            if (!killedByPlayer && !runtime.LastHitByLocalPlayer) return;

            double xp = EnemyScaler.KillXpShare(runtime.Profile.Bounty, 1); // co-op split: COS-13
            PlayerState.Character.OnKill(xp);
            EnemyKilled?.Invoke(runtime, xp);
        }
        catch (Exception e)
        {
            RLog.Error($"Champions of the Sons: death hook failed: {e}");
        }
    }

    [HarmonyPatch(typeof(Sons.Gameplay.MeleeWeapon), nameof(Sons.Gameplay.MeleeWeapon.GetNewImpactData))]
    [HarmonyPostfix]
    private static void AfterEnemyMeleeImpact(Sons.Gameplay.MeleeWeapon __instance, MeleeImpactData __result)
    {
        try
        {
            if (!EnemyRegistry.IsAuthority || __result == null) return;
            var runtime = EnemyRegistry.Get(__instance._ownerActor);
            if (runtime == null) return;
            float scaled = (float)EnemyScaler.DamageToPlayer(runtime.Profile, __result._originalRawDamage);
            float factor = __result._originalRawDamage > 0 ? scaled / __result._originalRawDamage : 1;
            __result._originalRawDamage = scaled;
            __result._remainingRawDamage *= factor;
        }
        catch (Exception e)
        {
            RLog.Error($"Champions of the Sons: enemy melee hook failed: {e}");
        }
    }

    private static bool IsLocalPlayer(MonoBehaviourStimuli? stimuli)
    {
        if (!BoltNetwork.isRunning) return true; // single-player: every player hit is ours
        var player = stimuli?.TryCast<PlayerStimuli>();
        return player != null && player.IsLocalPlayer();
    }
}
