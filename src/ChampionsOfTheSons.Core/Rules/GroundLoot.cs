namespace ChampionsOfTheSons.Core.Rules;

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

    /// <summary>Seconds before an unclaimed drop disappears.</summary>
    public double Lifetime { get; set; } = 180;

    public int Count => _drops.Count;
    public IEnumerable<GroundDrop> All => _drops.Values;

    public event Action<GroundDrop>? Added;
    public event Action<GroundDrop, bool>? Removed; // (drop, pickedUp)

    public GroundDrop? Get(int id) => _drops.GetValueOrDefault(id);

    public GroundDrop Add(ItemInstance item, float x, float y, float z, int stackSize = 1)
    {
        var drop = new GroundDrop { Id = _nextId++, Item = item, X = x, Y = y, Z = z, StackSize = stackSize };
        _drops[drop.Id] = drop;
        Added?.Invoke(drop);
        return drop;
    }

    /// <summary>Client side of co-op: shows a drop the host created, under the host's id (<see cref="Add"/> would number it itself).</summary>
    public GroundDrop Mirror(int id, ItemInstance item, float x, float y, float z, int stackSize = 1)
    {
        Remove(id);
        var drop = new GroundDrop { Id = id, Item = item, X = x, Y = y, Z = z, StackSize = stackSize };
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

    /// <summary>Ages every drop and removes the ones past <see cref="Lifetime"/>.</summary>
    public void Tick(double dt)
    {
        List<int>? expired = null;
        foreach (var d in _drops.Values)
        {
            d.Age += dt;
            if (d.Age >= Lifetime) (expired ??= new()).Add(d.Id);
        }
        if (expired != null) foreach (var id in expired) Remove(id);
    }

    private static float Sq(float v) => v * v;
}
