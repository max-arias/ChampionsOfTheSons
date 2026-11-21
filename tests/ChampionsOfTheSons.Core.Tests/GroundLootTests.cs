using ChampionsOfTheSons.Core.Rules;

namespace ChampionsOfTheSons.Core.Tests;

public class GroundLootTests
{
    private static ItemInstance Item(string id = "item.x", int amount = 1) => new() { BaseId = id, Level = 5, Amount = amount };

    [Fact]
    public void Pickup_moves_the_item_to_the_bag_and_removes_the_drop()
    {
        var ground = new GroundLoot();
        var inv = new Inventory();
        var drop = ground.Add(Item(), 1, 2, 3);
        Assert.Equal(PickupResult.Ok, ground.TryPickup(drop.Id, inv));
        Assert.Equal(0, ground.Count);
        Assert.Equal("item.x", inv.Get("bag.0")!.BaseId);
        Assert.Equal(PickupResult.Unknown, ground.TryPickup(drop.Id, inv));
    }

    [Fact]
    public void Full_bag_leaves_the_drop_on_the_ground()
    {
        var ground = new GroundLoot();
        var inv = new Inventory();
        for (int i = 0; i < Rules.LootRoller.BagSlots; i++) inv.AddToBag(Item("item.fill" + i));
        var drop = ground.Add(Item(), 0, 0, 0);
        Assert.Equal(PickupResult.BagFull, ground.TryPickup(drop.Id, inv));
        Assert.Equal(1, ground.Count);
    }

    [Fact]
    public void Partly_fitting_stack_keeps_its_remainder_on_the_ground()
    {
        var ground = new GroundLoot();
        var inv = new Inventory();
        for (int i = 0; i < Rules.LootRoller.BagSlots - 1; i++) inv.AddToBag(Item("item.fill" + i));
        inv.AddToBag(Item("item.mat", 8), 10);
        var drop = ground.Add(Item("item.mat", 5), 0, 0, 0, 10);
        Assert.Equal(PickupResult.BagFull, ground.TryPickup(drop.Id, inv));
        Assert.Equal(3, ground.Get(drop.Id)!.Item.Amount);
        Assert.Equal(10, inv.Get("bag." + (Rules.LootRoller.BagSlots - 1))!.Amount);
    }

    [Fact]
    public void Drops_expire_and_are_found_by_distance()
    {
        var ground = new GroundLoot();
        var near = ground.Add(Item(), 1, 0, 0);
        double life = GroundLoot.LifetimeFor(DropSource.Enemy, near.Item.Rarity);
        ground.Add(Item(), 9, 0, 0);
        Assert.Equal(new[] { near.Id }, ground.Near(0, 0, 0, 1.5f));
        var removed = new List<int>();
        ground.Removed += (d, picked) => { if (!picked) removed.Add(d.Id); };
        ground.Tick(life - 1);
        Assert.Equal(2, ground.Count);
        ground.Tick(2);
        Assert.Equal(0, ground.Count);
        Assert.Equal(2, removed.Count);
    }
}
