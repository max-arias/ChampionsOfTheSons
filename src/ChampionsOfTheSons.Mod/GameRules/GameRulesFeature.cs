using ChampionsOfTheSons.Core.Rules;
using ChampionsOfTheSons.Mod.Bridge;
using HarmonyLib;
using RedLoader;
using Sons.Ai.Vail;
using TheForest.Utils;
using UnityEngine;

namespace ChampionsOfTheSons.Mod.GameRules;

/// <summary>
/// COS-92: the game's own rules changed the way COTF changed The Forest's (numbers: <see cref="Core.Rules.GameRules"/>).
/// The game's hit methods for fire, poison, skunk spray, traps and falls mark the hit they cause with its source, so the
/// player hook (<see cref="PlayerBridge"/>) knows it is not an enemy's attack: it skips dodge, armor and block and takes
/// COTF's amount. Heals from meds and herbs, hunger and thirst drain, thirst damage and worn armor are adjusted here; traps
/// and explosions against enemies in <see cref="EnemyPatches"/>.
/// </summary>
[HarmonyPatch]
public sealed class GameRulesFeature : IModFeature
{
    private static PlayerDamageSource? _flag;
    private static float _fallSpeed;

    /// <summary>The source the hit in progress was marked with, if any (set around the game's own hit methods).</summary>
    public static PlayerDamageSource? Flagged => _flag;

    /// <summary>Self-test: the last classified player hit, its source and the amount after the rule.</summary>
    public static (PlayerDamageSource Source, double Game, double Final) LastHit { get; set; }

    // ---------------------------------------------------------------- classification

    /// <summary>
    /// Where a player hit came from: a flag from the game's own method, an explosion, an enemy (a recent attacker, a melee or
    /// projectile impact, demonic damage, or an enemy in reach), else the environment.
    /// </summary>
    public static PlayerDamageSource Classify(ImpactType impact, bool isDemonic, bool recentAttacker, bool enemyInReach)
    {
        if (_flag is { } f) return f;
        if (impact == ImpactType.Explosion) return PlayerDamageSource.Explosion;
        if (recentAttacker || isDemonic || impact is ImpactType.Melee or ImpactType.Projectile || enemyInReach)
            return PlayerDamageSource.Enemy;
        return PlayerDamageSource.Environment;
    }

    /// <summary>The amount a non-enemy hit does under COTF's rule, before the damage-taken multiplier.</summary>
    public static double NonEnemyHit(PlayerDamageSource source, double game)
    {
        var s = PlayerState.Character.Stats;
        double max = Derived.MaxHealth(s);
        return source switch
        {
            PlayerDamageSource.Fall => Core.Rules.GameRules.FallDamage(_fallSpeed, game, max),
            PlayerDamageSource.Explosion => Core.Rules.GameRules.ExplosionToPlayer(Difficulties.ForLevel(PlayerState.Character.Progress.Level).Index,
                WorldState.FriendlyFire, Difficulties.Hell.Index),
            _ => Core.Rules.GameRules.PlayerHit(source, game, max, s.Get(StatIds.FireDamageTaken)),
        };
    }

    private static void Mark(PlayerDamageSource s) => _flag = s;
    private static void Unmark() => _flag = null;

    [HarmonyPatch(typeof(PlayerStats), nameof(PlayerStats.HitFire))] [HarmonyPrefix] private static void BeforeFire() => Mark(PlayerDamageSource.Fire);
    [HarmonyPatch(typeof(PlayerStats), nameof(PlayerStats.HitFire))] [HarmonyPostfix] private static void AfterFire() => Unmark();
    [HarmonyPatch(typeof(PlayerStats), nameof(PlayerStats.HitPoison))] [HarmonyPrefix] private static void BeforePoison() => Mark(PlayerDamageSource.Poison);
    [HarmonyPatch(typeof(PlayerStats), nameof(PlayerStats.HitPoison))] [HarmonyPostfix] private static void AfterPoison() => Unmark();
    [HarmonyPatch(typeof(PlayerStats), nameof(PlayerStats.HitSkunkSpray))] [HarmonyPrefix] private static void BeforeSkunk() => Mark(PlayerDamageSource.Poison);
    [HarmonyPatch(typeof(PlayerStats), nameof(PlayerStats.HitSkunkSpray))] [HarmonyPostfix] private static void AfterSkunk() => Unmark();
    [HarmonyPatch(typeof(PlayerStats), nameof(PlayerStats.HitFromTrap))] [HarmonyPrefix] private static void BeforeTrap() => Mark(PlayerDamageSource.Trap);
    [HarmonyPatch(typeof(PlayerStats), nameof(PlayerStats.HitFromTrap))] [HarmonyPostfix] private static void AfterTrap() => Unmark();

    [HarmonyPatch(typeof(FirstPersonCharacter), nameof(FirstPersonCharacter.DoFallDamage))]
    [HarmonyPrefix]
    private static void BeforeFall(FirstPersonCharacter __instance)
    {
        try { _fallSpeed = Mathf.Abs(__instance.LandingVelocityY); } catch { _fallSpeed = 0; }
        Mark(PlayerDamageSource.Fall);
    }

    [HarmonyPatch(typeof(FirstPersonCharacter), nameof(FirstPersonCharacter.DoFallDamage))]
    [HarmonyPostfix]
    private static void AfterFall() => Unmark();

    // ---------------------------------------------------------------- heals

    private static float _medsAt = -10, _herbAt = -10;
    private static int _pills = -1, _herbs = -1;
    private static int[]? _pillIds, _herbIds;

    /// <summary>Meds 60% of max health, herbs 6%, other heals +0.2% of max health per point. A heal right after the bag lost
    /// meds or a herb counts as that item.</summary>
    [HarmonyPatch(typeof(Vitals), nameof(Vitals.ApplyHealing))]
    [HarmonyPrefix]
    private static void BeforeHealing(ref float value)
    {
        try
        {
            if (value <= 0 || !LocalPlayer.IsInWorld) return;
            float now = Time.realtimeSinceStartup;
            var kind = now - _medsAt < 1.5f ? HealKind.Meds : now - _herbAt < 1.5f ? HealKind.Herb : HealKind.Other;
            if (kind != HealKind.Other) { _medsAt = _herbAt = -10; }
            double max = Derived.MaxHealth(PlayerState.Character.Stats);
            float before = value;
            value = (float)Core.Rules.GameRules.Heal(kind, value, max);
            LastHeal = (kind, before, value);
        }
        catch (Exception e)
        {
            RLog.Warning($"[COTS] heal rule: {e.Message}");
        }
    }

    /// <summary>Self-test: the last heal's kind, the game's amount and ours.</summary>
    public static (HealKind Kind, double Game, double Final) LastHeal { get; private set; }

    private static void TrackConsumables()
    {
        var inv = LocalPlayer.Inventory;
        if (inv == null) return;
        _pillIds ??= Ids("Pills", "Meds", "Medicine");
        _herbIds ??= Ids("Aloe", "Yarrow", "Horsetail");
        int pills = _pillIds.Sum(id => inv.AmountOf(id, true, false));
        int herbs = _herbIds.Sum(id => inv.AmountOf(id, true, false));
        if (_pills >= 0 && pills < _pills) _medsAt = Time.realtimeSinceStartup;
        if (_herbs >= 0 && herbs < _herbs) _herbAt = Time.realtimeSinceStartup;
        _pills = pills;
        _herbs = herbs;
    }

    private static int[] Ids(params string[] words)
    {
        var list = Sons.Items.Core.ItemDatabaseManager.Items;
        var ids = new List<int>();
        for (int i = 0; i < list.Count; i++)
            if (list[i] != null && words.Any(w => list[i].Name.Contains(w, StringComparison.OrdinalIgnoreCase))) ids.Add(list[i].Id);
        return ids.ToArray();
    }

    // ---------------------------------------------------------------- armor

    [HarmonyPatch(typeof(Sons.Wearable.Armour.PlayerArmourSystem), nameof(Sons.Wearable.Armour.PlayerArmourSystem.CalculateRemainingDamageAfterArmourHit))]
    [HarmonyPostfix]
    private static void AfterArmour(float initialDamage, ref float __result)
    {
        __result = (float)Core.Rules.GameRules.WornArmorRemaining(initialDamage, __result);
    }

    // ---------------------------------------------------------------- enemies: traps

    private static TrapKind _trap;
    private static Vector3 _trapAt;
    private static float _trapUntil;

    [HarmonyPatch(typeof(Sons.Gameplay.Traps.BaseTrapController), nameof(Sons.Gameplay.Traps.BaseTrapController.TryTriggerTrapMechanics))]
    [HarmonyPrefix]
    private static void BeforeTrapTriggers(Sons.Gameplay.Traps.BaseTrapController __instance)
    {
        try
        {
            string type = __instance.GetIl2CppType().Name;
            _trap = type switch
            {
                "MaceTrap" => TrapKind.Swinging,
                "SpringTrap" or "BoneMakerTrap" or "FlySwatterTrap" or "SpinTrap" or "GrindTrap" => TrapKind.Heavy,
                _ => TrapKind.Generic,
            };
            _trapAt = __instance.transform.position;
            _trapUntil = Time.time + 2f; // swinging and falling parts hit a moment later
        }
        catch
        {
            _trapUntil = 0;
        }
    }

    /// <summary>A hit on an enemy near a trap that just went off is the trap's: its fixed amount instead of a kill. Null: not a trap.</summary>
    public static double? TrapHitOn(VailActor actor)
    {
        if (Time.time > _trapUntil || actor == null) return null;
        return Vector3.Distance(actor.transform.position, _trapAt) <= 8f ? Core.Rules.GameRules.TrapDamage(_trap) : null;
    }

    /// <summary>Whether an enemy died in water deep enough to drown (no XP or loot then).</summary>
    public static bool Drowned(VailActor? actor)
    {
        try { return actor != null && actor.IsInSwimmableWater() && actor.DepthInWater() >= actor._drownDepth; }
        catch { return false; }
    }

    // ---------------------------------------------------------------- needs

    private static float _fullness = -1, _hydration = -1, _tick;

    public void Update(float dt, bool inWorld)
    {
        if (!inWorld) { _fullness = _hydration = -1; return; }
        try
        {
            TrackConsumables();
            var v = LocalPlayer.Vitals;
            var s = PlayerState.Character.Stats;
            if (v != null)
            {
                _fullness = SlowDrain(v.GetFullness(), _fullness, Core.Rules.GameRules.NeedsDrainFactor(s.Get("perk_hunger_rate")), v.SetFullness);
                _hydration = SlowDrain(v.GetHydration(), _hydration, Core.Rules.GameRules.NeedsDrainFactor(s.Get("perk_thirst_rate")), v.SetHydration);
            }
            _tick -= dt;
            if (_tick <= 0 && LocalPlayer.Stats != null)
            {
                _tick = 1f;
                // Thirst damage: 20% of max health a tick (COTF), through the game's own dehydration tick.
                LocalPlayer.Stats._dehydrationDamagePerTick = (float)(0.2 * Derived.MaxHealth(s));
            }
        }
        catch (Exception e)
        {
            if (!_warned) RLog.Warning($"[COTS] needs rule: {e.Message}");
            _warned = true;
        }
    }

    private static bool _warned;

    /// <summary>Gives back part of each small drop (the natural drain), so it runs at <paramref name="factor"/> of the game's
    /// rate. Rises (eating, drinking) and big drops pass unchanged.</summary>
    private static float SlowDrain(float now, float last, double factor, Action<float> set)
    {
        if (last >= 0 && now < last && last - now < 1f)
        {
            float kept = last - (float)((last - now) * factor);
            set(kept);
            return kept;
        }
        return now;
    }

    public void RegisterSelfTests() => GameRulesSelfTest.Register();
}
