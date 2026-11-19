using ChampionsOfTheSons.Core.Content;
using ChampionsOfTheSons.Core.Rules;

namespace ChampionsOfTheSons.Core.Tests;

public class LootTests
{
    private readonly ContentDb _db = TestContent.Create();
    private LootRoller Loot => new(_db);

    [Fact]
    public void Rolled_values_stay_inside_the_scaled_range_and_under_the_cap()
    {
        var crit = _db.ItemStats["stat.crit"];
        var life = _db.ItemStats["stat.max_life"];
        var rng = new Rng(10);
        for (int i = 0; i < 2000; i++)
        {
            int level = rng.RangeInclusive(1, 300);
            int rarity = rng.RangeInclusive(0, 7);
            double v = LootRoller.RollValue(life, level, rarity, rng);
            double scale = Math.Pow(level, life.LevelPow) * LootRoller.RarityMultiplier[rarity];
            Assert.InRange(v, life.Min * scale * (1 - 1e-6), life.Max * scale * (1 + 1e-6));
            Assert.True(LootRoller.RollValue(crit, level, rarity, rng) <= crit.Cap!.Value + 1e-9);
        }
    }

    [Fact]
    public void Higher_rarity_rolls_stronger_on_average()
    {
        var life = _db.ItemStats["stat.max_life"];
        var rng = new Rng(11);
        double Avg(int r) => Enumerable.Range(0, 500).Average(_ => LootRoller.RollValue(life, 20, r, rng));
        for (int r = 0; r < 7; r++) Assert.True(Avg(r + 1) > Avg(r));
    }

    [Fact]
    public void Rarity_climbs_with_worth_magic_find_and_difficulty()
    {
        var rng = new Rng(12);
        double Avg(double worth, double mf, int d) => Enumerable.Range(0, 3000).Average(_ => LootRoller.RollRarity(worth, 10, mf, d, rng));
        Assert.Equal(0, LootRoller.RollRarity(worth: 50, itemLevel: 10, magicFind: 1, difficulty: 0, rng)); // w = 5: nothing passes
        Assert.True(Avg(100_000, 1, 3) > Avg(1_000, 1, 3) + 0.5);
        Assert.True(Avg(10_000, 2, 0) > Avg(10_000, 1, 0));
        Assert.True(Avg(10_000, 1, 8) > Avg(10_000, 1, 0));
        Assert.All(Enumerable.Range(0, 3000), _ => Assert.InRange(LootRoller.RollRarity(1e9, 1, 10, 10, rng), 0, 7));
    }

    [Fact]
    public void Item_rolls_one_stat_per_pool_and_never_more()
    {
        var ring = _db.Items["item.ring"];
        var rng = new Rng(13);
        for (int i = 0; i < 500; i++)
        {
            var item = Loot.RollItem(ring, 10, rng);
            Assert.Equal("item.ring", item.BaseId);
            Assert.Contains(item.Stats, s => s.StatId == "stat.max_life"); // first pool is mandatory
            Assert.InRange(item.Stats.Count, 2, 3); // second pool may be empty, third is "*"
        }
    }

    [Fact]
    public void Base_pick_respects_min_level_and_falls_back_a_rarity()
    {
        var rng = new Rng(14);
        Assert.Equal("item.boots", Loot.PickBase(1, 1, rng)!.Id); // no rarity-1 items: falls back to rarity 0
        Assert.Equal("item.ring", Loot.PickBase(2, 1, rng)!.Id);
    }

    [Fact]
    public void Elites_always_drop_and_bosses_drop_more()
    {
        var rng = new Rng(15);
        var rules = new WorldRules();
        var normal = new EnemyProfile { Level = 10 };
        var boss = new EnemyProfile { Level = 10, Tier = EnemyTier.Boss, Abilities = new[] { EnemyAbility.BossSteadfast, EnemyAbility.Meteor, EnemyAbility.Laser } };
        Assert.True(LootRoller.ShouldDrop(boss, rules, 1, rng));
        double normalAvg = Enumerable.Range(0, 200).Average(_ => LootRoller.DropCount(normal, rules, rng));
        double bossAvg = Enumerable.Range(0, 200).Average(_ => LootRoller.DropCount(boss, rules, rng));
        Assert.True(bossAvg > normalAvg + 7);
    }

    [Fact]
    public void Equipped_item_becomes_stat_modifiers()
    {
        var item = new ItemInstance { BaseId = "item.ring", Stats = new[] { new RolledStat("stat.max_life", 10.4f), new RolledStat("stat.damage_reduction", 0.1f) } };
        var mods = ItemEffects.For(item, _db);
        Assert.Contains(new StatModifier("max_health", EffectOp.Add, 10), mods); // round
        Assert.Contains(mods, m => m.Stat == "all_damage_taken" && m.Op == EffectOp.Mult && Math.Abs(m.Value - 0.9) < 1e-6); // one_minus
        Assert.Contains(mods, m => m.Stat == "i_infinity_loop" && m.Op == EffectOp.Enable); // unique effect
    }
}

public class CraftingTests
{
    private readonly ContentDb _db = TestContent.Create();

    private Crafting Crafting => new(_db, new LootRoller(_db));

    private ItemInstance Ring(Rarity r = Rarity.Magic) => new LootRoller(_db).RollItem(_db.Items["item.ring"], 30, new Rng(20)) with { Rarity = r };

    [Fact]
    public void Wrong_ingredient_count_or_low_rarity_is_refused()
    {
        var target = Ring();
        Assert.Equal(CraftError.WrongIngredientCount, Crafting.Craft(CraftMode.Reroll, target, new[] { Ring() }, new Rng(1)).Error);
        Assert.Equal(CraftError.IngredientRarityTooLow, Crafting.Craft(CraftMode.Polish, target, new[] { Ring(Rarity.Common) }, new Rng(1)).Error);
    }

    [Fact]
    public void Polish_rerolls_only_the_chosen_stat()
    {
        var target = Ring();
        var result = Crafting.Craft(CraftMode.Polish, target, new[] { Ring() }, new Rng(2), statIndex: 0);
        Assert.True(result.Ok);
        Assert.Equal(target.Stats[0].StatId, result.Item!.Stats[0].StatId);
        Assert.Equal(target.Stats.Skip(1), result.Item.Stats.Skip(1));
    }

    [Fact]
    public void Empower_sets_level_to_the_player_level()
    {
        var result = Crafting.Craft(CraftMode.Empower, Ring(), Enumerable.Repeat(Ring(), 9).ToList(), new Rng(3), playerLevel: 77);
        Assert.True(result.Ok);
        Assert.Equal(77, result.Item!.Level);
    }

    [Fact]
    public void Reforge_needs_another_base_of_the_same_type_and_rarity()
    {
        var result = Crafting.Craft(CraftMode.Reforge, Ring(), new[] { Ring(), Ring(), Ring() }, new Rng(4));
        Assert.Equal(CraftError.NoAlternativeBase, result.Error); // test content has only one ring
    }

    [Fact]
    public void Individual_reroll_needs_a_real_alternative_in_the_pool()
    {
        var target = Ring();
        int lifeIndex = target.Stats.ToList().FindIndex(s => s.StatId == "stat.max_life");
        var result = Crafting.Craft(CraftMode.IndividualReroll, target, new[] { Ring() }, new Rng(5), statIndex: lifeIndex);
        Assert.Equal(CraftError.NothingToReroll, result.Error); // pool ["stat.max_life"] has no alternative
    }

    private const string ScrapItems = """
    { "items": [
      { "id": "item.ring", "name": "Test Ring", "type": "ring", "rarity": 2, "minLevel": 1, "maxLevel": 50, "statPools": [ ["stat.max_life"] ] },
      { "id": "item.ore3", "name": "Ore", "type": "material", "rarity": 3, "minLevel": 20, "maxLevel": 300, "statPools": [], "stackSize": 100 },
      { "id": "item.ore5", "name": "Ore5", "type": "material", "rarity": 5, "minLevel": 20, "maxLevel": 300, "statPools": [], "stackSize": 100 },
      { "id": "item.heart", "name": "Heart", "type": "other", "rarity": 5, "minLevel": 1, "maxLevel": 2, "statPools": [], "stackSize": 100 }
    ] }
    """;

    [Fact]
    public void Scrap_gives_a_material_stack_scaling_with_rarity_and_level()
    {
        var db = TestContent.Create(new() { ["items.json"] = ScrapItems });
        var crafting = new Crafting(db, new LootRoller(db));
        var ring = new ItemInstance { BaseId = "item.ring", Level = 90, Rarity = Rarity.Legendary };
        var result = crafting.Scrap(ring, new Rng(1));
        Assert.True(result.Ok);
        Assert.Equal("item.ore5", result.Material!.BaseId); // never the "other" heart, rarity 5 = Legendary
        Assert.Equal(Crafting.ScrapYield(Rarity.Legendary, 90), result.Material.Amount);
        Assert.Equal(6, result.Material.Amount); // (5-1) + 90/40
        Assert.Equal(1, Crafting.ScrapYield(Rarity.Common, 1));
    }

    [Fact]
    public void Scrap_of_low_rarity_gives_the_lowest_material_rarity_and_refuses_materials()
    {
        var db = TestContent.Create(new() { ["items.json"] = ScrapItems });
        var crafting = new Crafting(db, new LootRoller(db));
        var common = crafting.Scrap(new ItemInstance { BaseId = "item.ring", Level = 5, Rarity = Rarity.Common }, new Rng(2));
        Assert.Equal("item.ore3", common.Material!.BaseId);
        var scrapMaterial = crafting.Scrap(new ItemInstance { BaseId = "item.ore3", Level = 1, Rarity = Rarity.Rare }, new Rng(3));
        Assert.Equal(CraftError.NotEquippable, scrapMaterial.Error);
    }
}
