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
            int rarity = rng.RangeInclusive(0, Rarities.Max);
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
        for (int r = 0; r < Rarities.Max; r++) Assert.True(Avg(r + 1) > Avg(r));
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
        Assert.All(Enumerable.Range(0, 3000), _ => Assert.InRange(LootRoller.RollRarity(1e9, 1, 10, 10, rng), 0, Rarities.Max));
    }

    [Fact]
    public void Rarity_is_the_eight_step_climb_folded_into_four_tiers()
    {
        // Same seed, same draws: the four-tier roll is the eight-step roll mapped (COS-109), so drop pacing is unchanged.
        for (ulong seed = 0; seed < 200; seed++)
        {
            int step = LootRoller.RollStep(5_000, 10, 2, 6, new Rng(seed));
            Assert.Equal((int)Rarities.FromLegacy(step), LootRoller.RollRarity(5_000, 10, 2, 6, new Rng(seed)));
        }
        Assert.Equal(new[] { 0, 0, 1, 2, 2, 3, 3, 3 }, Enumerable.Range(0, 8).Select(r => (int)Rarities.FromLegacy(r)));
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

public class EquipmentOnlyDropTests
{
    private const string MixedItems = """
    { "items": [
      { "id": "item.ring", "name": "Test Ring", "type": "ring", "rarity": 2, "minLevel": 1, "maxLevel": 50, "statPools": [ ["stat.max_life"] ] },
      { "id": "item.ore3", "name": "Ore", "type": "material", "rarity": 2, "minLevel": 20, "maxLevel": 300, "statPools": [], "stackSize": 100 },
      { "id": "item.ore5", "name": "Ore5", "type": "material", "rarity": 3, "minLevel": 20, "maxLevel": 300, "statPools": [], "stackSize": 100 },
      { "id": "item.heart", "name": "Heart", "type": "other", "rarity": 3, "minLevel": 1, "maxLevel": 2, "statPools": [], "stackSize": 100 }
    ] }
    """;

    [Fact]
    public void Drops_are_equipment_only_even_where_a_rarity_has_only_materials()
    {
        var loot = new LootRoller(TestContent.Create(new() { ["items.json"] = MixedItems }));
        var rng = new Rng(21);
        for (int i = 0; i < 500; i++)
        {
            var drop = loot.RollDrop(worth: 1e6, itemLevel: 60, magicFind: 3, difficulty: 10, rng);
            Assert.NotNull(drop); // Legendary holds only a material and the heart: the pick falls back to the ring
            Assert.Equal("item.ring", drop!.BaseId);
        }
    }
}
