using ChampionsOfTheSons.Core.Content;
using ChampionsOfTheSons.Core.Rules;

namespace ChampionsOfTheSons.Core.Tests;

public class PerkEffectTests
{
    private const string Perks = """
    { "perks": [
      { "id": "perk.deployable_shield", "name": "S", "category": "defense", "cost": 0, "levelReq": 1, "stackable": false, "effects": [] },
      { "id": "perk.deployable_shield_2", "name": "S2", "category": "defense", "cost": 0, "levelReq": 1, "stackable": false, "effects": [] },
      { "id": "perk.adrenaline_rush", "name": "A", "category": "ranged_offense", "cost": 0, "levelReq": 1, "stackable": false, "effects": [] },
      { "id": "perk.adrenaline_rush_ii", "name": "A2", "category": "ranged_offense", "cost": 0, "levelReq": 1, "stackable": false, "effects": [] },
      { "id": "perk.quick_silver", "name": "Q", "category": "magic_offense", "cost": 0, "levelReq": 1, "stackable": false, "effects": [] },
      { "id": "perk.more_meds", "name": "M", "category": "utility", "cost": 0, "levelReq": 1, "stackable": true, "effects": [] },
      { "id": "perk.meds", "name": "D", "category": "utility", "cost": 0, "levelReq": 1, "stackable": true, "effects": [] },
      { "id": "perk.storm_season", "name": "SS", "category": "magic_offense", "cost": 0, "levelReq": 1, "stackable": false, "effects": [] },
      { "id": "perk.endless_storm", "name": "ES", "category": "magic_offense", "cost": 0, "levelReq": 1, "stackable": false, "effects": [] },
      { "id": "perk.time_of_need", "name": "TN", "category": "magic_offense", "cost": 0, "levelReq": 1, "stackable": false, "effects": [] }
    ] }
    """;

    private const string Spells = """
    { "spells": [ { "id": "spell.blink", "name": "Blink", "levelReq": 1, "energyCost": 25, "cooldown": 14 },
                  { "id": "spell.other", "name": "Other", "levelReq": 1, "energyCost": 25, "cooldown": 14 },
                  { "id": "spell.ball_lightning", "name": "Ball", "levelReq": 1, "energyCost": 25, "cooldown": 100 },
                  { "id": "spell.healing_dome", "name": "Dome", "levelReq": 1, "energyCost": 25, "cooldown": 60 } ] }
    """;

    private static readonly string PlayerStats = TestContent.PlayerStats.Replace("{ \"playerStats\": [",
        "{ \"playerStats\": [ { \"id\": \"cooldown\", \"kind\": \"mult\", \"default\": 1 }, { \"id\": \"cooldown_rate\", \"kind\": \"mult\", \"default\": 1 }, " +
        "{ \"id\": \"i_death_pact_enabled\", \"kind\": \"bool\", \"default\": 0 },");

    private static ContentDb Db(bool perks) => TestContent.Create(new()
    {
        ["player_stats.json"] = PlayerStats,
        ["spells.json"] = Spells,
        ["perks.json"] = perks ? Perks : "{ \"perks\": [] }",
    });

    private static PerkTree Tree(params string[] bought)
    {
        var tree = new PerkTree(Db(true));
        tree.Restore(bought.GroupBy(b => b).ToDictionary(g => g.Key, g => g.Count()), Array.Empty<string>());
        return tree;
    }

    [Fact]
    public void Shield_amount_follows_the_perks_and_soaks_then_fades()
    {
        Assert.Equal(0, PerkTuning.ShieldFor(Tree(), 200));
        Assert.Equal(30, PerkTuning.ShieldFor(Tree("perk.deployable_shield"), 200), 6);
        Assert.Equal(50, PerkTuning.ShieldFor(Tree("perk.deployable_shield", "perk.deployable_shield_2"), 200), 6);

        var shield = new DodgeShield();
        shield.Grant(30);
        Assert.Equal(0, shield.Absorb(20));
        Assert.Equal(15, shield.Absorb(25)); // 10 left in the shield soaks 10 of 25
        Assert.Equal(0, shield.Amount);
        shield.Grant(30);
        shield.Tick(PerkTuning.ShieldSeconds + 1);
        Assert.Equal(0, shield.Amount);
    }

    [Fact]
    public void Adrenaline_rush_triggers_once_per_cooldown_at_low_health_and_scales_with_the_second_perk()
    {
        var rush = new AdrenalineRush();
        var tree = Tree("perk.adrenaline_rush");
        Assert.False(rush.TryTrigger(tree, 90, 100));
        Assert.True(rush.TryTrigger(tree, 30, 100));
        Assert.Equal(1.25, rush.Modifiers(tree).First(m => m.Stat == StatIds.MovementSpeed).Value, 6);
        Assert.False(rush.TryTrigger(tree, 10, 100)); // cooldown
        rush.Tick(PerkTuning.RushSeconds + 1);
        Assert.False(rush.IsActive);
        Assert.Empty(rush.Modifiers(tree));
        rush.Tick(PerkTuning.RushCooldown);
        var tree2 = Tree("perk.adrenaline_rush", "perk.adrenaline_rush_ii");
        Assert.True(rush.TryTrigger(tree2, 10, 100));
        Assert.Equal(1.5, rush.Modifiers(tree2).First(m => m.Stat == StatIds.AttackSpeed).Value, 6);
        Assert.False(new AdrenalineRush().TryTrigger(Tree(), 10, 100)); // perk not bought
    }

    [Fact]
    public void Quick_silver_takes_six_seconds_off_blink_only()
    {
        var db = Db(true);
        foreach (var bought in new[] { false, true })
        {
            var tree = Tree(bought ? new[] { "perk.quick_silver" } : Array.Empty<string>());
            var book = new SpellBook(db) { CooldownReduction = id => PerkTuning.CooldownReduction(tree, id) };
            var stats = new StatSheet(db);
            book.Equip(0, "spell.blink");
            book.Equip(1, "spell.other");
            book.TryCast(0, 1000, stats, out _);
            book.TryCast(1, 1000, stats, out _);
            book.Tick(8, stats);
            Assert.Equal(bought, book.IsReady(0));
            Assert.False(book.IsReady(1));
        }
    }

    [Fact]
    public void Spell_cooldown_perks_stack_flat_cuts_then_scale_only_their_spells()
    {
        var db = Db(true);
        var stats = new StatSheet(db);
        var tree = Tree("perk.storm_season", "perk.endless_storm", "perk.time_of_need");
        var book = new SpellBook(db)
        {
            CooldownReduction = id => PerkTuning.CooldownReduction(tree, id),
            CooldownScale = id => PerkTuning.CooldownScale(tree, id),
        };
        Assert.Equal(100 - 40 - 25, book.CooldownOf("spell.ball_lightning", stats), 6);
        Assert.Equal(60 * 0.5, book.CooldownOf("spell.healing_dome", stats), 6);
        Assert.Equal(14, book.CooldownOf("spell.other", stats), 6);
        Assert.Equal(100, new SpellBook(db).CooldownOf("spell.ball_lightning", stats), 6);
    }

    [Fact]
    public void Infinity_loop_trims_a_cooling_spell_and_does_nothing_when_all_are_ready()
    {
        var db = Db(false);
        var book = new SpellBook(db);
        var stats = new StatSheet(db);
        book.Equip(0, "spell.blink");
        book.TryCast(0, 1000, stats, out _);
        Assert.True(book.ReduceRandomCooldown(7, stats, new Rng(1)));
        Assert.Equal(0.5, book.CooldownProgress(0), 6);
        Assert.False(new SpellBook(db).ReduceRandomCooldown(1, stats, new Rng(1)));
    }

    [Fact]
    public void Utility_totals_sum_stacks_per_keyword()
    {
        var tree = Tree("perk.more_meds", "perk.more_meds", "perk.meds");
        Assert.Equal(40, UtilityPerks.Totals(tree, UtilityKind.Capacity)["Pills"]);
        Assert.Equal(3, UtilityPerks.Totals(tree, UtilityKind.DailyGrant)["Pills"]);
        Assert.Empty(UtilityPerks.Totals(Tree(), UtilityKind.Capacity));
    }

    [Fact]
    public void Death_pact_scales_damage_by_missing_health_and_never_takes_the_last_point()
    {
        var sheet = new StatSheet(Db(false));
        Assert.Equal(1, UniqueRuntime.DeathPactMultiplier(sheet, 50, 100)); // flag off
        sheet.SetSource("t", new[] { new StatModifier(UniqueFlagIds.DeathPact, EffectOp.Enable, 1) });
        Assert.Equal(3.5, UniqueRuntime.DeathPactMultiplier(sheet, 50, 100), 6);
        Assert.Equal(7, UniqueRuntime.DeathPactCost(sheet, 100, 100), 6);
        Assert.Equal(1, UniqueRuntime.DeathPactCost(sheet, 2, 100), 6);
    }
}
