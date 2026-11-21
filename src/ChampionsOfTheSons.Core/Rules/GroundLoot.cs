namespace ChampionsOfTheSons.Core.Rules;

/// <summary>Where a ground drop came from; sets how long it lies there (<see cref="GroundLoot.LifetimeFor"/>).</summary>
public enum DropSource : byte
{
    /// <summary>Dropped by a killed enemy.</summary>
    Enemy,
    /// <summary>Thrown out of a player's inventory.</summary>
    Thrown,
    /// <summary>Did not fit a full bag (a reward or a co-op pickup remainder).</summary>
    Overflow,
}

/// <summary>One item lying in the world. Positions are plain floats so the class stays game-agnostic.</summary>
public sealed class GroundDrop
{
    public int Id { get; init; }
    public ItemInstance Item { get; set; } = new();
    public float X { get; init; }
    public float Y { get; init; }
    public float Z { get; init; }

    /// <summary>Seconds the drop has lain on the ground.</summary>
    public double Age { get; set; }

    /// <summary>Seconds it lies there in all before it disappears.</summary>
    public double Lifetime { get; init; } = GroundLoot.LifetimeFor(DropSource.Enemy, Rarity.Common);

    public double Remaining => Math.Max(0, Lifetime - Age);

    /// <summary>Stack size of the base item, kept so pickup can stack without a content lookup.</summary>
    public int StackSize { get; init; } = 1;
}

public enum PickupResult { Ok, BagFull, Unknown }

/// <summary>
/// Authoritative list of items on the ground (host / single-player). The game bridge renders it; the network layer
/// (COS-14) mirrors <see cref="Added"/> / <see cref="Removed"/> to clients and applies their pickup requests via <see cref="TryPickup"/>.
/// </summary>
public sealed class GroundLoot
{
    private readonly Dictionary<int, GroundDrop> _drops = new();
    private int _nextId = 1;

    /// <summary>COTF's lifetime by source (enemy 120 s, thrown 60 s, death or overflow 900 s) plus 2 × tier² seconds, where the tier is
    /// the old eight-step rarity each of the four mostly came from (Common 0, Magic 2, Rare 3, Legendary 5): +0, +8, +18, +50 s.</summary>
    public static double LifetimeFor(DropSource source, Rarity rarity)
    {
        double baseSeconds = source switch { DropSource.Thrown => 60, DropSource.Overflow => 900, _ => 120 };
        int tier = LegacyTier[Rarities.Clamp((int)rarity)];
        return baseSeconds + 2 * tier * tier;
    }

    private static readonly int[] LegacyTier = { 0, 2, 3, 5 };

    public int Count => _drops.Count;
    public IEnumerable<GroundDrop> All => _drops.Values;

    public event Action<GroundDrop>? Added;
    public event Action<GroundDrop, bool>? Removed; // (drop, pickedUp)

    public GroundDrop? Get(int id) => _drops.GetValueOrDefault(id);

    public GroundDrop Add(ItemInstance item, float x, float y, float z, int stackSize = 1, DropSource source = DropSource.Enemy)
    {
        var drop = new GroundDrop { Id = _nextId++, Item = item, X = x, Y = y, Z = z, StackSize = stackSize, Lifetime = LifetimeFor(source, item.Rarity) };
        _drops[drop.Id] = drop;
        Added?.Invoke(drop);
        return drop;
    }

    /// <summary>Client side of co-op: shows a drop the host created, under the host's id (<see cref="Add"/> would number it itself),
    /// with the seconds it has left on the host (null: a fresh enemy drop).</summary>
    public GroundDrop Mirror(int id, ItemInstance item, float x, float y, float z, int stackSize = 1, double? remaining = null)
    {
        Remove(id);
        var drop = new GroundDrop { Id = id, Item = item, X = x, Y = y, Z = z, StackSize = stackSize, Lifetime = remaining ?? LifetimeFor(DropSource.Enemy, item.Rarity) };
        _drops[id] = drop;
        Added?.Invoke(drop);
        return drop;
    }

    /// <summary>Removes a drop without giving it to anyone (destroyed or despawned).</summary>
    public bool Remove(int id)
    {
        if (!_drops.Remove(id, out var drop)) return false;
        Removed?.Invoke(drop, false);
        return true;
    }

    /// <summary>Moves a drop into the bag. A full bag leaves the drop on the ground; a partly stacked drop keeps its remainder.</summary>
    public PickupResult TryPickup(int id, Inventory inventory)
    {
        if (!_drops.TryGetValue(id, out var drop)) return PickupResult.Unknown;
        if (!inventory.AddToBag(drop.Item, drop.StackSize, out var remainder))
        {
            if (remainder != null) drop.Item = remainder; // part of a stack fitted, the rest stays
            return PickupResult.BagFull;
        }
        _drops.Remove(id);
        Removed?.Invoke(drop, true);
        return PickupResult.Ok;
    }

    /// <summary>Ids of drops within <paramref name="radius"/> of a point.</summary>
    public List<int> Near(float x, float y, float z, float radius)
    {
        float r2 = radius * radius;
        return _drops.Values.Where(d => Sq(d.X - x) + Sq(d.Y - y) + Sq(d.Z - z) <= r2).Select(d => d.Id).ToList();
    }

    /// <summary>Ages every drop and removes the ones past their <see cref="GroundDrop.Lifetime"/>.</summary>
    public void Tick(double dt)
    {
        List<int>? expired = null;
        foreach (var d in _drops.Values)
        {
            d.Age += dt;
            if (d.Age >= d.Lifetime) (expired ??= new()).Add(d.Id);
        }
        if (expired != null) foreach (var id in expired) Remove(id);
    }

    private static float Sq(float v) => v * v;
}
