using ChampionsOfTheSons.Core.Content;

namespace ChampionsOfTheSons.Core.Rules;

/// <summary>Drops, rarity rolls and item generation.</summary>
public sealed class LootRoller
{
    /// <summary>Roll multiplier per rarity 0..7.</summary>
    public static readonly double[] RarityMultiplier = { 0.5, 0.7, 1, 1.4, 2.3, 3.4, 4.5, 5.6 };

    /// <summary>COTF's common stat IDs, used for "*" slots.</summary>
    private static readonly HashSet<int> CommonLegacyStatIds = new(
        Enumerable.Range(1, 18).Concat(Enumerable.Range(21, 6)).Append(31).Concat(Enumerable.Range(34, 14))
            .Concat(Enumerable.Range(49, 9)).Concat(Enumerable.Range(59, 9)));

    public const string EmptySocketStat = "stat.emptysocket";

    private readonly ContentDb _content;
    private readonly ItemStatDef[] _commonStats;
    private readonly Dictionary<int, ItemBaseDef[]> _byRarity;

    public LootRoller(ContentDb content)
    {
        _content = content;
        _commonStats = content.ItemStats.Values.Where(s => CommonLegacyStatIds.Contains(s.LegacyId)).OrderBy(s => s.LegacyId).ToArray();
        _byRarity = content.Items.Values.GroupBy(i => i.Rarity).ToDictionary(g => g.Key, g => g.OrderBy(i => i.LegacyIndex).ToArray());
    }

    /// <summary>Does this kill drop anything? Enemies with abilities always drop.</summary>
    public static bool ShouldDrop(EnemyProfile enemy, WorldRules rules, double magicFind, Rng rng) =>
        enemy.Abilities.Count > 0 || rng.NextDouble() <= 0.1 * rules.DropChance * magicFind;

    public static int DropCount(EnemyProfile enemy, WorldRules rules, Rng rng)
    {
        int count = rng.RangeInclusive(2, 3);
        if (enemy.Abilities.Count >= 3) count += rng.RangeInclusive(2, 4);
        if (enemy.Tier == EnemyTier.Boss) count += 7;
        else if (enemy.Tier == EnemyTier.Miniboss) count += 3;
        count += Math.Clamp(enemy.Level / 50, 1, 8);
        return Math.Max(0, (int)Math.Round(count * rules.DropQuantity, MidpointRounding.AwayFromZero));
    }

    /// <summary>Climbing rarity roll: each step must pass to try the next. <paramref name="worth"/> is the enemy bounty.</summary>
    public static int RollRarity(double worth, int itemLevel, double magicFind, int difficulty, Rng rng)
    {
        double w = worth / Math.Max(1, itemLevel) * magicFind;
        double mf = Math.Sqrt(Math.Max(0, magicFind)) - 1;
        int d = difficulty;
        double U() => rng.NextDouble();

        if (!((w > 20 && U() < 0.70 + 0.45 * mf + 0.075 * d) || (d > 5 && w > 2000))) return 0;
        if (!(w > 80 && (U() < 0.50 + 0.4 * mf + 0.07 * d || (w > 2200 && d > 6)))) return 1;
        if (!((w > 180 && U() < 0.5 + 0.35 * mf + 0.05 * d) || (w > 5000 && d > 7 && U() < 0.9))) return 2;
        if (!(d > 0 || U() < 0.05)) return 2;
        if (!((w > 360 && U() < 0.5 + 0.22 * mf + 0.034 * d) || (d > 8 && U() < 0.7))) return 3;
        if (!(d > 1 || U() < 0.02)) return 3;
        if (!(w > 720 && U() < 0.26 + 0.085 * mf + 0.02 * d)) return 4;
        if (!(d > 2 || U() < 0.01)) return 4;
        if (!(w > 1440 && U() < 0.18 + 0.033 * mf + 0.003 * d)) return 5;
        if (!(d > 3 || U() < 0.05)) return 5;
        if (!(w > 5000 && U() < 0.04 + 0.01 * mf)) return 6;
        if (!(d > 4 || U() < 0.001)) return 6;
        return 7;
    }

    /// <summary>Uniform base item of that rarity with minLevel ≤ level; falls back one rarity at a time.</summary>
    public ItemBaseDef? PickBase(int rarity, int level, Rng rng, Func<ItemBaseDef, bool>? filter = null)
    {
        for (int r = Math.Clamp(rarity, 0, 7); r >= 0; r--)
        {
            if (!_byRarity.TryGetValue(r, out var items)) continue;
            var pool = items.Where(i => i.MinLevel <= level && (filter == null || filter(i))).ToList();
            if (pool.Count > 0) return rng.Pick(pool);
        }
        return null;
    }

    /// <summary>Rolls everything a drop needs: rarity, base, stats and sockets.</summary>
    public ItemInstance? RollDrop(double worth, int itemLevel, double magicFind, int difficulty, Rng rng, int minRarity = 0)
    {
        int rarity = RollRarity(worth, itemLevel, magicFind, difficulty, rng);
        var baseDef = PickBase(rarity, itemLevel, rng, IsDroppable);
        if (baseDef == null || baseDef.Rarity < minRarity) return null;
        return RollItem(baseDef, itemLevel, rng);
    }

    public static bool IsMaterialOrOther(ItemBaseDef d) => d.Type is "material" or "other";

    /// <summary>What enemies can drop. First pass: equipment only; materials and consumables stay defined but unused (COS-59).</summary>
    public static bool IsDroppable(ItemBaseDef d) => IsEquippable(d);

    /// <summary>A fresh instance of <paramref name="baseDef"/> at <paramref name="level"/>.</summary>
    public ItemInstance RollItem(ItemBaseDef baseDef, int level, Rng rng)
    {
        if (IsMaterialOrOther(baseDef)) level = 1;
        var stats = new List<RolledStat>();
        foreach (var pool in baseDef.StatPools)
        {
            var option = pool.Count == 0 ? null : rng.Pick(pool);
            var def = ResolveOption(option, rng);
            if (def != null) stats.Add(new RolledStat(def.Id, (float)RollValue(def, level, baseDef.Rarity, rng)));
        }
        if (IsEquippable(baseDef) && level > 20 && rng.NextDouble() <= 0.175 && _content.ItemStats.ContainsKey(EmptySocketStat))
        {
            int sockets = rng.RangeInclusive(1, MaxSockets(baseDef.Type));
            for (int i = 0; i < sockets; i++) stats.Add(new RolledStat(EmptySocketStat, 0));
        }
        return new ItemInstance { BaseId = baseDef.Id, Level = level, Rarity = (Rarity)baseDef.Rarity, Stats = stats };
    }

    private ItemStatDef? ResolveOption(string? option, Rng rng)
    {
        if (option == null) return null;
        if (option == ItemBaseDef.AnyStat) return _commonStats.Length == 0 ? null : rng.Pick(_commonStats);
        return _content.ItemStats.TryGetValue(option, out var def) ? def : null;
    }

    /// <summary>The lowest and highest value <see cref="RollValue"/> can give for a stat at a level and rarity (crafting preview).</summary>
    public static (double Min, double Max) ValueRange(ItemStatDef stat, int level, int rarity)
    {
        double Value(double u)
        {
            double v = u * Math.Pow(Math.Max(1, level), stat.LevelPow);
            if (stat.Cap is double cap) v = Math.Min(v, cap);
            v *= stat.Multiplier;
            v *= RarityMultiplier[Math.Clamp(rarity, 0, 7)];
            if (stat.Cap is double cap2) v = Math.Min(v, cap2);
            return v;
        }
        return (Value(stat.Min), Value(stat.Max));
    }

    /// <summary>v = U(min,max) × level^pow, capped, × stat multiplier × rarity multiplier, capped again.</summary>
    public static double RollValue(ItemStatDef stat, int level, int rarity, Rng rng)
    {
        double v = rng.Range((float)stat.Min, (float)stat.Max) * Math.Pow(Math.Max(1, level), stat.LevelPow);
        if (stat.Cap is double cap) v = Math.Min(v, cap);
        v *= stat.Multiplier;
        v *= RarityMultiplier[Math.Clamp(rarity, 0, 7)];
        if (stat.Cap is double cap2) v = Math.Min(v, cap2);
        return v;
    }

    public static bool IsEquippable(ItemBaseDef d) => SlotsFor(d.Type).Count > 0;

    public static int MaxSockets(string type) => type switch
    {
        "bracer" or "pants" or "shield" or "quiver" or "spell_scroll" => 2,
        "chest_armor" => 3,
        _ => 1,
    };

    /// <summary>Equipment slot names an item type can go in.</summary>
    public static IReadOnlyList<string> SlotsFor(string type) => type switch
    {
        "helmet" => new[] { "equip.helmet" },
        "chest_armor" => new[] { "equip.chest" },
        "pants" => new[] { "equip.pants" },
        "boot" => new[] { "equip.boots" },
        "shoulder_armor" => new[] { "equip.shoulders" },
        "glove" => new[] { "equip.gloves" },
        "amulet" => new[] { "equip.amulet" },
        "bracer" => new[] { "equip.bracer" },
        "ring" => new[] { "equip.ring1", "equip.ring2" },
        "weapon" => new[] { "equip.weapon" },
        "shield" or "quiver" or "spell_scroll" => new[] { "equip.offhand" },
        _ => Array.Empty<string>(),
    };

    public static readonly IReadOnlyList<string> EquipSlots = new[]
    {
        "equip.helmet", "equip.chest", "equip.pants", "equip.boots", "equip.shoulders", "equip.gloves",
        "equip.amulet", "equip.bracer", "equip.ring1", "equip.ring2", "equip.weapon", "equip.offhand",
    };

    public const int BagSlots = 120;
}

/// <summary>Turns items into stat-sheet modifiers.</summary>
public static class ItemEffects
{
    /// <summary>All modifiers an equipped item applies: each rolled stat's effects plus the base item's unique effects.</summary>
    public static List<StatModifier> For(ItemInstance item, ContentDb content)
    {
        var mods = new List<StatModifier>();
        foreach (var rolled in item.Stats)
        {
            if (!content.ItemStats.TryGetValue(rolled.StatId, out var def)) continue;
            foreach (var e in def.Effects) mods.Add(ToModifier(e, rolled.Value));
        }
        if (content.Items.TryGetValue(item.BaseId, out var baseDef))
            foreach (var e in baseDef.UniqueEffects) mods.Add(ToModifier(e, 0));
        return mods;
    }

    public static StatModifier ToModifier(StatEffect e, double rolled) => new(e.Stat, e.Op, e.Resolve(rolled));

    /// <summary>Pieces equipped per set id.</summary>
    public static Dictionary<string, int> SetCounts(IEnumerable<ItemInstance> equipped, ContentDb content)
    {
        var counts = new Dictionary<string, int>();
        foreach (var item in equipped)
            if (content.Items.TryGetValue(item.BaseId, out var def) && def.Set != null)
                counts[def.Set] = counts.GetValueOrDefault(def.Set) + 1;
        return counts;
    }

    /// <summary>Modifiers from every set bonus whose piece count is reached; a per-piece bonus applies once per equipped piece.</summary>
    public static List<StatModifier> SetBonuses(IEnumerable<ItemInstance> equipped, ContentDb content)
    {
        var mods = new List<StatModifier>();
        foreach (var (setId, count) in SetCounts(equipped, content))
        {
            if (!content.Sets.TryGetValue(setId, out var set)) continue;
            foreach (var bonus in set.Bonuses)
                if (count >= bonus.Pieces)
                    for (int i = 0, times = bonus.PerPiece ? count : 1; i < times; i++)
                        foreach (var e in bonus.Effects) mods.Add(ToModifier(e, 0));
        }
        return mods;
    }
}
