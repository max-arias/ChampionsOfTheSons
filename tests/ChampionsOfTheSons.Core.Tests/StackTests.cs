using ChampionsOfTheSons.Core.Content;
using ChampionsOfTheSons.Core.Rules;

namespace ChampionsOfTheSons.Core.Tests;

/// <summary>COS-96: stack merge by drag, Split Stack and partial drop.</summary>
public class StackTests
{
    private static readonly ContentDb Db = TestContent.Create(new()
    {
        ["items.json"] = """
        { "items": [
          { "id": "item.potion", "name": "Potion", "type": "other", "rarity": 0, "minLevel": 1, "maxLevel": 50, "statPools": [], "stackSize": 10 }
        ] }
        """,
    });

    private static ItemInstance Potion(int n, int level = 5) => new() { BaseId = "item.potion", Level = level, Amount = n };

    [Fact]
    public void Dragging_a_stack_onto_an_equal_stack_merges_and_leaves_the_overflow()
    {
        var inv = new Inventory();
        inv.Restore(new Dictionary<string, ItemInstance> { ["bag.0"] = Potion(6), ["bag.1"] = Potion(7) });
        Assert.True(inv.MoveOrMerge("bag.0", "bag.1", Db));
        Assert.Equal(10, inv.Get("bag.1")!.Amount);
        Assert.Equal(3, inv.Get("bag.0")!.Amount);
    }

    [Fact]
    public void Different_items_or_levels_swap_instead_of_merging()
    {
        var inv = new Inventory();
        inv.Restore(new Dictionary<string, ItemInstance> { ["bag.0"] = Potion(2, level: 5), ["bag.1"] = Potion(3, level: 9) });
        Assert.True(inv.MoveOrMerge("bag.0", "bag.1", Db));
        Assert.Equal(9, inv.Get("bag.0")!.Level);
        Assert.Equal(2, inv.Get("bag.1")!.Amount);
    }

    [Fact]
    public void Split_moves_half_rounded_down_to_a_free_slot()
    {
        var inv = new Inventory();
        inv.Restore(new Dictionary<string, ItemInstance> { ["bag.0"] = Potion(7) });
        Assert.True(inv.Split("bag.0"));
        Assert.Equal(4, inv.Get("bag.0")!.Amount);
        Assert.Equal(3, inv.Get("bag.1")!.Amount);
        inv.Restore(new Dictionary<string, ItemInstance> { ["bag.0"] = Potion(1) });
        Assert.False(inv.Split("bag.0"));
    }

    [Fact]
    public void Take_removes_part_of_a_stack_or_all_of_it()
    {
        var inv = new Inventory();
        inv.Restore(new Dictionary<string, ItemInstance> { ["bag.0"] = Potion(5) });
        Assert.Equal(2, inv.Take("bag.0", 2)!.Amount);
        Assert.Equal(3, inv.Get("bag.0")!.Amount);
        Assert.Equal(3, inv.Take("bag.0", 9)!.Amount);
        Assert.Null(inv.Get("bag.0"));
    }

    [Fact]
    public void Stat_rolls_combine_by_their_rule()
    {
        Assert.Equal(0.3, StatCombineMath.Combine(StatCombine.Sum, new[] { 0.1, 0.2 }), 6);
        Assert.Equal(0.28, StatCombineMath.Combine(StatCombine.OneMinusProduct, new[] { 0.1, 0.2 }), 6); // 1 - 0.9 x 0.8
        Assert.Equal(0.32, StatCombineMath.Combine(StatCombine.ProductPlusOne, new[] { 0.1, 0.2 }), 6); // 1.1 x 1.2 - 1
    }
}
