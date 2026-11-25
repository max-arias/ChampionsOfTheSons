using ChampionsOfTheSons.Core.Rules;
using TheForest.Utils;
using UnityEngine;

namespace ChampionsOfTheSons.Mod.Bridge;

/// <summary>
/// Effects on the local player that need state over time: stun (Basher), poison (Poisonous), health and energy gains
/// (on hit, leech, kills) and Undying Promise. Energy is the game's stamina bar until the spell system brings its own pool.
/// </summary>
public static class PlayerEffects
{
    private static float _poisonPhase, _leakPhase;
    private static int _poisonSerial;

    public static readonly UndyingPromiseState Undying = new();
    public static readonly NearDeathState NearDeath = new();

    /// <summary>The enemy whose melee attack was last built, and when: the hit that follows is attributed to it.</summary>
    public static (EnemyRuntime Enemy, float Time)? LastAttacker { get; set; }

    private static PlayerBuffs Buffs => PlayerDebuffs.Buffs;

    /// <summary>Stunned: no movement, no attacks (the stun buff's <c>stunned</c> stat, COS-89).</summary>
    public static bool Stunned => PlayerState.Character.Stats.IsOn(StatIds.Stunned);
    /// <summary>Rooted: no movement or jumping, but the player can still attack and look around.</summary>
    public static bool Rooted => PlayerState.Character.Stats.IsOn(StatIds.Rooted);
    public static int PoisonCount => Buffs.CountOf(PlayerDebuffs.Poison);

    public static bool Stun(double seconds, string source = "stun")
    {
        if (seconds <= 0 || Spells.FlickerRun.Untouchable) return false;
        var r = Buffs.Apply(PlayerDebuffs.Stun, source, seconds);
        return r is BuffApply.Applied or BuffApply.Refreshed;
    }

    public static bool Root(double seconds, string source = "root")
    {
        if (seconds <= 0 || Spells.FlickerRun.Untouchable) return false;
        var r = Buffs.Apply(PlayerDebuffs.Root, source, seconds);
        return r is BuffApply.Applied or BuffApply.Refreshed;
    }

    /// <summary>A poison of <paramref name="perSecond"/> damage per second. Each call is its own poison unless a source is given (the
    /// same source then adds up and refreshes); two or more poisons also slow the player.</summary>
    public static void AddPoison(double perSecond, double seconds, string? source = null)
    {
        if (perSecond <= 0 || seconds <= 0) return;
        Buffs.Apply(PlayerDebuffs.Poison, source ?? $"p{++_poisonSerial}", seconds, perSecond);
    }

    /// <summary>Purge: ends every debuff it can (stun, root, silence, poisons, slows, corruption: dispel level 2).</summary>
    public static void ClearDebuffs() => Buffs.Dispel(2);

    public static void Heal(double amount)
    {
        var vitals = LocalPlayer.Vitals;
        var health = vitals?.Health;
        if (health == null || amount <= 0 || health._currentValue <= 0) return;
        float max = health.GetMax(), now = Mathf.Min(max, health._currentValue + (float)amount);
        health.SetCurrentValue(now);
        // COS-213: the game keeps a target health and moves health back to it every frame, so a heal that only writes the
        // current value is undone at once in live play. Raise the target with it.
        try
        {
            if (max > 0 && vitals!.GetTargetHealthFactor() * max < now) vitals.SetTargetHealth(now);
        }
        catch (Exception e)
        {
            if (!_targetWarned) RedLoader.RLog.Warning($"[COTS] heal: target health not set: {e.Message}");
            _targetWarned = true;
        }
    }

    private static bool _targetWarned;

    public static void AddEnergy(double amount)
    {
        if (amount > 0) LocalPlayer.Vitals?.AdjustStamina((float)amount);
    }

    public static void Reset()
    {
        Buffs.Clear();
        _poisonPhase = _leakPhase = 0;
        Undying.Reset();
        NearDeath.Reset();
        LastAttacker = null;
    }

    public static void Tick(float dt)
    {
        var vitals = LocalPlayer.Vitals;
        if (vitals == null) return;
        // Slows age on the ability loops (AbilityForces.Age); every other buff here.
        Buffs.Tick(dt, id => id != PlayerDebuffs.MoveSlow);
        Undying.Tick(dt);
        bool wasLocked = !NearDeath.Ready;
        NearDeath.Tick(dt);
        if (wasLocked && NearDeath.Ready && PlayerState.Character.Stats.IsOn(NearDeathState.PerkFlag))
            Loot.LootFeature.Message(UI.UiText.T("ui.perks.near_death.ready", "Last Breath is ready again."));

        // Poison: once a second, the sum of every poison, times the damage-taken multiplier (COTF).
        double poison = Buffs.Total(PlayerDebuffs.Poison);
        if (poison > 0 && (_poisonPhase += dt) >= 1)
        {
            _poisonPhase -= 1;
            vitals.ApplyDamage((float)(poison * PlayerState.Character.Stats.Get(StatIds.AllDamageTaken)));
        }
        else if (poison <= 0) _poisonPhase = 0;
        // Energy leak: energy drained per second.
        double leak = Buffs.Total(PlayerDebuffs.EnergyLeak);
        if (leak > 0 && (_leakPhase += dt) >= 1)
        {
            _leakPhase -= 1;
            Spells.SpellCaster.Spend(leak);
        }
        else if (leak <= 0) _leakPhase = 0;
    }
}
