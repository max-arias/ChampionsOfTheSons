using ChampionsOfTheSons.Core;
using ChampionsOfTheSons.Core.Rules;
using Bolt;
using HarmonyLib;
using RedLoader;
using Sons.Ai.Vail;
using Sons.Ai.Vail.StimuliTypes;
using Sons.StatSystem;
using Sons.Weapon;
using TheForest.Utils;
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

    /// <summary>Damage kind of the hit being reported by <see cref="EnemyHit"/> (damage numbers color).</summary>
    public static DamageKind LastHitKind { get; private set; }

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
            var runtime = EnemyRegistry.GetOrRoll(statsManager, kind, vanillaMax, __instance);
            float newMax = (float)runtime.Profile.MaxHealth;
            health.SetMax(newMax);
            health.SetCurrentValue(ratio * newMax);
            EnemyAbilityFeature.Prepare(runtime);
        }
        catch (Exception e)
        {
            RLog.Error($"Champions of the Sons: enemy scaling failed: {e}");
        }
    }

    /// <summary>Applies damage the bridge itself deals to an enemy (bleed, fire, thorns) straight to its health, through the same
    /// shield, armor, cap and Undead rules as a player hit. <paramref name="creditPlayer"/>: a kill counts for the local player.</summary>
    public static void DealForced(EnemyRuntime runtime, OutgoingHit hit, bool creditPlayer)
    {
        var health = runtime.Stats == null ? null : EnemyRegistry.Health(runtime.Stats);
        if (health == null || health._currentValue <= 0) return;
        double damage = Mitigate(runtime, health, hit, hit.Amount, creditPlayer, forced: true);
        if (damage <= 0) return;
        float left = health._currentValue - (float)damage;
        health.SetCurrentValue(Mathf.Max(0, left));
        // Setting health alone doesn't start the death sequence (the game's damage caller does).
        if (left <= 0 && runtime.Actor != null && !runtime.Actor.IsDead()) runtime.Actor.OnDeath();
    }

    /// <summary>Shield, armor, damage-taken, steadfast cap and Undead for one hit; returns the damage that lands.</summary>
    private static double Mitigate(EnemyRuntime runtime, HealthStat? health, OutgoingHit hit, double rawAmount, bool local, bool forced)
    {
        var combat = runtime.Combat;
        if (combat.AbsorbsHit())
        {
            LastHit = (rawAmount, 0, hit.Crit);
            return 0;
        }

        var (damage, reduction) = EnemyDefense.Apply(runtime.Profile, runtime.ArmorReduction, hit, combat.DamageTakenMultiplier, combat.ArmorMultiplier);
        runtime.ArmorReduction = reduction;
        if (local)
        {
            runtime.LastHitByLocalPlayer = true;
            runtime.LastHitWasCrit = hit.Crit;
        }

        // Undead: the first lethal hit leaves it at half health instead.
        if (health != null && damage >= health._currentValue && health._currentValue > 0 && combat.TryUndeadRevive())
        {
            health.SetCurrentValue((float)(runtime.Profile.MaxHealth * AbilityTuning.UndeadReviveFraction));
            damage = 0;
            runtime.Revived = true;
        }

        LastHit = (rawAmount, damage, hit.Crit);
        if (damage > 0 && local && !forced) OnPlayerHit(runtime, damage);
        LastHitKind = hit.Kind;
        if (runtime.Actor != null) EnemyHit?.Invoke(runtime.Actor, runtime, damage, hit.Crit);
        return damage;
    }

    [HarmonyPatch(typeof(VailActor), nameof(VailActor.ReceivedDamage))]
    [HarmonyPrefix]
    private static void BeforeReceivedDamage(VailActor __instance, object[] __args, ImpactType impactType, MonoBehaviourStimuli sourceStimuli, ref float amount, bool fromPlayer)
    {
        try
        {
            if (!EnemyRegistry.IsAuthority || amount <= 0) return;
            var runtime = EnemyRegistry.Get(__instance);
            if (runtime == null) return;
            runtime.Actor = __instance;

            bool local;
            OutgoingHit hit;
            if (fromPlayer && IsLocalPlayer(sourceStimuli))
            {
                local = true;
                var sheet = PlayerState.Character.Stats;
                var weapon = impactType switch
                {
                    ImpactType.Projectile => WeaponClass.Ranged,
                    ImpactType.Explosion => WeaponClass.Thrown,
                    _ => WeaponClass.Melee,
                };
                bool head = weapon == WeaponClass.Ranged && IsHead(__args.Length > 0 ? __args[0] as DamageNode : null);
                hit = PlayerAttack.Weapon(sheet, weapon, amount, Rng, headshot: head);
                if (weapon == WeaponClass.Thrown) hit = hit with { Amount = hit.Amount * (1 + sheet.Get(CombatStatIds.ExplosionDamage)) * sheet.Get(CombatStatIds.ThrownSpearDamage) };
                double fire = sheet.Get(StatIds.FireDamage);
                if (fire > 0 && __instance.IsBodyBurning()) hit = hit with { Amount = hit.Amount * (1 + fire) };
                hit = Followups.FollowupsFeature.OnLocalHit(hit, weapon);
            }
            else
            {
                // A remote player's hit arrives as a Hit message with their own stats (COS-13): the vanilla one is dropped.
                if (fromPlayer && Net.CoopHits.ReplacesVanillaRemoteHits) { amount = 0; return; }
                local = false;
                hit = new OutgoingHit(amount, DamageKind.Physical, false, 0);
            }

            amount = (float)Mitigate(runtime, EnemyRegistry.HealthOf(__instance), hit, amount, local, forced: false);
        }
        catch (Exception e)
        {
            RLog.Error($"Champions of the Sons: damage hook failed: {e}");
        }
    }

    /// <summary>Everything a landed player hit triggers: life/energy on hit, leech, slow, bleed, weaken, stun.</summary>
    private static void OnPlayerHit(EnemyRuntime runtime, double damage)
    {
        var character = PlayerState.Character;
        var procs = PlayerHitEffects.Roll(character.Stats, damage, Rng);
        PlayerEffects.Heal(procs.Life + UniqueItems.LifeStealFraction(character.Inventory) * damage);
        PlayerEffects.AddEnergy(procs.Energy);
        var combat = runtime.Combat;
        if (procs.Slow) combat.ApplySlow("player.slow", AbilityTuning.SlowAmount, AbilityTuning.SlowSeconds);
        if (procs.Bleed) combat.ApplyDot("player.bleed", DotKind.Bleed, procs.BleedPerSecond, AbilityTuning.BleedSeconds);
        if (procs.Weaken) combat.ApplyWeaken("player.weaken", AbilityTuning.WeakenMult, AbilityTuning.WeakenSeconds);
        if (procs.StunSeconds > 0) combat.ApplyStun(procs.StunSeconds);
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
            EnemyAbilityFeature.OnEnemyDied(runtime);
            // The game's flag misses some player kills (e.g. deaths finished by its own state machine); our hook saw the last hit.
            if (!killedByPlayer && !runtime.LastHitByLocalPlayer && !Net.CoopHits.HitByRemote(runtime)) return;

            double xp = Net.CoopHits.DistributeKillXp(runtime); // co-op: players in range share it (COS-13)
            if (xp > 0 || !(Net.CoopSession.IsHost && Net.CoopFeature.Verified.Count > 0)) PlayerState.Character.OnKill(xp);
            double killHeal = UniqueItems.KillHealFraction(PlayerState.Character.Inventory);
            if (killHeal > 0 && LocalPlayer.Vitals?.Health != null) PlayerEffects.Heal(killHeal * LocalPlayer.Vitals.Health.GetMax());
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
            if (runtime == null || !runtime.IsActive) return;
            float scaled = (float)EnemyScaler.DamageToPlayer(runtime.Profile, __result._originalRawDamage, runtime.Combat.DamageDealtMultiplier);
            float factor = __result._originalRawDamage > 0 ? scaled / __result._originalRawDamage : 1;
            __result._originalRawDamage = scaled;
            __result._remainingRawDamage *= factor;
            PlayerEffects.LastAttacker = (runtime, Time.time);
        }
        catch (Exception e)
        {
            RLog.Error($"Champions of the Sons: enemy melee hook failed: {e}");
        }
    }

    private static bool IsHead(DamageNode? node)
    {
        if (node == null) return false;
        try
        {
            return node.gameObject.name.IndexOf("head", StringComparison.OrdinalIgnoreCase) >= 0;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsLocalPlayer(MonoBehaviourStimuli? stimuli)
    {
        if (!BoltNetwork.isRunning) return true; // single-player: every player hit is ours
        var player = stimuli?.TryCast<PlayerStimuli>();
        return player != null && player.IsLocalPlayer();
    }
}
