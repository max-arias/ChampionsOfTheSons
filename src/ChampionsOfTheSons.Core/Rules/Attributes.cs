using ChampionsOfTheSons.Core.Content;

namespace ChampionsOfTheSons.Core.Rules;

/// <summary>
/// Attribute points (COS-174, Max 2026-11-23): the player spends points on Strength, Agility, Vitality and Intelligence; gear gives
/// none. Each point raises its attribute (so perks like "each point of Strength adds 1% melee damage" read it) and gives its own
/// classic bonuses (<see cref="AttributeRules"/>). Spending and refunding are free at any time.
/// </summary>
public sealed class AttributePoints
{
    public static readonly string[] Ids = { StatIds.Strength, StatIds.Agility, StatIds.Vitality, StatIds.Intelligence };

    private readonly Dictionary<string, int> _spent = new();

    public event Action? Changed;

    public IReadOnlyDictionary<string, int> Spent => _spent;

    public int Total => _spent.Values.Sum();

    public int Get(string attribute) => _spent.TryGetValue(attribute, out var n) ? n : 0;

    /// <summary>Spends up to <paramref name="amount"/> points on one attribute, as many as <paramref name="available"/> allows. Returns how many.</summary>
    public int Spend(string attribute, int amount, int available)
    {
        if (!Ids.Contains(attribute)) return 0;
        int n = Math.Min(Math.Max(0, amount), Math.Max(0, available));
        if (n == 0) return 0;
        _spent[attribute] = Get(attribute) + n;
        Changed?.Invoke();
        return n;
    }

    /// <summary>Takes up to <paramref name="amount"/> points back from one attribute. Returns how many.</summary>
    public int Refund(string attribute, int amount)
    {
        int n = Math.Min(Math.Max(0, amount), Get(attribute));
        if (n == 0) return 0;
        if (Get(attribute) == n) _spent.Remove(attribute);
        else _spent[attribute] = Get(attribute) - n;
        Changed?.Invoke();
        return n;
    }

    public void Reset()
    {
        if (_spent.Count == 0) return;
        _spent.Clear();
        Changed?.Invoke();
    }

    /// <summary>After a level went down (admin): takes points back, from the largest attribute first, until no more than
    /// <paramref name="total"/> are spent.</summary>
    public void FitTo(int total)
    {
        bool changed = false;
        while (Total > Math.Max(0, total))
        {
            var top = _spent.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.Ordinal).First();
            int n = Math.Min(top.Value, Total - Math.Max(0, total));
            if (top.Value == n) _spent.Remove(top.Key); else _spent[top.Key] = top.Value - n;
            changed = true;
        }
        if (changed) Changed?.Invoke();
    }

    /// <summary>Restores saved points without checks (unknown attribute ids are dropped).</summary>
    public void Restore(IReadOnlyDictionary<string, int>? saved)
    {
        _spent.Clear();
        if (saved != null)
            foreach (var (id, n) in saved)
                if (n > 0 && Ids.Contains(id)) _spent[id] = n;
        Changed?.Invoke();
    }
}

/// <summary>What attribute points give (COS-174). Starting numbers; PacingSim checks them.</summary>
public static class AttributeRules
{
    /// <summary>Points to spend at level 1.</summary>
    public const int StartPoints = 10;

    /// <summary>Points earned by <paramref name="level"/>: the start points plus what the level used to add to all four attributes
    /// by itself (Σ round(√x) per attribute), so a player who spreads them evenly stands where the automatic gain left them.</summary>
    public static int TotalPoints(int level) => StartPoints + 4 * (XpCurve.AttributeBase(level) - 2);

    // Per spent point.
    public const double MeleePerStrength = 0.002, CarryPerStrength = 1.0 / 25;
    public const double HealthPerVitality = 0.5, RegenPerVitality = 0.001;
    public const double RangedPerAgility = 0.002, AttackSpeedPerAgility = 0.0005, EnergyPerAgility = 0.2;
    public const double SpellPerIntelligence = 0.002, EnergyRegenPerIntelligence = 0.002, CooldownPerIntelligence = 0.0002, MaxCooldownCut = 0.4;

    /// <summary>The stat-sheet source "attributes": each attribute raised by its points, plus each point's own bonuses.</summary>
    public static IReadOnlyList<StatModifier> Modifiers(AttributePoints p)
    {
        int str = p.Get(StatIds.Strength), agi = p.Get(StatIds.Agility), vit = p.Get(StatIds.Vitality), intel = p.Get(StatIds.Intelligence);
        var mods = new List<StatModifier>();
        foreach (var id in AttributePoints.Ids)
            if (p.Get(id) > 0) mods.Add(new StatModifier(id, EffectOp.Add, p.Get(id)));
        if (str > 0) mods.Add(new StatModifier(StatIds.MeleeIncreasedDmg, EffectOp.Add, str * MeleePerStrength));
        if (vit > 0)
        {
            mods.Add(new StatModifier(StatIds.MaxHealth, EffectOp.Add, vit * HealthPerVitality));
            mods.Add(new StatModifier(StatIds.HealthPerSecRate, EffectOp.Add, vit * RegenPerVitality));
        }
        if (agi > 0)
        {
            mods.Add(new StatModifier(StatIds.RangedIncreasedDmg, EffectOp.Add, agi * RangedPerAgility));
            mods.Add(new StatModifier(StatIds.AttackSpeed, EffectOp.Add, agi * AttackSpeedPerAgility));
            mods.Add(new StatModifier(StatIds.MaxEnergy, EffectOp.Add, agi * EnergyPerAgility));
        }
        if (intel > 0)
        {
            mods.Add(new StatModifier(StatIds.SpellIncreasedDmg, EffectOp.Add, intel * SpellPerIntelligence));
            mods.Add(new StatModifier(StatIds.StaminaPerSecRate, EffectOp.Add, intel * EnergyRegenPerIntelligence));
            mods.Add(new StatModifier(StatIds.Cooldown, EffectOp.Mult, 1 - CooldownCut(intel)));
        }
        return mods;
    }

    /// <summary>Spell cooldowns shortened by Intelligence points, at most <see cref="MaxCooldownCut"/>.</summary>
    public static double CooldownCut(int intelligence) => Math.Min(MaxCooldownCut, Math.Max(0, intelligence) * CooldownPerIntelligence);

    /// <summary>Extra carried items of every kind the carry stats name, from Strength points.</summary>
    public static int CarryBonus(int strength) => (int)Math.Floor(Math.Max(0, strength) * CarryPerStrength);
}
