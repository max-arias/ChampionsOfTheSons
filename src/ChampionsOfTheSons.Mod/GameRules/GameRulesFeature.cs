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
public sealed class GameRulesFeature : IModFeature
{
    /// <summary>
    /// Patches one by one with a log line before each: a patch the IL2CPP runtime cannot take crashes the game without an
    /// exception, and the log then names it.
    /// </summary>
    public void Init()
    {
        var h = new HarmonyLib.Harmony("cots.game-rules");
        void Patch(Type type, string method, string? prefix, string? postfix)
        {
            RLog.Msg($"[COTS] game rules: patching {type.Name}.{method}");
            var target = AccessTools.Method(type, method);
            if (target == null) { RLog.Warning($"[COTS] game rules: {type.Name}.{method} not found"); return; }
            h.Patch(target,
                prefix: prefix == null ? null : new HarmonyMethod(typeof(GameRulesFeature), prefix),
                postfix: postfix == null ? null : new HarmonyMethod(typeof(GameRulesFeature), postfix));
        }
        Patch(typeof(PlayerStats), nameof(PlayerStats.HitFire), nameof(BeforeFire), nameof(AfterFire));
        Patch(typeof(PlayerStats), nameof(PlayerStats.HitPoison), nameof(BeforePoison), nameof(AfterPoison));
        Patch(typeof(PlayerStats), nameof(PlayerStats.HitSkunkSpray), nameof(BeforeSkunk), nameof(AfterSkunk));
        Patch(typeof(PlayerStats), nameof(PlayerStats.HitFromTrap), nameof(BeforeTrap), nameof(AfterTrap));
        Patch(typeof(FirstPersonCharacter), nameof(FirstPersonCharacter.DoFallDamage), nameof(BeforeFall), nameof(AfterFall));
        Patch(typeof(Vitals), nameof(Vitals.ApplyHealing), nameof(BeforeHealing), null);
        // COS-216: drowning hits come from the underwater breathing update; cold takes max vitality away.
        Patch(typeof(PlayerStats), nameof(PlayerStats.UpdateUnderwaterBreathing), nameof(BeforeDrowning), nameof(AfterDrowning));
        Patch(typeof(PlayerStats), nameof(PlayerStats.GetVitalityLostFromColdFactor), null, nameof(AfterColdVitality));
        Patch(typeof(Sons.Wearable.Armour.PlayerArmourSystem), nameof(Sons.Wearable.Armour.PlayerArmourSystem.CalculateRemainingDamageAfterArmourHit), null, nameof(AfterArmour));
        // Not BaseTrapController.TryTriggerTrapMechanics: patching it crashes the game as the save loads (2026-11-21).
        // Traps are polled instead (PollTraps).
        RLog.Msg("[COTS] game rules: patched");
    }

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
        bool burning;
        try { burning = LocalPlayer.Stats != null && LocalPlayer.Stats.IsBurning; } catch { burning = false; }
        if (burning && !recentAttacker && !isDemonic && impact is not (ImpactType.Melee or ImpactType.Projectile)) return PlayerDamageSource.Fire;
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

    private static void BeforeFire() => Mark(PlayerDamageSource.Fire);
    private static void AfterFire() => Unmark();
    private static void BeforePoison() => Mark(PlayerDamageSource.Poison);
    private static void AfterPoison() => Unmark();
    private static void BeforeSkunk() => Mark(PlayerDamageSource.Poison);
    private static void AfterSkunk() => Unmark();
    private static void BeforeTrap() => Mark(PlayerDamageSource.Trap);
    private static void AfterTrap() => Unmark();
    private static void BeforeDrowning() => Mark(PlayerDamageSource.Drowning);
    private static void AfterDrowning() => Unmark();

    /// <summary>Self-test: a hit as the game's underwater breathing update makes it.</summary>
    internal static void HitAsDrowning(float damage)
    {
        BeforeDrowning();
        try { LocalPlayer.Stats.Hit(damage); }
        finally { AfterDrowning(); }
    }

    /// <summary>Self-test: the cold vitality loss before and after the max-health scaling.</summary>
    public static (double Game, double Final) LastCold { get; private set; }

    private static void AfterColdVitality(ref float __result)
    {
        try
        {
            if (Net.ServerConfig.OnDedicatedServer) return;
            double scaled = Core.Rules.GameRules.ColdVitalityLoss(__result, Derived.MaxHealth(PlayerState.Character.Stats));
            LastCold = (__result, scaled);
            __result = (float)scaled;
        }
        catch (Exception e)
        {
            RLog.Error($"Champions of the Sons: cold rule failed: {e}");
        }
    }

    private static void BeforeFall(FirstPersonCharacter __instance)
    {
        try { _fallSpeed = Mathf.Abs(__instance.LandingVelocityY); } catch { _fallSpeed = 0; }
        Mark(PlayerDamageSource.Fall);
    }

    private static void AfterFall() => Unmark();

    // ---------------------------------------------------------------- heals

    private static float _medsAt = -10, _herbAt = -10;
    private static int _pills = -1, _herbs = -1;
    private static int[]? _pillIds, _herbIds;

    /// <summary>Meds 60% of max health, herbs 6%, other heals +0.2% of max health per point. A heal right after the bag lost
    /// meds or a herb counts as that item.</summary>
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

    private static void AfterArmour(float initialDamage, ref float __result)
    {
        __result = (float)Core.Rules.GameRules.WornArmorRemaining(initialDamage, __result);
    }

    // ---------------------------------------------------------------- enemies: traps

    private static TrapKind _trap;
    private static Vector3 _trapAt;
    private static float _trapUntil;

    private static readonly List<Sons.Gameplay.Traps.BaseTrapController> Traps = new();
    private static readonly HashSet<IntPtr> Triggered = new();
    private static float _trapScan, _trapPoll;

    /// <summary>Every 5 s: the traps within 60 m. Every 0.1 s: a trap that just went off sets the trap context.</summary>
    private static void PollTraps(float dt)
    {
        var me = LocalPlayer.Transform;
        if (me == null) return;
        _trapScan -= dt;
        if (_trapScan <= 0)
        {
            _trapScan = 5f;
            Traps.Clear();
            foreach (var t in UnityEngine.Object.FindObjectsOfType<Sons.Gameplay.Traps.BaseTrapController>())
                if (t != null && Vector3.Distance(t.transform.position, me.position) < 60f) Traps.Add(t);
        }
        _trapPoll -= dt;
        if (_trapPoll > 0) return;
        _trapPoll = 0.1f;
        foreach (var t in Traps)
        {
            if (t == null) continue;
            bool on = t.IsTriggered;
            if (on && Triggered.Add(t.Pointer)) OnTrapTriggered(t);
            else if (!on) Triggered.Remove(t.Pointer);
        }
    }

    private static void OnTrapTriggered(Sons.Gameplay.Traps.BaseTrapController __instance)
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
            PollTraps(dt);
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
