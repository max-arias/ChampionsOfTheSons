using ChampionsOfTheSons.Core.Content;

namespace ChampionsOfTheSons.Core.Rules;

/// <summary>A resolved change to one player stat from one source.</summary>
public readonly record struct StatModifier(string Stat, EffectOp Op, double Value);

/// <summary>
/// The player's stats. Every contribution belongs to a named source ("equip.ring1", "perk.x", "buff.y", "level");
/// replacing or removing a source recomputes from scratch, so values never drift the way add/subtract pairs can.
/// Value of a stat = (default + Σ adds) × defaultMult × Π mults ÷ Π divs. A bool stat is on when its default
/// is true or at least one source enables it and none disables it.
/// </summary>
public sealed class StatSheet
{
    private readonly ContentDb _content;
    private readonly Dictionary<string, IReadOnlyList<StatModifier>> _sources = new();
    private Dictionary<string, double>? _cache;

    public StatSheet(ContentDb content) => _content = content;

    public event Action? Changed;

    public IEnumerable<string> Sources => _sources.Keys;

    /// <summary>Sets (or replaces) all modifiers from one source.</summary>
    public void SetSource(string sourceId, IReadOnlyList<StatModifier> modifiers)
    {
        _sources[sourceId] = modifiers;
        Invalidate();
    }

    public bool RemoveSource(string sourceId)
    {
        if (!_sources.Remove(sourceId)) return false;
        Invalidate();
        return true;
    }

    public void Clear()
    {
        _sources.Clear();
        Invalidate();
    }

    /// <summary>Resolved value. Unknown stats are 0. Bool stats are 1 or 0.</summary>
    public double Get(string statId) => Resolve().TryGetValue(statId, out var v) ? v : 0;

    public bool IsOn(string statId) => Get(statId) != 0;

    /// <summary>Value before any sources were added (the stat's default).</summary>
    public double GetDefault(string statId)
    {
        if (!_content.PlayerStats.TryGetValue(statId, out var def)) return 0;
        return def.Kind == PlayerStatKind.Bool ? (def.Default != 0 ? 1 : 0) : def.Default * def.DefaultMult;
    }

    private void Invalidate()
    {
        _cache = null;
        Changed?.Invoke();
    }

    private Dictionary<string, double> Resolve()
    {
        if (_cache != null) return _cache;

        var adds = new Dictionary<string, double>();
        var mults = new Dictionary<string, double>();
        var enables = new Dictionary<string, int>();
        var disables = new HashSet<string>();
        foreach (var list in _sources.Values)
        foreach (var m in list)
        {
            switch (m.Op)
            {
                case EffectOp.Add:
                    adds[m.Stat] = adds.GetValueOrDefault(m.Stat) + m.Value;
                    break;
                case EffectOp.Mult:
                    mults[m.Stat] = mults.GetValueOrDefault(m.Stat, 1) * m.Value;
                    break;
                case EffectOp.Div:
                    if (m.Value != 0) mults[m.Stat] = mults.GetValueOrDefault(m.Stat, 1) / m.Value;
                    break;
                case EffectOp.Enable:
                    enables[m.Stat] = enables.GetValueOrDefault(m.Stat) + 1;
                    break;
                case EffectOp.Disable:
                    disables.Add(m.Stat);
                    break;
            }
        }

        var result = new Dictionary<string, double>(_content.PlayerStats.Count);
        foreach (var def in _content.PlayerStats.Values)
        {
            if (def.Kind == PlayerStatKind.Bool)
            {
                bool on = !disables.Contains(def.Id) && (def.Default != 0 || enables.ContainsKey(def.Id));
                result[def.Id] = on ? 1 : 0;
                continue;
            }
            double v = (def.Default + adds.GetValueOrDefault(def.Id)) * def.DefaultMult * mults.GetValueOrDefault(def.Id, 1);
            if (def.Integer) v = Math.Round(v, MidpointRounding.AwayFromZero);
            result[def.Id] = v;
        }
        return _cache = result;
    }
}
