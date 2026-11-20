using ChampionsOfTheSons.Core.Content;

namespace ChampionsOfTheSons.Core.Rules;

public enum CastResult
{
    Ok,
    EmptySlot,
    OnCooldown,
    NotEnoughEnergy,
    /// <summary>A channeled or toggle spell that was running has been switched off by the cast.</summary>
    Stopped,
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
    private readonly bool[] _active = new bool[Slots];

    public SpellBook(ContentDb content) => _content = content;

    public IReadOnlyList<string?> Loadout => _loadout;

    /// <summary>Flat seconds off a spell's base cooldown (perks like Quick Silver), applied before the cooldown multiplier.</summary>
    public Func<string, double>? CooldownReduction { get; set; }

    /// <summary>Factor on a spell's base cooldown after the flat reduction (perks like Time of Need), before the cooldown multiplier.</summary>
    public Func<string, double>? CooldownScale { get; set; }

    /// <summary>Factor on a spell's energy cost and upkeep from items (Grip of Sora on Multishot).</summary>
    public Func<string, StatSheet, double>? CostScale { get; set; }

    /// <summary>Energy added to a spell's base cost by items and perks (Flame Pauldrons on Firebolt), before the cost multipliers.</summary>
    public Func<string, StatSheet, double>? CostAdd { get; set; }

    /// <summary>Factor on one spell's cooldown rate (the Healing Dome bracer).</summary>
    public Func<string, StatSheet, double>? CooldownRateScale { get; set; }

    /// <summary>What casting or one second of upkeep of a spell costs, after <see cref="CostAdd"/> and <see cref="CostScale"/>.</summary>
    public SpellCost CostOf(string spellId, StatSheet stats)
    {
        var def = _content.Spells[spellId];
        double add = CostAdd?.Invoke(spellId, stats) ?? 0;
        var c = Cost(add == 0 ? def : new SpellDef { Id = def.Id, EnergyCost = Math.Max(0, def.EnergyCost + add) }, stats);
        double k = CostScale?.Invoke(spellId, stats) ?? 1;
        return new SpellCost(c.Energy * k, c.Stamina * k);
    }

    /// <summary>A spell's cooldown in seconds before the cooldown rate: (base - flat perks) x perk scale, at least 0.5 s, x the cooldown stat.</summary>
    public double CooldownOf(string spellId, StatSheet stats) =>
        Math.Max(0.5, (_content.Spells[spellId].Cooldown - (CooldownReduction?.Invoke(spellId) ?? 0)) * (CooldownScale?.Invoke(spellId) ?? 1))
        * stats.Get(StatIds.Cooldown);

    /// <summary>Puts a spell in a slot. Equipping starts its full cooldown, as in the original.</summary>
    public bool Equip(int slot, string? spellId)
    {
        if (slot is < 0 or >= Slots) return false;
        if (spellId != null && !_content.Spells.ContainsKey(spellId)) return false;
        for (int i = 0; i < Slots; i++)
            if (spellId != null && _loadout[i] == spellId)
            {
                _loadout[i] = null;
                _active[i] = false;
            }
        _loadout[slot] = spellId;
        _ready[slot] = spellId == null ? 1 : 0;
        _active[slot] = false;
        return true;
    }

    public double CooldownProgress(int slot) => _ready[slot];

    public bool IsReady(int slot) => _ready[slot] >= 1;

    /// <summary>A channeled or toggle spell that is currently running (paying its upkeep).</summary>
    public bool IsActive(int slot) => slot is >= 0 and < Slots && _active[slot];

    private bool IsSustained(string id) => _content.Spells[id].Channeled || _content.Spells[id].Toggle;

    /// <summary>Advances cooldowns: progress += dt × cooldownRate / (cooldown × cooldownMult).</summary>
    public void Tick(double dt, StatSheet stats)
    {
        double rate = stats.Get(StatIds.CooldownRate);
        for (int i = 0; i < Slots; i++)
        {
            if (_loadout[i] is not string id || _ready[i] >= 1) continue;
            double cd = CooldownOf(id, stats);
            double r = rate * (CooldownRateScale?.Invoke(id, stats) ?? 1);
            _ready[i] = cd <= 0 ? 1 : Math.Min(1, _ready[i] + dt * r / cd);
        }
    }

    /// <summary>Multiplies the remaining cooldown of every slot by <paramref name="factor"/> (Infinity: ×0.95 on each cast).</summary>
    public void ScaleRemainingCooldowns(double factor)
    {
        for (int i = 0; i < Slots; i++)
            if (_loadout[i] != null && _ready[i] < 1) _ready[i] = 1 - (1 - _ready[i]) * Math.Clamp(factor, 0, 1);
    }

    private void OnCast(StatSheet stats)
    {
        if (stats.IsOn(PerkStatIds.Infinity)) ScaleRemainingCooldowns(PerkRules.InfinityFactor);
    }

    /// <summary>Takes <paramref name="seconds"/> off the remaining cooldown of one random spell that is cooling down (Infinity Loop). False if none is.</summary>
    public bool ReduceRandomCooldown(double seconds, StatSheet stats, Rng rng)
    {
        var cooling = Enumerable.Range(0, Slots).Where(i => _loadout[i] != null && _ready[i] < 1).ToList();
        if (cooling.Count == 0) return false;
        int slot = rng.Pick(cooling);
        double cd = CooldownOf(_loadout[slot]!, stats);
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

    /// <summary>
    /// Tries to cast the spell in a slot with the given energy. On success the cooldown restarts.
    /// Channeled and toggle spells work differently: the cast switches them on (needs one second of upkeep in energy, no cooldown
    /// check beyond being ready) and a second cast switches them off again, which starts their cooldown; while on they cost
    /// their energy cost every second (<see cref="SustainTick"/>).
    /// </summary>
    public CastResult TryCast(int slot, double energy, StatSheet stats, out SpellCost cost)
    {
        cost = default;
        if (slot is < 0 or >= Slots || _loadout[slot] is not string id) return CastResult.EmptySlot;
        if (IsSustained(id))
        {
            if (_active[slot])
            {
                Stop(slot);
                return CastResult.Stopped;
            }
            if (!IsReady(slot)) return CastResult.OnCooldown;
            if (energy < CostOf(id, stats).Energy) return CastResult.NotEnoughEnergy;
            _active[slot] = true;
            OnCast(stats);
            return CastResult.Ok;
        }
        if (!IsReady(slot)) return CastResult.OnCooldown;
        cost = CostOf(id, stats);
        if (energy < cost.Energy) return CastResult.NotEnoughEnergy;
        _ready[slot] = 0;
        OnCast(stats);
        return CastResult.Ok;
    }

    /// <summary>Switches a running spell off and starts its cooldown.</summary>
    public void Stop(int slot)
    {
        if (!IsActive(slot)) return;
        _active[slot] = false;
        _ready[slot] = 0;
    }

    /// <summary>What a frame of upkeep cost, and which slots ran out of energy and switched off.</summary>
    public readonly record struct SustainResult(double Spent, IReadOnlyList<int> Stopped);

    /// <summary>Charges the upkeep of every running spell for <paramref name="dt"/> seconds out of <paramref name="energy"/>
    /// (energy and stamina parts together). A spell that can't pay its slice switches off.</summary>
    public SustainResult SustainTick(double dt, double energy, StatSheet stats)
    {
        double spent = 0;
        List<int>? stopped = null;
        for (int i = 0; i < Slots; i++)
        {
            if (!_active[i] || _loadout[i] is not string id) continue;
            var c = CostOf(id, stats);
            double due = (c.Energy + c.Stamina) * dt;
            if (energy - spent < due)
            {
                Stop(i);
                (stopped ??= new()).Add(i);
                continue;
            }
            spent += due;
        }
        return new SustainResult(spent, stopped ?? (IReadOnlyList<int>)Array.Empty<int>());
    }

    public void Restore(IReadOnlyList<string?> loadout)
    {
        for (int i = 0; i < Slots; i++)
        {
            var id = i < loadout.Count ? loadout[i] : null;
            _loadout[i] = id != null && _content.Spells.ContainsKey(id) ? id : null;
            _ready[i] = 1;
            _active[i] = false;
        }
    }
}
