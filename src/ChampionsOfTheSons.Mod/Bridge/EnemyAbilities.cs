using ChampionsOfTheSons.Core.Rules;
using RedLoader;
using TheForest.World;
using UnityEngine;

namespace ChampionsOfTheSons.Mod.Bridge;

/// <summary>
/// Hook for the active, spell-like abilities (COS-20: Meteor, Laser, Black Hole, Blizzard, Radiance, Flare, Cataclysms,
/// Warp, Chains, Trapper). Register one handler per ability with <see cref="EnemyAbilities.Register"/>; the host loop calls it
/// only while the enemy is alive and active, so a handler never has to guard against dead or despawned enemies.
/// </summary>
public interface IAbilityHandler
{
    EnemyAbility Ability { get; }

    /// <summary>Once, when the enemy has its profile.</summary>
    void OnSpawn(EnemyRuntime enemy) { }

    /// <summary>Every frame while the enemy is alive and active.</summary>
    void OnTick(EnemyRuntime enemy, float dt) { }

    /// <summary>After the enemy died (the runtime is already out of the registry).</summary>
    void OnDeath(EnemyRuntime enemy) { }
}

public static class EnemyAbilities
{
    private static readonly List<IAbilityHandler> Handlers = new();

    public static void Register(IAbilityHandler handler) => Handlers.Add(handler);

    internal static IEnumerable<IAbilityHandler> For(EnemyRuntime enemy) => Handlers.Where(h => enemy.Profile.Has(h.Ability));

    /// <summary>Every frame per active enemy: a plain loop (no LINQ iterator per enemy per frame).</summary>
    internal static void Tick(EnemyRuntime enemy, float dt)
    {
        if (enemy.Profile.Abilities.Count == 0) return;
        for (int i = 0; i < Handlers.Count; i++)
            if (enemy.Profile.Has(Handlers[i].Ability)) Handlers[i].OnTick(enemy, dt);
    }
}

/// <summary>
/// Drives the passive abilities and debuffs of every tracked enemy: scale, speed, crowd-control immunity, rain, bleed/fire/etc.
/// ticks, Avenger and Sacrifice. Everything is gated on <see cref="EnemyRuntime.IsActive"/> (the original's bug #43).
/// </summary>
public sealed class EnemyAbilityFeature : IModFeature
{
    private static float _weatherTimer, _resolveTimer;
    private static bool _raining;

    /// <summary>For self-tests: force the weather answer (null = ask the game).</summary>
    public static bool? RainOverride { get; set; }

    public static bool Raining => RainOverride ?? _raining;

    /// <summary>Called right after an enemy is rolled: applies what only needs doing once.</summary>
    public static void Prepare(EnemyRuntime enemy)
    {
        var actor = enemy.Actor;
        if (actor == null) return;
        try
        {
            float scale = WantedScale(enemy);
            if (Math.Abs(scale - 1) > 0.001) actor.SetScaleMult(scale);
            if (enemy.Combat.CcImmune)
            {
                actor._knockdownEnabled = false;
                actor._staggerPerHit = 0; // Juggernaut: a hit never builds up a stagger (COS-90)
            }
            foreach (var h in EnemyAbilities.For(enemy)) h.OnSpawn(enemy);
        }
        catch (Exception e)
        {
            RLog.Error($"Champions of the Sons: enemy prepare failed: {e}");
        }
    }

    /// <summary>The model size the enemy should have: its profile scale times the Undead revive growth.</summary>
    public static float WantedScale(EnemyRuntime enemy) => (float)(enemy.Profile.Scale * enemy.Combat.ScaleMultiplier);

    /// <summary>An enemy died: Avenger allies grow angrier, Sacrifice heals and empowers the survivors, handlers get their callback.</summary>
    public static void OnEnemyDied(EnemyRuntime dead)
    {
        try
        {
            Vector3? at = dead.LastPosition;
            if (at != null)
            {
                foreach (var other in EnemyRegistry.Live)
                {
                    if (other == dead || !other.IsActive) continue;
                    float dist = Vector3.Distance(other.Actor!.transform.position, at.Value);
                    other.Combat.OnAllyDied(dist);
                    if (dead.Profile.Has(EnemyAbility.Sacrifice))
                    {
                        double heal = other.Combat.OnSacrificeDied(dist);
                        var health = EnemyRegistry.HealthOf(other.Actor);
                        if (heal > 0 && health != null)
                            health.SetCurrentValue(Mathf.Min(health.GetMax(), health._currentValue + (float)(heal * health.GetMax())));
                    }
                }
            }
            foreach (var h in EnemyAbilities.For(dead)) h.OnDeath(dead);
        }
        catch (Exception e)
        {
            RLog.Error($"Champions of the Sons: enemy death abilities failed: {e}");
        }
    }

    public void RegisterSelfTests() => AffixSelfTest.Register();

    public void Update(float dt, bool inWorld)
    {
        // A dedicated server has no player in the world, but owns the enemies: it steps them too (COS-79).
        if (inWorld || Net.ServerConfig.OnDedicatedServer) Step(dt);
    }

    /// <summary>One frame of the host loop. Self-tests call it to step enemy time themselves (the headless game runs with time scale 0).</summary>
    public static void Step(float dt)
    {
        // Clients need the live list too (nearest attacker, spell targets), so it is refreshed before the authority check.
        _resolveTimer -= dt;
        if (_resolveTimer <= 0 || EnemyRegistry.Dirty)
        {
            _resolveTimer = 0.25f;
            EnemyRegistry.ResolveActors();
        }
        if (!EnemyRegistry.IsAuthority) return;

        _weatherTimer -= dt;
        if (_weatherTimer <= 0)
        {
            _weatherTimer = 1;
            try
            {
                _raining = WeatherSystem.Instance != null && WeatherSystem.Instance.IsRaining;
            }
            catch
            {
                _raining = false;
            }
        }

        // Only enemies active in the scene: Tick does nothing for the rest (thousands in the world simulation).
        foreach (var enemy in EnemyRegistry.Live)
        {
            bool active = enemy.IsActive;
            if (active) enemy.LastPosition = enemy.Actor!.transform.position;
            enemy.Combat.Raining = Raining;
            var ticks = enemy.Combat.Tick(dt, active);
            if (!active) continue;

            foreach (var tick in ticks)
            {
                // Damage over time is pure damage (armor does not help) but the steadfast caps and shields still apply.
                var hit = new OutgoingHit(tick.Damage, DamageKind.Pure, false, 0);
                EnemyPatches.DealForced(enemy, hit, creditPlayer: tick.Source.StartsWith("player.", StringComparison.Ordinal));
                // Burning melts armor: each fire tick adds its damage to the enemy's armor reduction (COS-90).
                if (tick.Kind == DotKind.Fire) enemy.ArmorReduction = EnemyHitEffects.MeltArmor(enemy.Profile, enemy.ArmorReduction, tick.Damage);
                if (!enemy.IsActive) break;
            }
            if (!enemy.IsActive) continue;

            ApplySpeed(enemy);
            if (!enemy.Dormant) EnemyAbilities.Tick(enemy, dt);
        }
        Abilities.ActiveAbilitySystem.Sweep(dt);
    }

    /// <summary>Movement and animation speed: the profile's speed times every slow, stun and rain bonus.</summary>
    private static void ApplySpeed(EnemyRuntime enemy)
    {
        // The game resets scale when it activates the actor, so it is kept in step here as well as at spawn.
        float scale = WantedScale(enemy);
        if (Math.Abs(scale - 1) > 0.001 && Mathf.Abs(enemy.Actor!.GetScaleMult() - scale) > 0.01f)
            enemy.Actor.SetScaleMult(scale);
        float want = (float)(enemy.Profile.Speed * enemy.Combat.SpeedMultiplier);
        enemy.WantedSpeed = want;
        var anim = enemy.Actor!._animator;
        if (anim == null) return;
        if (Mathf.Abs(anim.speed - want) > 0.01f) anim.speed = want;
    }
}
