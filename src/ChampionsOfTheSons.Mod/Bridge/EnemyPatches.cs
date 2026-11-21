using ChampionsOfTheSons.Core;
using ChampionsOfTheSons.Core.Rules;
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
    /// <summary>The local player's weapon hit an enemy (before damage is applied): item effects that react to hits by weapon class.</summary>
    public static event Action<EnemyRuntime, WeaponClass>? LocalWeaponHit;

    private static readonly int PerfAfterSetMaxHealth = Perf.Perf.Register("Hook.SetMaxHealth");

    [HarmonyPatch(typeof(VailActor), nameof(VailActor.SetMaxHealthStat))]
    [HarmonyPostfix]
    private static void AfterSetMaxHealth(VailActor __instance, StatsManager statsManager)
    {
        using var perf = Perf.Perf.Scope(PerfAfterSetMaxHealth);
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
        if (Net.CoopHits.ForwardsToHost)
        {
            // Co-op client: the enemy is the host's. Our spell, thorn and splash damage goes there (COS-13); the local
            // estimate only drives our own number. The host's health sync moves the mirror.
            Mitigate(runtime, health, hit, hit.Amount, creditPlayer, forced: true);
            Net.CoopHits.Forward(runtime, hit, Core.Networking.HitFlags.None);
            return;
        }
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
        if (damage > 0) runtime.Engaged = true;
        if (runtime.Actor != null) EnemyHit?.Invoke(runtime.Actor, runtime, damage, hit.Crit);
        return damage;
    }

    private static readonly int PerfBeforeReceivedDamage = Perf.Perf.Register("Hook.ReceivedDamage");

    [HarmonyPatch(typeof(VailActor), nameof(VailActor.ReceivedDamage))]
    [HarmonyPrefix]
    private static void BeforeReceivedDamage(VailActor __instance, object[] __args, ImpactType impactType, MonoBehaviourStimuli sourceStimuli, ref float amount, bool fromPlayer)
    {
        using var perf = Perf.Perf.Scope(PerfBeforeReceivedDamage);
        try
        {
            bool client = Net.CoopHits.ForwardsToHost;
            if ((!EnemyRegistry.IsAuthority && !client) || amount <= 0) return;
            var runtime = EnemyRegistry.Get(__instance);
            if (runtime == null) return;
            runtime.Actor = __instance;

            bool local;
            OutgoingHit hit;
            if (client && !(fromPlayer && IsLocalPlayer(sourceStimuli))) return; // the host owns every other hit on its enemies
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
                _extraHits = 0;
                _shot = null;
                if (weapon == WeaponClass.Ranged)
                {
                    bool head = IsHead(__args.Length > 0 ? __args[0] as DamageNode : null);
                    double distance = LocalPlayer.Transform != null ? Vector3.Distance(LocalPlayer.Transform.position, __instance.transform.position) : 0;
                    var shot = new RangedShot(PerkHooks.ShotTracker.KindForHit(), distance, head);
                    var ranged = RangedRules.Hit(sheet, WeaponTypes.RangedBaseDamage(Weapons.WeaponModels.Active, amount), shot, Rng);
                    hit = ranged.Hit;
                    _extraHits = ranged.ExtraHits;
                    _headshot = ranged.Headshot;
                    _shot = shot;
                }
                else
                {
                    // Weapon types (COS-93): the type's melee damage factor.
                    double baseDamage = weapon == WeaponClass.Melee ? amount * Weapons.WeaponModels.MeleeDamageFactor : amount;
                    hit = PlayerAttack.Weapon(sheet, weapon, baseDamage, Rng);
                }
                // Explosions (bombs, molotovs): COTF's fixed 40..850 by the game's falloff, times (1 + explosion damage), so a
                // bomb never one-shots a scaled enemy (COS-92). Thrown spears are projectiles and get their own multiplier.
                if (weapon == WeaponClass.Thrown)
                    hit = new OutgoingHit(Core.Rules.GameRules.ExplosionToEnemy(amount, Net.CoopParty.TeamExplosionDamage()), DamageKind.Physical, false, 0);
                if (weapon == WeaponClass.Melee) hit = hit with { Amount = hit.Amount * MeleeRules.FireDanceMultiplier(sheet, PerkHooks.PerkHooksFeature.SpellRunning("spell.black_flame")) };
                double fire = sheet.Get(StatIds.FireDamage) + runtime.Combat.FireMark;
                if (fire > 0 && __instance.IsBodyBurning()) hit = hit with { Amount = hit.Amount * (1 + fire) };
                hit = Followups.FollowupsFeature.OnLocalHit(hit, weapon);
                hit = Spells.SpellHits.Modify(runtime, weapon, _shot, weapon == WeaponClass.Ranged && _headshot, hit); // weapon-linked spells (COS-84)
                LocalWeaponHit?.Invoke(runtime, weapon);
                _hitWeapon = weapon;
            }
            else
            {
                // A remote player's hit arrives as a Hit message with their own stats (COS-13): the vanilla one is dropped.
                if (fromPlayer && Net.CoopHits.ReplacesVanillaRemoteHits) { amount = 0; return; }
                local = false;
                // COS-92: a trap hits for its fixed amount and an explosion for COTF's 40..850 instead of killing outright.
                double world = GameRules.GameRulesFeature.TrapHitOn(__instance) is { } trap ? trap
                    : impactType == ImpactType.Explosion ? Core.Rules.GameRules.ExplosionToEnemy(amount, 0) : amount;
                hit = new OutgoingHit(world, DamageKind.Physical, false, 0);
            }

            amount = (float)Mitigate(runtime, EnemyRegistry.HealthOf(__instance), hit, amount, local, forced: false);
            if (local && amount > 0 && _shot is { } landed) AfterRangedHit(runtime, hit, landed);
            if (client)
            {
                // The same pipeline as the host's own hits (melee factor, fire, follow-ups, headshots, procs, extra hits);
                // the hit itself goes to the host, which applies defense and owns health (COS-13).
                Net.CoopHits.Forward(runtime, hit, Net.CoopHits.WeaponFlag(_hitWeapon));
                amount = 0;
            }
        }
        catch (Exception e)
        {
            RLog.Error($"Champions of the Sons: damage hook failed: {e}");
        }
    }

    private static WeaponClass _hitWeapon;
    private static RangedShot? _shot;
    private static int _extraHits;
    private static bool _headshot;

    /// <summary>After a ranged hit landed: True Aim's extra hits, a second spear, and area damage around the target (COS-86).
    /// Each goes through the forced-hit path (armor, shields, caps) and procs nothing further.</summary>
    private static void AfterRangedHit(EnemyRuntime runtime, OutgoingHit hit, RangedShot shot)
    {
        var sheet = PlayerState.Character.Stats;
        int extra = _extraHits;
        if (Rng.Chance(RangedRules.SecondSpearChance(sheet, shot.Kind))) extra++;
        _shot = null;
        _extraHits = 0;
        for (int i = 0; i < extra && runtime.IsActive; i++) DealForced(runtime, hit, creditPlayer: true);

        bool headshot = _headshot;
        _headshot = false;
        if (runtime.Actor == null) return;
        var center = runtime.Actor.transform.position;
        Splash(runtime, center, RangedRules.AreaSplash(sheet, hit.Amount, Rng), sheet.Get(RangedStatIds.AreaRadius), hit.ArmorPierce);
        // Eruption Bow: a headshot splashes half its damage on enemies around the target.
        if (headshot && sheet.IsOn(UniqueFlagIds.Eruption))
            Splash(runtime, center, hit.Amount * UniqueTuning.EruptionFraction, UniqueTuning.EruptionRadius, hit.ArmorPierce);
    }

    private static void Splash(EnemyRuntime target, Vector3 center, double amount, double radius, double pierce)
    {
        if (amount <= 0 || radius <= 0) return;
        foreach (var other in Spells.SpellWorld.EnemiesWithin(center, (float)radius))
        {
            if (other == target) continue;
            DealForced(other, new OutgoingHit(amount, DamageKind.Physical, false, pierce), creditPlayer: true);
        }
    }

    /// <summary>Everything a landed player hit triggers: life/energy on hit, leech, slow, bleed, weaken, stun, stamina on hit, Smolder Mark.</summary>
    private static void OnPlayerHit(EnemyRuntime runtime, double damage)
    {
        var character = PlayerState.Character;
        if (_hitWeapon == WeaponClass.Melee) PlayerEffects.AddEnergy(MeleeRules.StaminaOnHit(character.Stats));
        double mark = PerkRules.SmolderMarkBonus(character.Stats, _hitWeapon != WeaponClass.Melee);
        if (mark > 0) runtime.Combat.ApplyFireMark(mark, PerkRules.SmolderSeconds);
        var procs = PlayerHitEffects.Roll(character.Stats, damage, Rng);
        PlayerEffects.Heal(procs.Life + UniqueItems.LifeStealFraction(character.Inventory) * damage);
        PlayerEffects.AddEnergy(procs.Energy);
        var combat = runtime.Combat;
        if (procs.Slow) combat.ApplySlow("player.slow", AbilityTuning.SlowAmount, AbilityTuning.SlowSeconds);
        if (procs.Bleed) combat.ApplyDot("player.bleed", DotKind.Bleed, procs.BleedPerSecond, AbilityTuning.BleedSeconds);
        if (procs.Weaken) combat.ApplyWeaken("player.weaken", AbilityTuning.WeakenMult, AbilityTuning.WeakenSeconds);
        if (procs.StunSeconds > 0) combat.ApplyStun(procs.StunSeconds);
        if (WeaponTypes.HitSlow(Weapons.WeaponModels.Active, character.Stats) is { } hammer) combat.ApplySlow("weapon.hammer", hammer.SpeedMult, hammer.Seconds);
    }

    private static readonly int PerfAfterActorDied = Perf.Perf.Register("Hook.ActorDied");

    [HarmonyPatch(typeof(VailWorldSimulation), nameof(VailWorldSimulation.OnActorDied))]
    [HarmonyPostfix]
    private static void AfterActorDied(WorldSimActor deadActor, bool killedByPlayer)
    {
        using var perf = Perf.Perf.Scope(PerfAfterActorDied);
        try
        {
            if (!EnemyRegistry.IsAuthority || deadActor == null) return;
            var stats = deadActor.StatManager;
            var runtime = EnemyRegistry.Get(stats);
            if (SelfTest.Enabled) SelfTest.Log($"OnActorDied {deadActor.TypeId} killedByPlayer={killedByPlayer} tracked={runtime != null}");
            if (runtime == null)
            {
                World.WorldXpFeature.OnActorDied(deadActor, killedByPlayer); // animals (COS-75)
                return;
            }
            EnemyRegistry.Remove(stats);
            EnemyAbilityFeature.OnEnemyDied(runtime);
            // The game's flag misses some player kills (e.g. deaths finished by its own state machine); our hook saw the last hit.
            // COS-92: a death by a trap or the world still pays, except drowning.
            bool byPlayer = killedByPlayer || runtime.LastHitByLocalPlayer || Net.CoopHits.HitByRemote(runtime);
            if (!Core.Rules.GameRules.DeathPays(byPlayer, GameRules.GameRulesFeature.Drowned(runtime.Actor))) return;

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

    private static readonly int PerfAfterEnemyMeleeImpact = Perf.Perf.Register("Hook.EnemyMeleeImpact");

    [HarmonyPatch(typeof(Sons.Gameplay.MeleeWeapon), nameof(Sons.Gameplay.MeleeWeapon.GetNewImpactData))]
    [HarmonyPostfix]
    private static void AfterEnemyMeleeImpact(Sons.Gameplay.MeleeWeapon __instance, MeleeImpactData __result)
    {
        using var perf = Perf.Perf.Scope(PerfAfterEnemyMeleeImpact);
        try
        {
            // Also on a co-op client: its own player's hits are resolved there, and the mirror carries the host's profile (COS-79).
            if (__result == null) return;
            var runtime = EnemyRegistry.Get(__instance._ownerActor);
            if (runtime == null || !runtime.IsActive) return;
            float scaled = (float)EnemyScaler.DamageToPlayer(runtime.Profile, __result._originalRawDamage, runtime.Combat.DamageDealtMultiplier);
            float factor = __result._originalRawDamage > 0 ? scaled / __result._originalRawDamage : 1;
            __result._originalRawDamage = scaled;
            __result._remainingRawDamage *= factor;
            PlayerEffects.LastAttacker = (runtime, Time.time);
            if (Net.CoopSelfTest.ServerDriver || Net.CoopTest.Enabled)
                RLog.Msg($"{Net.CoopTestJoin.Tag} {(EnemyRegistry.IsAuthority ? "host" : "client")}: enemy melee impact scaled {scaled / factor:F1} -> {scaled:F1}");
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
