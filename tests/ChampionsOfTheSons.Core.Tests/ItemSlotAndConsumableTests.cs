using System.Text.Json.Nodes;
using ChampionsOfTheSons.Core.Content;
using ChampionsOfTheSons.Core.Rules;
using ChampionsOfTheSons.Core.Saving;

namespace ChampionsOfTheSons.Core.Tests;

/// <summary>Stat slots (COS-71, COS-73) and the bag's Use action (COS-72).</summary>
public class ItemSlotAndConsumableTests
{
    private const string ItemStats = """
    { "itemStats": [
      { "id": "stat.max_life", "legacyId": 5, "name": "Maximum Life", "min": 4, "max": 6, "levelPow": 1.2, "combine": "sum", "rarity": 0,
        "effects": [ { "stat": "max_health", "op": "add", "transform": "round" } ] },
      { "id": "stat.crit", "legacyId": 12, "name": "Crit Chance", "min": 0.01, "max": 0.02, "levelPow": 0.25, "combine": "sum", "rarity": 3, "cap": 0.5,
        "effects": [ { "stat": "crit_chance", "op": "add", "transform": "x" } ] },
      { "id": "stat.damage_reduction", "legacyId": 11, "name": "Damage Reduction", "min": 0.005, "max": 0.0075, "levelPow": 0.5, "combine": "one_minus_product", "rarity": 2,
        "effects": [ { "stat": "all_damage_taken", "op": "mult", "transform": "one_minus" } ] },
      { "id": "stat.permanentperkpoints", "legacyId": 32, "name": "Perk Points", "min": 0.25, "max": 0.25, "levelPow": 0, "combine": "sum", "rarity": 3, "effects": [] },
      { "id": "stat.experience", "legacyId": 33, "name": "Experience", "min": 30, "max": 30, "levelPow": 3, "combine": "sum", "rarity": 3, "effects": [] },
      { "id": "stat.emptysocket", "legacyId": 3000, "name": "Empty Socket", "min": 1, "max": 3.5, "levelPow": 0, "combine": "sum", "rarity": 0, "multiplier": 0, "effects": [] }
    ] }
    """;

    private const string Items = """
    { "items": [
      { "id": "item.maul", "name": "Maul", "type": "weapon", "rarity": 2, "minLevel": 1, "maxLevel": 50,
        "statPools": [ ["stat.max_life"], ["stat.crit"] ],
        "statMultipliers": [ { "slot": 1, "stat": "stat.crit", "multiplier": -2 } ] },
      { "id": "item.mixed", "name": "Mixed Ring", "type": "ring", "rarity": 2, "minLevel": 1, "maxLevel": 50,
        "statPools": [ ["stat.max_life"], ["stat.crit", "*"] ] },
      { "id": "item.greater_mutated_heart", "name": "Big Heart", "type": "other", "rarity": 3, "minLevel": 1, "maxLevel": 1, "stackSize": 100,
        "statPools": [ ["stat.permanentperkpoints"] ] },
      { "id": "item.lesser_mutated_heart", "name": "Xp Heart", "type": "other", "rarity": 3, "minLevel": 1, "maxLevel": 1,
        "statPools": [ ["stat.experience"] ] },
      { "id": "item.heart_of_purity", "name": "Respec Heart", "type": "other", "rarity": 2, "minLevel": 1, "maxLevel": 1, "stackSize": 100, "statPools": [] },
      { "id": "item.pebble", "name": "Pebble", "type": "other", "rarity": 0, "minLevel": 1, "maxLevel": 1, "statPools": [] }
    ] }
    """;

    private const string PlayerStats = """
    { "playerStats": [
      { "id": "max_health", "kind": "add", "default": 0, "integer": true },
      { "id": "crit_chance", "kind": "add", "default": 0.05 },
      { "id": "all_damage_taken", "kind": "mult", "default": 1 },
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

    private const string Perks = """
    { "perks": [
      { "id": "perk.tough", "name": "Tough", "category": "Defense", "cost": 1, "effects": [] }
    ] }
    """;

    private readonly ContentDb _db = TestContent.Create(new()
    {
        ["item_stats.json"] = ItemStats, ["items.json"] = Items, ["player_stats.json"] = PlayerStats, ["perks.json"] = Perks,
    });

    private LootRoller Loot => new(_db);

    [Fact]
    public void A_negative_slot_multiplier_yields_a_negative_roll_only_in_its_slot()
    {
        var rng = new Rng(1);
        for (int i = 0; i < 200; i++)
        {
            var item = Loot.RollItem(_db.Items["item.maul"], 20, rng);
            Assert.True(item.Stats.Single(s => s.Slot == 1).Value < 0);
            Assert.True(item.Stats.Single(s => s.Slot == 0).Value > 0);
        }
        var (lo, hi) = LootRoller.ValueRange(_db.ItemStats["stat.crit"], 20, 2, _db.Items["item.maul"].MultiplierFor(1, "stat.crit"));
        Assert.True(lo < hi && hi < 0);
    }

    [Fact]
    public void A_wildcard_option_weighs_each_common_stat_like_a_named_option()
    {
        // Slot ["stat.crit", "*"] with three common stats rolls from four equal options: crit, max_life, crit, damage_reduction.
        var rng = new Rng(2);
        const int n = 8000;
        var counts = new Dictionary<string, int>();
        for (int i = 0; i < n; i++)
        {
            var stat = Loot.RollItem(_db.Items["item.mixed"], 10, rng).Stats.Single(s => s.Slot == 1).StatId;
            counts[stat] = counts.GetValueOrDefault(stat) + 1;
        }
        Assert.InRange(counts["stat.crit"] / (double)n, 0.47, 0.53);
        Assert.InRange(counts["stat.max_life"] / (double)n, 0.22, 0.28);
        Assert.InRange(counts["stat.damage_reduction"] / (double)n, 0.22, 0.28);
    }

    [Fact]
    public void Old_items_get_their_slots_inferred_first_free_slot_first()
    {
        var old = new ItemInstance
        {
            BaseId = "item.mixed",
            Stats = new[] { new RolledStat("stat.max_life", 5), new RolledStat("stat.max_life", 6), new RolledStat(LootRoller.EmptySocketStat, 0) },
        };
        var inferred = Loot.InferSlots(old);
        Assert.Equal(new[] { 0, 1, -1 }, inferred.Stats.Select(s => s.Slot));
    }

    [Fact]
    public void Version_1_save_migrates_and_loads_with_inferred_slots()
    {
        const string v1 = """
        { "version": 1, "xp": 0, "level": 5, "bonusPerkPoints": 0,
          "slots": { "bag.0": { "baseId": "item.maul", "level": 10, "rarity": 2, "amount": 1, "upgrades": 0,
            "stats": [ { "id": "stat.crit", "value": -0.03 }, { "id": "stat.max_life", "value": 40 } ] } },
          "perks": {}, "unlockedSpells": [], "spellLoadout": [] }
        """;
        var save = SaveCodec.ReadPlayer(v1);
        Assert.All(save.Slots["bag.0"].Stats, s => Assert.Equal(-1, s.Slot));
        Assert.Equal(PlayerSave.CurrentVersion, JsonNode.Parse(SaveCodec.Write(save))!["version"]!.GetValue<int>());

        var pc = new PlayerCharacter(_db);
        pc.Load(save);
        Assert.Equal(new[] { 1, 0 }, pc.Inventory.Get("bag.0")!.Stats.Select(s => s.Slot));
    }

    [Fact]
    public void Slots_round_trip_through_the_save()
    {
        var item = Loot.RollItem(_db.Items["item.maul"], 10, new Rng(8));
        var save = new PlayerSave();
        save.Slots["bag.0"] = SavedItem.From(item);
        Assert.Equal(item, SaveCodec.ReadPlayer(SaveCodec.Write(save)).Slots["bag.0"].ToItem());
    }

    [Fact]
    public void Bonus_point_heart_adds_a_permanent_point_that_survives_respec_and_save()
    {
        var pc = new PlayerCharacter(_db);
        pc.Inventory.AddToBag(Loot.RollItem(_db.Items["item.greater_mutated_heart"], 25, new Rng(9)) with { Amount = 2 }, 100);
        int before = pc.Perks.AvailablePoints(pc.Progress);
        Assert.True(Consumables.Use(pc, "bag.0"));
        Assert.Equal(1, pc.Progress.BonusPerkPoints); // round(0.25 × 5.6)
        Assert.Equal(before + 1, pc.Perks.AvailablePoints(pc.Progress));
        Assert.Equal(1, pc.Inventory.Get("bag.0")!.Amount);

        Assert.Equal(BuyResult.Ok, pc.BuyPerk("perk.tough"));
        pc.Respec();
        Assert.Equal(1, pc.Progress.BonusPerkPoints);
        var loaded = new PlayerCharacter(_db);
        loaded.Load(SaveCodec.ReadPlayer(SaveCodec.Write(pc.ToSave())));
        Assert.Equal(pc.Progress.TotalPerkPoints, loaded.Progress.TotalPerkPoints);
    }

    [Fact]
    public void Xp_heart_grants_its_rolled_value_through_the_xp_path()
    {
        var pc = new PlayerCharacter(_db);
        pc.Load(new PlayerSave { Level = 10 });
        var heart = Loot.RollItem(_db.Items["item.lesser_mutated_heart"], 2, new Rng(10));
        pc.Inventory.AddToBag(heart);
        Assert.True(Consumables.Use(pc, "bag.0"));
        Assert.Equal(30 * 8 * LootRoller.RarityMultiplier[(int)Rarity.Legendary], pc.Progress.Xp, 2); // 30 × level³ × rarity multiplier, × XP gain 1
        Assert.Null(pc.Inventory.Get("bag.0"));
    }

    [Fact]
    public void Respec_heart_respecs_and_unusable_items_do_nothing()
    {
        var pc = new PlayerCharacter(_db);
        pc.Inventory.AddToBag(new ItemInstance { BaseId = "item.heart_of_purity" });
        pc.Inventory.AddToBag(new ItemInstance { BaseId = "item.pebble" });
        Assert.False(Consumables.Use(pc, "bag.0")); // nothing to respec yet: the heart is kept
        Assert.Equal(BuyResult.Ok, pc.BuyPerk("perk.tough"));
        Assert.True(Consumables.Use(pc, "bag.0"));
        Assert.Equal(0, pc.Perks.SpentPoints);
        Assert.False(Consumables.CanUse(pc.Inventory.Get("bag.1")!));
        Assert.False(Consumables.Use(pc, "bag.1"));
    }
}
