namespace ChampionsOfTheSons.Core.Rules;

/// <summary>
/// Timed stat buffs from spells (War Cry, Berserk, Golden Skin...). Each buff is one stat-sheet source named
/// "spell.&lt;id&gt;", so removing it restores the stats exactly; casting it again refreshes the duration.
/// </summary>
public sealed class SpellBuffs
{
    private sealed class Buff
    {
        public double Left;
    }

    private const string Prefix = "spell.";
    private readonly StatSheet _stats;
    private readonly Dictionary<string, Buff> _buffs = new();

    public SpellBuffs(StatSheet stats) => _stats = stats;

    public int Count => _buffs.Count;

    public bool Has(string id) => _buffs.ContainsKey(id);

    public double Remaining(string id) => _buffs.TryGetValue(id, out var b) ? b.Left : 0;

    /// <summary>Starts or refreshes a buff: the modifiers are replaced and the time left is the longer of old and new.</summary>
    public void Apply(string id, double seconds, IReadOnlyList<StatModifier> modifiers)
    {
        if (seconds <= 0) return;
        if (_buffs.TryGetValue(id, out var b)) b.Left = Math.Max(b.Left, seconds);
        else _buffs[id] = new Buff { Left = seconds };
        _stats.SetSource(Prefix + id, modifiers);
    }

    public bool Remove(string id)
    {
        if (!_buffs.Remove(id)) return false;
        _stats.RemoveSource(Prefix + id);
        return true;
    }

    public void Clear()
    {
        foreach (var id in _buffs.Keys.ToList()) Remove(id);
    }

    /// <summary>Counts every buff down and removes the ones that ran out; returns their ids.</summary>
    public IReadOnlyList<string> Tick(double dt)
    {
        List<string>? gone = null;
        foreach (var (id, b) in _buffs)
        {
            b.Left -= dt;
            if (b.Left <= 0) (gone ??= new()).Add(id);
        }
        if (gone == null) return Array.Empty<string>();
        foreach (var id in gone) Remove(id);
        return gone;
    }
}
