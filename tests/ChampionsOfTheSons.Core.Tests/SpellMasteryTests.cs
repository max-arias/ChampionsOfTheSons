using ChampionsOfTheSons.Core.Content;
using ChampionsOfTheSons.Core.Rules;
using ChampionsOfTheSons.Core.Saving;

namespace ChampionsOfTheSons.Core.Tests;

/// <summary>COS-176: spells improve by being cast.</summary>
public class SpellMasteryTests
{
    private const string Perks = """
    { "perks": [
      { "id": "perk.root", "name": "Root", "category": "defense", "cost": 1, "levelReq": 1 },
      { "id": "perk.blink_far", "name": "Far", "category": "mastery", "cost": 1, "levelReq": 1,
        "effects": [ { "stat": "max_health", "op": "add", "value": 10 } ] },
      { "id": "perk.blink_dmg", "name": "Dmg", "category": "mastery", "cost": 2, "levelReq": 1, "stackable": true,
        "effects": [ { "stat": "max_health", "op": "add", "value": 5 } ] }
    ] }
    """;

    private const string Spells = """
    { "spells": [
      { "id": "spell.blink", "name": "Blink", "levelReq": 1, "energyCost": 100, "cooldown": 10, "mastery": [ "perk.blink_far", "perk.blink_dmg" ] },
      { "id": "spell.frenzy", "name": "Frenzy", "levelReq": 1, "energyCost": 50, "cooldown": 40 }
    ] }
    """;

    private const string Stats = """
    { "playerStats": [
      { "id": "max_health", "kind": "add", "default": 0 },
      { "id": "crit_chance", "kind": "add", "default": 0.05 },
      { "id": "all_damage_taken", "kind": "mult", "default": 1 },
      { "id": "i_infinity_loop", "kind": "bool", "default": 0 },
      { "id": "cooldown", "kind": "mult", "default": 1 },
      { "id": "cooldown_rate", "kind": "mult", "default": 1 },
      { "id": "spell_cost", "kind": "mult", "default": 1 },
      { "id": "spell_cost_energy_cost", "kind": "mult", "default": 1 }
    ] }
    """;

    private readonly ContentDb _db = TestContent.Create(new() { ["perks.json"] = Perks, ["spells.json"] = Spells, ["player_stats.json"] = Stats });

    private PlayerCharacter Pc()
    {
        var pc = new PlayerCharacter(_db);
        pc.Load(new PlayerSave { Level = 10 });
        return pc;
    }

    [Fact]
    public void Levels_come_with_casts()
    {
        Assert.Equal(0, SpellMastery.LevelFor(SpellMastery.Thresholds[0] - 1));
        Assert.Equal(1, SpellMastery.LevelFor(SpellMastery.Thresholds[0]));
        Assert.Equal(SpellMastery.MaxLevel, SpellMastery.LevelFor(int.MaxValue));
    }

    [Fact]
    public void Upgrades_spread_over_the_levels_in_order()
    {
        Assert.Equal(new[] { 1 }, Enumerable.Range(0, 1).Select(i => SpellMastery.UnlockLevel(i, 1)));
        Assert.Equal(new[] { 1, 3 }, Enumerable.Range(0, 2).Select(i => SpellMastery.UnlockLevel(i, 2)));
        Assert.Equal(new[] { 1, 1, 2, 3, 4, 5 }, Enumerable.Range(0, 6).Select(i => SpellMastery.UnlockLevel(i, 6)));
    }

    [Fact]
    public void Casting_grants_the_spells_upgrades_for_free_once_each()
    {
        var pc = Pc();
        int points = pc.Perks.AvailablePoints(pc.Progress);
        int? up = null;
        for (int i = 0; i < SpellMastery.Thresholds[0]; i++) up = pc.RecordCast("spell.blink") ?? up;
        Assert.Equal(1, up);
        Assert.Equal(1, pc.Perks.TimesBought("perk.blink_far"));
        Assert.Equal(0, pc.Perks.TimesBought("perk.blink_dmg")); // its turn comes at level 3
        Assert.Equal(10, pc.Stats.Get("max_health"));
        pc.SetMastery("spell.blink", SpellMastery.MaxLevel);
        Assert.Equal(15, pc.Stats.Get("max_health")); // the stackable upgrade applies once
        Assert.Equal(points, pc.Perks.AvailablePoints(pc.Progress));
        Assert.Equal(BuyResult.NotBuyable, pc.Perks.CanBuy("perk.blink_far", pc.Progress));
        pc.Respec();
        Assert.Equal(1, pc.Perks.TimesBought("perk.blink_far")); // a respec never takes mastery away
    }

    [Fact]
    public void Each_level_makes_the_spell_cheaper_and_faster()
    {
        var pc = Pc();
        double cost = pc.Spells.CostOf("spell.blink", pc.Stats).Total, cd = pc.Spells.CooldownOf("spell.blink", pc.Stats);
        pc.SetMastery("spell.blink", 2);
        Assert.Equal(cost * (1 - 2 * SpellMastery.CutPerLevel), pc.Spells.CostOf("spell.blink", pc.Stats).Total, 6);
        Assert.Equal(cd * (1 - 2 * SpellMastery.CutPerLevel), pc.Spells.CooldownOf("spell.blink", pc.Stats), 6);
        Assert.Equal(50, pc.Spells.CostOf("spell.frenzy", pc.Stats).Total, 6); // other spells untouched
    }

    [Fact]
    public void An_upgrade_bought_before_mastery_is_given_back()
    {
        var tree = new PerkTree(_db);
        tree.Restore(new Dictionary<string, int> { ["perk.blink_far"] = 1, ["perk.root"] = 1 }, Array.Empty<string>());
        Assert.Equal(0, tree.TimesBought("perk.blink_far"));
        Assert.Equal(1, tree.SpentPoints);
    }

    [Fact]
    public void Casts_survive_a_save()
    {
        var pc = Pc();
        pc.SetMastery("spell.blink", 2);
        var loaded = new PlayerCharacter(_db);
        loaded.Load(SaveCodec.ReadPlayer(SaveCodec.Write(pc.ToSave())));
        Assert.Equal(2, loaded.Mastery.Level("spell.blink"));
        Assert.Equal(1, loaded.Perks.TimesBought("perk.blink_far"));
    }
}
