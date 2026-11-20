namespace ChampionsOfTheSons.Core;

/// <summary>Rarity tiers 0..3 (COS-109, Max 2026-11-20). The original mod had eight; see <see cref="Rarities.FromLegacy"/>.</summary>
public enum Rarity
{
    Common = 0,
    Magic = 1,
    Rare = 2,
    Legendary = 3,
}

public static class Rarities
{
    public const int Max = (int)Rarity.Legendary;

    /// <summary>The original eight tiers (Common, Uncommon, Magic, Rare, Epic, Legendary, Mythic, Primal) onto the four:
    /// Common/Uncommon → Common, Magic → Magic, Rare/Epic → Rare, Legendary/Mythic/Primal → Legendary.</summary>
    public static Rarity FromLegacy(int legacy) => legacy switch
    {
        <= 1 => Rarity.Common,
        2 => Rarity.Magic,
        3 or 4 => Rarity.Rare,
        _ => Rarity.Legendary,
    };

    public static int Clamp(int rarity) => Math.Clamp(rarity, 0, Max);
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

    /// <summary>Upgrade count. Nothing sets it since crafting was removed (COS-145); kept in saves and the net codec.</summary>
    public int Upgrades { get; init; }

    public bool Equals(ItemInstance? other) =>
        other is not null && BaseId == other.BaseId && Level == other.Level && Rarity == other.Rarity &&
        Amount == other.Amount && Upgrades == other.Upgrades && Stats.SequenceEqual(other.Stats);

    public override int GetHashCode() => HashCode.Combine(BaseId, Level, Rarity, Amount, Upgrades, Stats.Count);
}
