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
    private sealed class Poison
    {
        public double PerSecond;
        public int TicksLeft;
        public float Phase;
    }

    private static float _stunLeft;
    private static readonly List<Poison> Poisons = new();

    public static readonly UndyingPromiseState Undying = new();
    public static readonly NearDeathState NearDeath = new();

    /// <summary>The enemy whose melee attack was last built, and when: the hit that follows is attributed to it.</summary>
    public static (EnemyRuntime Enemy, float Time)? LastAttacker { get; set; }

    public static bool Stunned => _stunLeft > 0;
    public static int PoisonCount => Poisons.Count;

    public static bool Stun(double seconds)
    {
        if (seconds <= 0 || PlayerState.Character.Stats.Get(CombatStatIds.StunImmunity) > 0) return false;
        seconds = SpellTuning.DebuffSeconds(PlayerState.Character.Stats, seconds);
        _stunLeft = Mathf.Max(_stunLeft, (float)seconds);
        return true;
    }

    public static void AddPoison(double perSecond, double seconds)
    {
        if (perSecond <= 0 || seconds <= 0) return;
        seconds = SpellTuning.DebuffSeconds(PlayerState.Character.Stats, seconds);
        Poisons.Add(new Poison { PerSecond = perSecond, TicksLeft = (int)Math.Ceiling(seconds) });
    }

    /// <summary>Purge: ends stun and poison on the player.</summary>
    public static void ClearDebuffs()
    {
        _stunLeft = 0;
        Poisons.Clear();
    }

    public static void Heal(double amount)
    {
        var health = LocalPlayer.Vitals?.Health;
        if (health == null || amount <= 0 || health._currentValue <= 0) return;
        health.SetCurrentValue(Mathf.Min(health.GetMax(), health._currentValue + (float)amount));
    }

    public static void AddEnergy(double amount)
    {
        if (amount > 0) LocalPlayer.Vitals?.AdjustStamina((float)amount);
    }

    public static void Reset()
    {
        _stunLeft = 0;
        Poisons.Clear();
        Undying.Reset();
        NearDeath.Reset();
        LastAttacker = null;
    }

    public static void Tick(float dt)
    {
        var vitals = LocalPlayer.Vitals;
        if (vitals == null) return;
        _stunLeft = Mathf.Max(0, _stunLeft - dt);
        Undying.Tick(dt);
        bool wasLocked = !NearDeath.Ready;
        NearDeath.Tick(dt);
        if (wasLocked && NearDeath.Ready && PlayerState.Character.Stats.IsOn(NearDeathState.PerkFlag))
            Loot.LootFeature.Message(UI.UiText.T("ui.perks.near_death.ready", "Last Breath is ready again."));

        for (int i = Poisons.Count - 1; i >= 0; i--)
        {
            var p = Poisons[i];
            p.Phase += dt;
            while (p.Phase >= 1 && p.TicksLeft > 0)
            {
                p.Phase -= 1;
                p.TicksLeft--;
                vitals.ApplyDamage((float)p.PerSecond);
            }
            if (p.TicksLeft <= 0) Poisons.RemoveAt(i);
        }
    }
}
