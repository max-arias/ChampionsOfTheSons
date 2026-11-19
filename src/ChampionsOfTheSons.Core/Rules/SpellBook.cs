using ChampionsOfTheSons.Core.Content;

namespace ChampionsOfTheSons.Core.Rules;

public enum CastResult
{
    Ok,
    EmptySlot,
    OnCooldown,
    NotEnoughEnergy,
}

/// <summary>Energy and stamina a cast takes.</summary>
public readonly record struct SpellCost(double Energy, double Stamina);

/// <summary>Spell bar (6 slots), cooldowns and costs.</summary>
public sealed class SpellBook
{
    public const int Slots = 6;

    private readonly ContentDb _content;
    private readonly string?[] _loadout = new string?[Slots];
    /// <summary>Cooldown progress per slot: 0 = just cast, 1 = ready.</summary>
    private readonly double[] _ready = Enumerable.Repeat(1.0, Slots).ToArray();

    public SpellBook(ContentDb content) => _content = content;

    public IReadOnlyList<string?> Loadout => _loadout;

    /// <summary>Flat seconds off a spell's base cooldown (perks like Quick Silver), applied before the cooldown multiplier.</summary>
    public Func<string, double>? CooldownReduction { get; set; }

    /// <summary>Puts a spell in a slot. Equipping starts its full cooldown, as in the original.</summary>
    public bool Equip(int slot, string? spellId)
    {
        if (slot is < 0 or >= Slots) return false;
        if (spellId != null && !_content.Spells.ContainsKey(spellId)) return false;
        for (int i = 0; i < Slots; i++)
            if (spellId != null && _loadout[i] == spellId) _loadout[i] = null;
        _loadout[slot] = spellId;
        _ready[slot] = spellId == null ? 1 : 0;
        return true;
    }

    public double CooldownProgress(int slot) => _ready[slot];

    public bool IsReady(int slot) => _ready[slot] >= 1;

    /// <summary>Advances cooldowns: progress += dt × cooldownRate / (cooldown × cooldownMult).</summary>
    public void Tick(double dt, StatSheet stats)
    {
        double rate = stats.Get(StatIds.CooldownRate);
        double mult = stats.Get(StatIds.Cooldown);
        for (int i = 0; i < Slots; i++)
        {
            if (_loadout[i] is not string id || _ready[i] >= 1) continue;
            double cd = Math.Max(0.5, _content.Spells[id].Cooldown - (CooldownReduction?.Invoke(id) ?? 0)) * mult;
            _ready[i] = cd <= 0 ? 1 : Math.Min(1, _ready[i] + dt * rate / cd);
        }
    }

    /// <summary>Takes <paramref name="seconds"/> off the remaining cooldown of one random spell that is cooling down (Infinity Loop). False if none is.</summary>
    public bool ReduceRandomCooldown(double seconds, StatSheet stats, Rng rng)
    {
        var cooling = Enumerable.Range(0, Slots).Where(i => _loadout[i] != null && _ready[i] < 1).ToList();
        if (cooling.Count == 0) return false;
        int slot = rng.Pick(cooling);
        double cd = Math.Max(0.5, _content.Spells[_loadout[slot]!].Cooldown - (CooldownReduction?.Invoke(_loadout[slot]!) ?? 0)) * stats.Get(StatIds.Cooldown);
        _ready[slot] = cd <= 0 ? 1 : Math.Min(1, _ready[slot] + seconds / cd);
        return true;
    }

    /// <summary>Energy/stamina split: energy part = cost × spellCostEnergyCost × spellCost, the rest goes to stamina.</summary>
    public static SpellCost Cost(SpellDef spell, StatSheet stats)
    {
        double total = spell.EnergyCost * stats.Get(StatIds.SpellCost);
        double energyShare = Math.Clamp(stats.Get(StatIds.SpellCostEnergyCost), 0, 1);
        return new SpellCost(total * energyShare, total * (1 - energyShare));
    }

    /// <summary>Tries to cast the spell in a slot with the given energy. On success the cooldown restarts.</summary>
    public CastResult TryCast(int slot, double energy, StatSheet stats, out SpellCost cost)
    {
        cost = default;
        if (slot is < 0 or >= Slots || _loadout[slot] is not string id) return CastResult.EmptySlot;
        if (!IsReady(slot)) return CastResult.OnCooldown;
        cost = Cost(_content.Spells[id], stats);
        if (energy < cost.Energy) return CastResult.NotEnoughEnergy;
        _ready[slot] = 0;
        return CastResult.Ok;
    }

    public void Restore(IReadOnlyList<string?> loadout)
    {
        for (int i = 0; i < Slots; i++)
        {
            var id = i < loadout.Count ? loadout[i] : null;
            _loadout[i] = id != null && _content.Spells.ContainsKey(id) ? id : null;
            _ready[i] = 1;
        }
    }
}
