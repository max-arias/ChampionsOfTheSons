using ChampionsOfTheSons.Core.Content;

namespace ChampionsOfTheSons.Core.Rules;

/// <summary>What can merge together: one item type at one rarity, or any Legendary (<see cref="AnyType"/>).</summary>
public readonly record struct MergeKind(string Type, Rarity Rarity)
{
    public bool AnyType => Rarity == Rarity.Legendary;
}

public enum MergeError
{
    None,
    /// <summary>Not exactly <see cref="Merger.Need"/> distinct bag slots.</summary>
    WrongCount,
    /// <summary>A slot is empty, not in the bag, or holds an unknown or non-equipment item.</summary>
    NotMergeable,
    /// <summary>The items are not all of one kind.</summary>
    MixedKinds,
    /// <summary>No base exists for the result (content gap).</summary>
    NoBase,
}

public sealed record MergeResult(MergeError Error, ItemInstance? Item = null, string? Slot = null)
{
    public bool Ok => Error == MergeError.None;
}

/// <summary>
/// COS-144 (Max 2026-11-20): several bag items of one kind become one item of the next rarity. 3 Common → 1 Magic, 5 Magic → 1 Rare,
/// 7 Rare → 1 Legendary, all of one item type; 3 Legendary of any types → 1 random Legendary of any type, never one of the input bases.
/// The result is a fresh roll at the lowest input level. Local to a player's bag: no host involvement.
/// </summary>
public sealed class Merger
{
    private static readonly int[] Needed = { 3, 5, 7, 3 };

    private readonly ContentDb _content;
    private readonly LootRoller _loot;

    public Merger(ContentDb content, LootRoller loot)
    {
        _content = content;
        _loot = loot;
    }

    public static int Need(Rarity rarity) => Needed[Rarities.Clamp((int)rarity)];

    /// <summary>The kind an item merges as; null for items that cannot merge (unknown, not equipment).</summary>
    public MergeKind? KindOf(ItemInstance? item)
    {
        if (item == null || !_content.Items.TryGetValue(item.BaseId, out var def) || !LootRoller.IsEquippable(def)) return null;
        return new MergeKind(item.Rarity == Rarity.Legendary ? "*" : def.Type, item.Rarity);
    }

    public bool Matches(ItemInstance? item, MergeKind kind) => KindOf(item) == kind;

    /// <summary>Bag slots holding items of the kind, highest level first (then bag order).</summary>
    public IReadOnlyList<string> Matching(Inventory inventory, MergeKind kind) =>
        inventory.Slots
            .Where(kv => IsBagSlot(kv.Key) && Matches(kv.Value, kind))
            .OrderByDescending(kv => kv.Value.Level)
            .ThenBy(kv => BagIndex(kv.Key))
            .Select(kv => kv.Key)
            .ToList();

    /// <summary>The best batch: the <see cref="Need"/> highest-level matching items, fewer when the bag holds fewer.</summary>
    public IReadOnlyList<string> AutoPick(Inventory inventory, MergeKind kind) => Matching(inventory, kind).Take(Need(kind.Rarity)).ToList();

    /// <summary>
    /// Merges the items in <paramref name="slots"/>. The result goes into <paramref name="resultSlot"/> (one of the slots; default the
    /// first). On any error nothing changes.
    /// </summary>
    public MergeResult Merge(Inventory inventory, IReadOnlyList<string> slots, Rng rng, string? resultSlot = null)
    {
        var distinct = slots.Distinct().ToList();
        if (distinct.Count == 0) return new MergeResult(MergeError.WrongCount);
        var items = new List<ItemInstance>();
        foreach (var slot in distinct)
        {
            var item = inventory.Get(slot);
            if (!IsBagSlot(slot) || item == null || KindOf(item) == null) return new MergeResult(MergeError.NotMergeable);
            items.Add(item);
        }
        var kind = KindOf(items[0])!.Value;
        if (items.Any(i => KindOf(i) != kind)) return new MergeResult(MergeError.MixedKinds);
        if (distinct.Count != Need(kind.Rarity)) return new MergeResult(MergeError.WrongCount);

        int level = items.Min(i => i.Level);
        var target = ResultBase(kind, items.Select(i => i.BaseId).ToHashSet(), level, rng);
        if (target == null) return new MergeResult(MergeError.NoBase);

        var result = _loot.RollItem(target, level, rng);
        string into = resultSlot != null && distinct.Contains(resultSlot) ? resultSlot : distinct[0];
        var kept = inventory.Slots.Where(kv => !distinct.Contains(kv.Key)).ToList();
        kept.Add(new(into, result));
        inventory.Restore(kept);
        return new MergeResult(MergeError.None, result, into);
    }

    /// <summary>
    /// A base of the next rarity: same item type, or for the Legendary row any Legendary not among the inputs. Bases whose minLevel
    /// is at most the result level are preferred; when none is, the ones with the lowest minLevel are used.
    /// </summary>
    private ItemBaseDef? ResultBase(MergeKind kind, HashSet<string> inputBases, int level, Rng rng)
    {
        int rarity = Math.Min((int)kind.Rarity + 1, Rarities.Max);
        var pool = _content.Items.Values
            .Where(d => d.Rarity == rarity && LootRoller.IsEquippable(d))
            .Where(d => kind.AnyType ? !inputBases.Contains(d.Id) : d.Type == kind.Type)
            .ToList();
        if (pool.Count == 0) return null;
        var fitting = pool.Where(d => d.MinLevel <= level).ToList();
        if (fitting.Count == 0)
        {
            int lowest = pool.Min(d => d.MinLevel);
            fitting = pool.Where(d => d.MinLevel == lowest).ToList();
        }
        return rng.Pick(fitting.OrderBy(d => d.Id, StringComparer.Ordinal).ToList());
    }

    public static bool IsBagSlot(string slot) => slot.StartsWith("bag.", StringComparison.Ordinal);

    private static int BagIndex(string slot) => int.TryParse(slot.AsSpan(4), out var i) ? i : int.MaxValue;
}
