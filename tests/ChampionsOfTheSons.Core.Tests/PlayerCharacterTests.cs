using ChampionsOfTheSons.Core.Rules;
using ChampionsOfTheSons.Core.Saving;

namespace ChampionsOfTheSons.Core.Tests;

public class PlayerCharacterTests
{
    private const string Stats = """
    { "playerStats": [
      { "id": "max_health", "kind": "add", "default": 0, "integer": true },
      { "id": "crit_chance", "kind": "add", "default": 0.05 },
      { "id": "all_damage_taken", "kind": "mult", "default": 1 },
      { "id": "i_infinity_loop", "kind": "bool", "default": 0 },
      { "id": "strength", "kind": "add", "default": 1 },
      { "id": "agility", "kind": "add", "default": 1 },
      { "id": "vitality", "kind": "add", "default": 1 },
      { "id": "intelligence", "kind": "add", "default": 1 },
      { "id": "exp_gain", "kind": "add", "default": 1 },
      { "id": "max_massacre_time", "kind": "add", "default": 15 },
      { "id": "time_bonus_per_kill", "kind": "add", "default": 7.5 },
      { "id": "cooldown_rate", "kind": "mult", "default": 1 },
      { "id": "cooldown", "kind": "mult", "default": 1 }
    ] }
    """;

    private static PlayerCharacter NewCharacter() => new(TestContent.Create(new() { ["player_stats.json"] = Stats }));

    [Fact]
    public void Equipping_changes_stats_and_unequipping_restores_them()
    {
        var c = NewCharacter();
        c.Inventory.AddToBag(new ItemInstance { BaseId = "item.ring", Stats = new[] { new RolledStat("stat.max_life", 25) } });
        Assert.True(c.Inventory.Equip("bag.0", c.Content));
        Assert.Equal(25, c.Stats.Get("max_health"));
        Assert.True(c.Stats.IsOn("i_infinity_loop"));
        Assert.True(c.Inventory.Move("equip.ring1", "bag.5", c.Content));
        Assert.Equal(0, c.Stats.Get("max_health"));
        Assert.False(c.Stats.IsOn("i_infinity_loop"));
    }

    [Fact]
    public void Two_rings_fill_both_ring_slots_and_boots_cannot_go_in_a_ring_slot()
    {
        var c = NewCharacter();
        c.Inventory.AddToBag(new ItemInstance { BaseId = "item.ring" });
        c.Inventory.AddToBag(new ItemInstance { BaseId = "item.ring" });
        c.Inventory.AddToBag(new ItemInstance { BaseId = "item.boots" });
        Assert.True(c.Inventory.Equip("bag.0", c.Content));
        Assert.True(c.Inventory.Equip("bag.1", c.Content));
        Assert.NotNull(c.Inventory.Get("equip.ring1"));
        Assert.NotNull(c.Inventory.Get("equip.ring2"));
        Assert.False(c.Inventory.Move("bag.2", "equip.ring1", c.Content));
    }

    [Fact]
    public void Level_ups_raise_attributes()
    {
        var c = NewCharacter();
        Assert.Equal(1, c.Stats.Get("strength"));
        c.AddXp(Enumerable.Range(1, 9).Sum(XpCurve.Goal));
        Assert.Equal(10, c.Progress.Level);
        Assert.Equal(XpCurve.AttributeBase(10) - 1, c.Stats.Get("strength"));
    }

    [Fact]
    public void Kills_pay_out_through_the_streak()
    {
        var c = NewCharacter();
        c.OnKill(10);
        c.OnKill(10);
        c.OnKill(10);
        Assert.Equal(0, c.Progress.Xp);
        c.Tick(16);
        // 3 kills × 10 XP × 1.1 streak bonus = 33; the level-1 goal is 30
        Assert.Equal(2, c.Progress.Level);
        Assert.Equal(3, c.Progress.Xp, 6);
    }

    [Fact]
    public void Character_survives_a_save_round_trip()
    {
        var c = NewCharacter();
        c.AddXp(1000);
        c.Inventory.AddToBag(new ItemInstance { BaseId = "item.ring", Level = 7, Stats = new[] { new RolledStat("stat.crit", 0.02f) } });
        c.Inventory.Equip("bag.0", c.Content);
        c.Inventory.AddToBag(new ItemInstance { BaseId = "item.no_longer_exists" });

        var loaded = NewCharacter();
        loaded.Load(SaveCodec.ReadPlayer(SaveCodec.Write(c.ToSave())));

        Assert.Equal(c.Progress.Level, loaded.Progress.Level);
        Assert.Equal(c.Progress.Xp, loaded.Progress.Xp, 6);
        Assert.Equal(c.Inventory.Get("equip.ring1"), loaded.Inventory.Get("equip.ring1"));
        Assert.Equal("item.no_longer_exists", loaded.Inventory.Get("bag.0")!.BaseId);
        Assert.Equal(c.Stats.Get("crit_chance"), loaded.Stats.Get("crit_chance"), 6);
    }
}
