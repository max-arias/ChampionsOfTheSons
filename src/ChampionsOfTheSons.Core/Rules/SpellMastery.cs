using ChampionsOfTheSons.Core.Content;

namespace ChampionsOfTheSons.Core.Rules;

/// <summary>
/// Spell mastery (COS-176, Max 2026-11-23): spells improve by being cast, no points. Each spell counts its successful casts;
/// mastery levels come at <see cref="Thresholds"/>. Every level makes that spell cheaper and its cooldown shorter, and the spell's
/// upgrade perks (<see cref="SpellDef.Mastery"/>, in order) unlock across the levels as granted perks (<see cref="PerkTree.Grant"/>).
/// </summary>
public sealed class SpellMastery
{
    /// <summary>Casts needed for mastery levels 1..5.</summary>
    public static readonly int[] Thresholds = { 5, 15, 35, 75, 150 };

    public static int MaxLevel => Thresholds.Length;

    /// <summary>Cost and cooldown cut per mastery level.</summary>
    public const double CutPerLevel = 0.04;

    private readonly ContentDb _content;
    private readonly Dictionary<string, int> _casts = new();

    public SpellMastery(ContentDb content) => _content = content;

    public IReadOnlyDictionary<string, int> Casts => _casts;

    public int CastsOf(string spellId) => _casts.TryGetValue(spellId, out var n) ? n : 0;

    public static int LevelFor(int casts)
    {
        int level = 0;
        while (level < Thresholds.Length && casts >= Thresholds[level]) level++;
        return level;
    }

    public int Level(string spellId) => LevelFor(CastsOf(spellId));

    /// <summary>Casts at the start of the next level (null at max), for the progress bar.</summary>
    public static int? NextThreshold(int level) => level < Thresholds.Length ? Thresholds[level] : null;

    /// <summary>Counts one successful cast; returns the new level when it went up.</summary>
    public int? AddCast(string spellId)
    {
        int before = Level(spellId);
        _casts[spellId] = CastsOf(spellId) + 1;
        int after = Level(spellId);
        return after > before ? after : null;
    }

    /// <summary>Admin and self-tests: sets a spell to exactly the casts of a mastery level.</summary>
    public void SetLevel(string spellId, int level)
    {
        level = Math.Clamp(level, 0, MaxLevel);
        if (level == 0) _casts.Remove(spellId);
        else _casts[spellId] = Thresholds[level - 1];
    }

    /// <summary>The mastery level at which the i-th of n upgrades unlocks: spread evenly over levels 1..5, earlier ones first.</summary>
    public static int UnlockLevel(int index, int count) => count <= 0 ? MaxLevel : 1 + index * MaxLevel / count;

    /// <summary>Every upgrade perk unlocked by the current mastery levels.</summary>
    public IEnumerable<string> Unlocked()
    {
        foreach (var spell in _content.Spells.Values)
        {
            int level = Level(spell.Id);
            if (level == 0) continue;
            for (int i = 0; i < spell.Mastery.Count; i++)
                if (UnlockLevel(i, spell.Mastery.Count) <= level) yield return spell.Mastery[i];
        }
    }

    /// <summary>Factor on the spell's energy cost and on its cooldown from mastery.</summary>
    public double Scale(string spellId) => 1 - CutPerLevel * Level(spellId);

    public void Restore(IReadOnlyDictionary<string, int>? saved)
    {
        _casts.Clear();
        if (saved == null) return;
        foreach (var (id, n) in saved)
            if (n > 0) _casts[id] = n;
    }
}
