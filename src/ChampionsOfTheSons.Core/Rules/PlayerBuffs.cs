using ChampionsOfTheSons.Core.Content;

namespace ChampionsOfTheSons.Core.Rules;

/// <summary>How a buff's amount changes when the same source applies it again (COS-89, after COTF).</summary>
public enum BuffStack
{
    /// <summary>The new amount replaces the old.</summary>
    Overwrite,
    /// <summary>The new amount is added to the old (poison, armor corruption, energy leak).</summary>
    Accumulate,
    /// <summary>The larger amount stays.</summary>
    Max,
    /// <summary>The smaller amount stays (speed multipliers: the strongest slow).</summary>
    Min,
}

/// <summary>A player buff or debuff kind (content/buffs.json).</summary>
public sealed class BuffDef
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    /// <summary>Good for the player (shown white on the HUD) or bad (red, can be resisted and dispelled).</summary>
    public bool Positive { get; set; }
    /// <summary>Negative buffs: debuff immunity blocks and ends level 2 or less; Purge ends its own level or less.</summary>
    public int DispelLevel { get; set; } = 1;
    public BuffStack Stack { get; set; }
    /// <summary>A player stat that, while on, blocks this buff (stun immunity blocks stun).</summary>
    public string? BlockedBy { get; set; }
    /// <summary>Stat effects while active; an effect without a fixed value uses the buff's amount (times its scale).</summary>
    public List<StatEffect> Effects { get; set; } = new();
    /// <summary>Icon id under content/icons (HUD row).</summary>
    public string? Icon { get; set; }
}

/// <summary>What happened to an <see cref="PlayerBuffs.Apply"/>.</summary>
public enum BuffApply
{
    Applied,
    Refreshed,
    /// <summary>Blocked by an immunity stat (<see cref="BuffDef.BlockedBy"/>).</summary>
    Immune,
    /// <summary>Blocked by debuff resistance or debuff immunity.</summary>
    Resisted,
    UnknownBuff,
}

/// <summary>One active buff from one source, for the HUD.</summary>
public readonly record struct ActiveBuff(string BuffId, string Source, double Amount, double Left, double Duration, bool Positive);

/// <summary>
/// The player's buffs and debuffs (COS-89). Each active buff is keyed by buff id and source; applying the same pair again refreshes
/// the time (the longer stays) and stacks the amount by the buff's rule. Every active buff is one stat-sheet source, so removing it
/// restores the stats exactly. Negative buffs last shorter with debuff resistance (<see cref="SpellTuning.DebuffSeconds"/>, at most
/// −75%) and are blocked by debuff immunity up to dispel level 2 (<see cref="ImmunityLevel"/>); gaining immunity also ends them.
/// Buffs without a definition (spell buffs with their own modifiers) are positive and never dispelled.
/// </summary>
public sealed class PlayerBuffs
{
    public const int ImmunityLevel = 2;
    private const string Prefix = "buff.";

    private sealed class Entry
    {
        public BuffDef? Def;
        public double Amount, Left, Duration;
        public IReadOnlyList<StatModifier>? Raw;
    }

    private readonly StatSheet _stats;
    private readonly IReadOnlyDictionary<string, BuffDef> _defs;
    private readonly Dictionary<(string Buff, string Source), Entry> _active = new();

    public PlayerBuffs(StatSheet stats, IReadOnlyDictionary<string, BuffDef> defs)
    {
        _stats = stats;
        _defs = defs;
    }

    /// <summary>Raised when a buff ends (ran out, dispelled, resisted later, removed).</summary>
    public event Action<string, string>? Ended;

    public int Count => _active.Count;

    public bool Has(string buffId) => _active.Keys.Any(k => k.Buff == buffId);

    public bool Has(string buffId, string source) => _active.ContainsKey((buffId, source));

    /// <summary>Number of sources a buff is active from (two poisons slow the player).</summary>
    public int CountOf(string buffId) => _active.Keys.Count(k => k.Buff == buffId);

    /// <summary>Sum of a buff's amounts over its sources (poison damage, armor lost, energy leaked per second).</summary>
    public double Total(string buffId) => _active.Where(kv => kv.Key.Buff == buffId).Sum(kv => kv.Value.Amount);

    /// <summary>Product of a buff's amounts over its sources (speed multipliers from several slows).</summary>
    public double Product(string buffId) => _active.Where(kv => kv.Key.Buff == buffId).Aggregate(1.0, (m, kv) => m * kv.Value.Amount);

    public double Remaining(string buffId, string source) => _active.TryGetValue((buffId, source), out var e) ? e.Left : 0;

    public IReadOnlyList<ActiveBuff> Snapshot() =>
        _active.Select(kv => new ActiveBuff(kv.Key.Buff, kv.Key.Source, kv.Value.Amount, kv.Value.Left, kv.Value.Duration, kv.Value.Def?.Positive ?? true))
            .OrderByDescending(b => b.Positive).ThenBy(b => b.BuffId, StringComparer.Ordinal).ToList();

    /// <summary>Starts or refreshes <paramref name="buffId"/> from <paramref name="source"/>.</summary>
    public BuffApply Apply(string buffId, string source, double seconds, double amount = 1)
    {
        if (!_defs.TryGetValue(buffId, out var def)) return BuffApply.UnknownBuff;
        if (seconds <= 0) return BuffApply.Resisted;
        if (def.BlockedBy != null && _stats.IsOn(def.BlockedBy)) return BuffApply.Immune;
        if (!def.Positive)
        {
            if (Blocks(def.DispelLevel)) return BuffApply.Resisted;
            seconds *= 1 - Math.Clamp(_stats.Get(StatIds.DebuffResistance), 0, SpellTuning.MaxDebuffResistance); // immunity is checked by level above
            if (seconds <= 0) return BuffApply.Resisted;
        }
        var key = (buffId, source);
        bool refreshed = _active.TryGetValue(key, out var e);
        if (e == null)
        {
            e = new Entry { Def = def, Amount = amount };
            _active[key] = e;
        }
        else
        {
            e.Amount = def.Stack switch
            {
                BuffStack.Accumulate => e.Amount + amount,
                BuffStack.Max => Math.Max(e.Amount, amount),
                BuffStack.Min => Math.Min(e.Amount, amount),
                _ => amount,
            };
        }
        e.Left = Math.Max(e.Left, seconds);
        e.Duration = Math.Max(e.Duration, e.Left);
        Write(key, e);
        if (def.Positive) EnforceResistance();
        return refreshed ? BuffApply.Refreshed : BuffApply.Applied;
    }

    /// <summary>A buff with its own modifiers and no definition (spell buffs): positive, refreshes to the longer time, modifiers replaced.</summary>
    public void ApplyRaw(string id, double seconds, IReadOnlyList<StatModifier> modifiers)
    {
        if (seconds <= 0) return;
        var key = (id, "");
        if (!_active.TryGetValue(key, out var e)) _active[key] = e = new Entry();
        e.Raw = modifiers;
        e.Left = Math.Max(e.Left, seconds);
        e.Duration = Math.Max(e.Duration, e.Left);
        Write(key, e);
        EnforceResistance();
    }

    public bool Remove(string buffId, string source = "")
    {
        if (!_active.Remove((buffId, source))) return false;
        _stats.RemoveSource(SourceId(buffId, source));
        Ended?.Invoke(buffId, source);
        return true;
    }

    /// <summary>Ends every source of a buff.</summary>
    public int RemoveAll(string buffId)
    {
        var keys = _active.Keys.Where(k => k.Buff == buffId).ToList();
        foreach (var k in keys) Remove(k.Buff, k.Source);
        return keys.Count;
    }

    /// <summary>Ends every negative buff of dispel level ≤ <paramref name="level"/> (Purge, death). Returns how many.</summary>
    public int Dispel(int level)
    {
        var keys = _active.Where(kv => kv.Value.Def is { Positive: false } d && d.DispelLevel <= level).Select(kv => kv.Key).ToList();
        foreach (var k in keys) Remove(k.Buff, k.Source);
        return keys.Count;
    }

    public void Clear()
    {
        foreach (var k in _active.Keys.ToList()) Remove(k.Buff, k.Source);
    }

    /// <summary>Counts buffs down (only those <paramref name="only"/> accepts, when given: each buff ages on the loop that applies it);
    /// removes the ones that ran out and debuffs a new immunity now blocks.</summary>
    public void Tick(double dt, Func<string, bool>? only = null)
    {
        List<(string, string)>? gone = null;
        foreach (var (key, e) in _active)
        {
            if (only != null && !only(key.Buff)) continue;
            e.Left -= dt;
            if (e.Left <= 0) (gone ??= new()).Add(key);
        }
        if (gone != null) foreach (var (b, s) in gone) Remove(b, s);
        EnforceResistance();
    }

    private bool Blocks(int dispelLevel) => _stats.Get(StatIds.DebuffImmunity) > 0 && dispelLevel <= ImmunityLevel;

    private void EnforceResistance()
    {
        var keys = _active.Where(kv => kv.Value.Def is { Positive: false } d && (Blocks(d.DispelLevel) || (d.BlockedBy != null && _stats.IsOn(d.BlockedBy))))
            .Select(kv => kv.Key).ToList();
        foreach (var k in keys) Remove(k.Buff, k.Source);
    }

    private static string SourceId(string buffId, string source) => source.Length == 0 ? Prefix + buffId : $"{Prefix}{buffId}.{source}";

    private void Write((string Buff, string Source) key, Entry e)
    {
        IReadOnlyList<StatModifier> mods = e.Raw ?? e.Def!.Effects.Select(fx => new StatModifier(fx.Stat, fx.Op, fx.Value ?? e.Amount * fx.Scale)).ToList();
        _stats.SetSource(SourceId(key.Buff, key.Source), mods);
    }
}
