namespace ChampionsOfTheSons.Core;

/// <summary>Rarity tiers 0..7, as in the original mod.</summary>
public enum Rarity
{
    Common = 0,
    Uncommon = 1,
    Magic = 2,
    Rare = 3,
    Epic = 4,
    Legendary = 5,
    Mythic = 6,
    Primal = 7,
}

/// <summary>One rolled stat on an item instance. <paramref name="Slot"/> is the index of the base item's stat pool it came from
/// (-1 for sockets and for stats whose slot is not known yet; see <see cref="Rules.StatSlots.Infer"/>).</summary>
public sealed record RolledStat(string StatId, float Value, int Slot = -1);

/// <summary>
/// A concrete item: a base definition plus its rolled level, rarity and stats.
/// Identified by content <see cref="BaseId"/> (stable string), never by index.
/// </summary>
public sealed record ItemInstance
{
    public string BaseId { get; init; } = "";
    public int Level { get; init; } = 1;
    public Rarity Rarity { get; init; }
    public int Amount { get; init; } = 1;
    public IReadOnlyList<RolledStat> Stats { get; init; } = Array.Empty<RolledStat>();

    /// <summary>Times the item was empowered/polished etc. Kept generic for crafting.</summary>
    public int Upgrades { get; init; }

    public bool Equals(ItemInstance? other) =>
        other is not null && BaseId == other.BaseId && Level == other.Level && Rarity == other.Rarity &&
        Amount == other.Amount && Upgrades == other.Upgrades && Stats.SequenceEqual(other.Stats);

    public override int GetHashCode() => HashCode.Combine(BaseId, Level, Rarity, Amount, Upgrades, Stats.Count);
}
