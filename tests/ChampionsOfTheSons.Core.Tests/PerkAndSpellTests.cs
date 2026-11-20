using ChampionsOfTheSons.Core.Content;
using ChampionsOfTheSons.Core.Rules;

namespace ChampionsOfTheSons.Core.Tests;

public class PerkAndSpellTests
{
    private const string Perks = """
    { "perks": [
      { "id": "perk.root", "name": "Root", "category": "melee_offense", "cost": 1, "levelReq": 1,
        "effects": [ { "stat": "max_health", "op": "add", "value": 10 } ] },
      { "id": "perk.branch", "name": "Branch", "category": "melee_offense", "cost": 2, "levelReq": 5, "unlockPath": ["perk.root"] },
      { "id": "perk.stack", "name": "Stack", "category": "defense", "cost": 1, "levelReq": 1, "stackable": true,
        "effects": [ { "stat": "max_health", "op": "add", "value": 5 } ] },
      { "id": "perk.both", "name": "Both", "category": "utility", "cost": 1, "levelReq": 1, "requires": ["perk.root", "perk.stack"] }
    ] }
    """;

    private const string Spells = """
    { "spells": [
      { "id": "spell.blink", "name": "Blink", "levelReq": 3, "energyCost": 25, "cooldown": 14 },
      { "id": "spell.frenzy", "name": "Frenzy", "levelReq": 1, "energyCost": 0, "cooldown": 40 }
    ] }
    """;

    private const string SpellStats = """
    { "playerStats": [
      { "id": "max_health", "kind": "add", "default": 0 },
      { "id": "crit_chance", "kind": "add", "default": 0.05 },
      { "id": "all_damage_taken", "kind": "mult", "default": 1 },
      { "id": "i_infinity_loop", "kind": "bool", "default": 0 },
      { "id": "cooldown_rate", "kind": "mult", "default": 1 },
      { "id": "cooldown", "kind": "mult", "default": 1 },
      { "id": "spell_cost", "kind": "mult", "default": 1 },
      { "id": "spell_cost_energy_cost", "kind": "mult", "default": 1 }
    ] }
    """;

    private readonly ContentDb _db = TestContent.Create(new() { ["perks.json"] = Perks, ["spells.json"] = Spells, ["player_stats.json"] = SpellStats });

    [Fact]
    public void Perks_need_points_level_and_path()
    {
        var tree = new PerkTree(_db);
        var p = new Progress(1, 0, 0); // 1 point
        Assert.Equal(BuyResult.PathLocked, tree.CanBuy("perk.branch", new Progress(10, 0, 0)));
        Assert.Equal(BuyResult.Ok, tree.Buy("perk.root", p));
        Assert.Equal(BuyResult.AlreadyBought, tree.Buy("perk.root", p));
        Assert.Equal(BuyResult.LevelTooLow, tree.CanBuy("perk.branch", p));
        Assert.Equal(BuyResult.NotEnoughPoints, tree.CanBuy("perk.stack", p));
        Assert.Equal(BuyResult.Ok, tree.CanBuy("perk.branch", new Progress(5, 0, 0)));
    }

    [Fact]
    public void Requirements_need_every_listed_perk()
    {
        var tree = new PerkTree(_db);
        var p = new Progress(10, 0, 0);
        tree.Buy("perk.root", p);
        Assert.Equal(BuyResult.RequirementsMissing, tree.CanBuy("perk.both", p));
        tree.Buy("perk.stack", p);
        Assert.Equal(BuyResult.Ok, tree.CanBuy("perk.both", p));
    }

    [Fact]
    public void A_restored_perk_stays_bought_even_when_its_path_is_not_owned()
    {
        // COS-70: saves made before a path fix may hold a perk bought through the old (wrong) parent.
        var tree = new PerkTree(_db);
        tree.Restore(new Dictionary<string, int> { ["perk.branch"] = 1 }, Array.Empty<string>());
        Assert.Equal(1, tree.TimesBought("perk.branch"));
        Assert.Equal(8, tree.AvailablePoints(new Progress(10, 0, 0)));
    }

    [Fact]
    public void Content_reports_perks_no_purchase_order_can_reach()
    {
        const string cyclic = """
        { "perks": [
          { "id": "perk.root", "name": "Root", "category": "defense" },
          { "id": "perk.a", "name": "A", "category": "defense", "unlockPath": ["perk.b"] },
          { "id": "perk.b", "name": "B", "category": "defense", "unlockPath": ["perk.a"] },
          { "id": "perk.c", "name": "C", "category": "defense", "unlockPath": ["perk.root"], "requires": ["perk.a"] }
        ] }
        """;
        var db = ContentDb.Load(new Dictionary<string, string> { ["player_stats.json"] = TestContent.PlayerStats, ["perks.json"] = cyclic });
        Assert.Equal(new[] { "perk perk.a: not reachable from a root perk", "perk perk.b: not reachable from a root perk", "perk perk.c: not reachable from a root perk" }, db.Errors);
    }

    [Fact]
    public void Stackable_perks_apply_once_per_purchase()
    {
        var tree = new PerkTree(_db);
        var p = new Progress(10, 0, 0);
        for (int i = 0; i < 3; i++) Assert.Equal(BuyResult.Ok, tree.Buy("perk.stack", p));
        var sheet = new StatSheet(_db);
        sheet.SetSource("perks", tree.Modifiers());
        Assert.Equal(15, sheet.Get("max_health"));
        Assert.Equal(7, tree.AvailablePoints(p));
    }

    [Fact]
    public void Spells_cost_two_points_and_respec_refunds_everything()
    {
        var tree = new PerkTree(_db);
        var p = new Progress(3, 0, 0);
        Assert.Equal(BuyResult.Ok, tree.UnlockSpell("spell.blink", p));
        Assert.Equal(1, tree.AvailablePoints(p));
        tree.Buy("perk.root", p);
        tree.Respec();
        Assert.Equal(3, tree.AvailablePoints(p));
        Assert.Empty(tree.UnlockedSpells);
    }

    [Fact]
    public void Equipping_starts_the_cooldown_and_casting_restarts_it()
    {
        var book = new SpellBook(_db);
        var stats = new StatSheet(_db);
        book.Equip(0, "spell.blink");
        Assert.Equal(CastResult.OnCooldown, book.TryCast(0, 100, stats, out _));
        book.Tick(14, stats);
        Assert.Equal(CastResult.Ok, book.TryCast(0, 100, stats, out var cost));
        Assert.Equal(25, cost.Energy, 6);
        Assert.Equal(CastResult.OnCooldown, book.TryCast(0, 100, stats, out _));
    }

    [Fact]
    public void Cooldown_rate_speeds_up_cooldowns_and_cost_can_shift_to_stamina()
    {
        var book = new SpellBook(_db);
        var stats = new StatSheet(_db);
        stats.SetSource("gear", new[] { new StatModifier("cooldown_rate", EffectOp.Mult, 2), new StatModifier("spell_cost_energy_cost", EffectOp.Mult, 0.25) });
        book.Equip(1, "spell.blink");
        book.Tick(7, stats);
        Assert.True(book.IsReady(1));
        var cost = SpellBook.Cost(_db.Spells["spell.blink"], stats);
        Assert.Equal(6.25, cost.Energy, 6);
        Assert.Equal(18.75, cost.Stamina, 6);
    }

    [Fact]
    public void Not_enough_energy_fails_without_starting_the_cooldown()
    {
        var book = new SpellBook(_db);
        var stats = new StatSheet(_db);
        book.Restore(new[] { "spell.blink" });
        Assert.Equal(CastResult.NotEnoughEnergy, book.TryCast(0, 10, stats, out _));
        Assert.True(book.IsReady(0));
        Assert.Equal(CastResult.EmptySlot, book.TryCast(5, 100, stats, out _));
    }
}
