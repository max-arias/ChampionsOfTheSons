using ChampionsOfTheSons.Core.Content;
using ChampionsOfTheSons.Core.Rules;

namespace ChampionsOfTheSons.Core.Tests;

public class MergeTests
{
    private const string Items = """
    { "items": [
      { "id": "item.boots_c", "name": "C Boots", "type": "boot", "rarity": 0, "minLevel": 1, "maxLevel": 5, "statPools": [ ["stat.max_life"] ] },
      { "id": "item.boots_m", "name": "M Boots", "type": "boot", "rarity": 1, "minLevel": 1, "maxLevel": 5, "statPools": [ ["stat.max_life"] ] },
      { "id": "item.boots_r_low", "name": "R Boots", "type": "boot", "rarity": 2, "minLevel": 24, "maxLevel": 30, "statPools": [ ["stat.max_life"] ] },
      { "id": "item.boots_r_high", "name": "R Boots 2", "type": "boot", "rarity": 2, "minLevel": 46, "maxLevel": 50, "statPools": [ ["stat.max_life"] ] },
      { "id": "item.ring_c", "name": "C Ring", "type": "ring", "rarity": 0, "minLevel": 1, "maxLevel": 5, "statPools": [ ["stat.max_life"] ] },
      { "id": "item.ring_m", "name": "M Ring", "type": "ring", "rarity": 1, "minLevel": 1, "maxLevel": 5, "statPools": [ ["stat.max_life"] ] },
      { "id": "item.leg_a", "name": "Leg A", "type": "ring", "rarity": 3, "minLevel": 1, "maxLevel": 5, "statPools": [ ["stat.max_life"] ] },
      { "id": "item.leg_b", "name": "Leg B", "type": "helmet", "rarity": 3, "minLevel": 1, "maxLevel": 5, "statPools": [ ["stat.max_life"] ] },
      { "id": "item.leg_c", "name": "Leg C", "type": "boot", "rarity": 3, "minLevel": 1, "maxLevel": 5, "statPools": [ ["stat.max_life"] ] },
      { "id": "item.leg_d", "name": "Leg D", "type": "pants", "rarity": 3, "minLevel": 1, "maxLevel": 5, "statPools": [ ["stat.max_life"] ] },
      { "id": "item.heart", "name": "Heart", "type": "other", "rarity": 0, "minLevel": 1, "maxLevel": 2, "statPools": [] }
    ] }
    """;

    private readonly ContentDb _db = TestContent.Create(new() { ["items.json"] = Items });
    private Merger Merger => new(_db, new LootRoller(_db));

    private Inventory Bag(params (string BaseId, int Level)[] items)
    {
        var inv = new Inventory();
        foreach (var (id, level) in items)
            inv.AddToBag(new ItemInstance { BaseId = id, Level = level, Rarity = (Rarity)_db.Items[id].Rarity });
        return inv;
    }

    [Theory]
    [InlineData(Rarity.Common, 3)]
    [InlineData(Rarity.Magic, 5)]
    [InlineData(Rarity.Rare, 7)]
    [InlineData(Rarity.Legendary, 3)]
    public void Each_row_needs_its_count(Rarity rarity, int need) => Assert.Equal(need, Merger.Need(rarity));

    [Fact]
    public void Three_commons_of_one_type_become_a_magic_of_that_type_at_the_lowest_level()
    {
        var inv = Bag(("item.boots_c", 9), ("item.boots_c", 4), ("item.boots_c", 7));
        var result = Merger.Merge(inv, new[] { "bag.0", "bag.1", "bag.2" }, new Rng(1), resultSlot: "bag.2");
        Assert.True(result.Ok);
        Assert.Equal("item.boots_m", result.Item!.BaseId);
        Assert.Equal(Rarity.Magic, result.Item.Rarity);
        Assert.Equal(4, result.Item.Level);
        Assert.Equal("bag.2", result.Slot);
        Assert.Single(inv.Slots);
        Assert.Equal(result.Item, inv.Get("bag.2"));
    }

    [Fact]
    public void Wrong_count_or_mixed_types_change_nothing()
    {
        var inv = Bag(("item.boots_c", 5), ("item.boots_c", 5), ("item.ring_c", 5), ("item.boots_c", 5));
        var before = inv.Slots.ToList();
        Assert.Equal(MergeError.WrongCount, Merger.Merge(inv, new[] { "bag.0", "bag.1" }, new Rng(2)).Error);
        Assert.Equal(MergeError.WrongCount, Merger.Merge(inv, new[] { "bag.0", "bag.1", "bag.1" }, new Rng(2)).Error);
        Assert.Equal(MergeError.MixedKinds, Merger.Merge(inv, new[] { "bag.0", "bag.1", "bag.2" }, new Rng(2)).Error);
        Assert.Equal(before, inv.Slots.ToList());
    }

    [Fact]
    public void Equipped_empty_or_non_equipment_slots_are_refused()
    {
        var inv = Bag(("item.boots_c", 5), ("item.boots_c", 5), ("item.heart", 1));
        inv.AddToBag(new ItemInstance { BaseId = "item.boots_c", Level = 5 });
        Assert.True(inv.Move("bag.3", "equip.boots", _db));
        var before = inv.Slots.ToList();
        Assert.Equal(MergeError.NotMergeable, Merger.Merge(inv, new[] { "bag.0", "bag.1", "equip.boots" }, new Rng(3)).Error);
        Assert.Equal(MergeError.NotMergeable, Merger.Merge(inv, new[] { "bag.0", "bag.1", "bag.2" }, new Rng(3)).Error);
        Assert.Equal(MergeError.NotMergeable, Merger.Merge(inv, new[] { "bag.0", "bag.1", "bag.9" }, new Rng(3)).Error);
        Assert.Equal(before, inv.Slots.ToList());
        Assert.Null(Merger.KindOf(inv.Get("bag.2")));
    }

    [Fact]
    public void Legendaries_of_any_types_give_a_legendary_that_is_none_of_the_input_bases()
    {
        for (ulong seed = 1; seed <= 50; seed++)
        {
            var inv = Bag(("item.leg_a", 20), ("item.leg_b", 20), ("item.leg_c", 20));
            var result = Merger.Merge(inv, new[] { "bag.0", "bag.1", "bag.2" }, new Rng(seed));
            Assert.True(result.Ok);
            Assert.Equal("item.leg_d", result.Item!.BaseId);
        }
    }

    [Fact]
    public void Below_every_minimum_level_the_lowest_minimum_base_is_used_at_the_input_level()
    {
        var inv = Bag(("item.boots_m", 10), ("item.boots_m", 12), ("item.boots_m", 30), ("item.boots_m", 11), ("item.boots_m", 15));
        var result = Merger.Merge(inv, inv.Slots.Keys.ToList(), new Rng(4));
        Assert.Equal("item.boots_r_low", result.Item!.BaseId);
        Assert.Equal(10, result.Item.Level);
    }

    [Fact]
    public void Auto_pick_takes_the_highest_levels_of_the_kind_from_the_bag_only()
    {
        var inv = Bag(("item.ring_c", 3), ("item.ring_c", 8), ("item.boots_c", 50), ("item.ring_c", 8), ("item.ring_c", 6), ("item.ring_m", 9));
        var kind = Merger.KindOf(inv.Get("bag.0"))!.Value;
        Assert.Equal(new[] { "bag.1", "bag.3", "bag.4" }, Merger.AutoPick(inv, kind));
        Assert.Equal(4, Merger.Matching(inv, kind).Count);
        var legendary = Merger.KindOf(new ItemInstance { BaseId = "item.leg_a", Rarity = Rarity.Legendary })!.Value;
        Assert.True(legendary.AnyType);
        Assert.True(Merger.Matches(new ItemInstance { BaseId = "item.leg_b", Rarity = Rarity.Legendary }, legendary));
    }
}
