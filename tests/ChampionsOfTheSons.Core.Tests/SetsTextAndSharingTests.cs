using ChampionsOfTheSons.Core.Content;
using ChampionsOfTheSons.Core.Rules;

namespace ChampionsOfTheSons.Core.Tests;

public class SetBonusTests
{
    private const string Items = """
    { "items": [
      { "id": "item.ring", "name": "Test Ring", "type": "ring", "rarity": 2, "minLevel": 1, "maxLevel": 50, "statPools": [ ["stat.max_life"] ] },
      { "id": "item.boots", "name": "Test Boots", "type": "boot", "rarity": 0, "minLevel": 1, "maxLevel": 5, "statPools": [ ["stat.max_life"] ] },
      { "id": "item.set_helm", "name": "Set Helm", "type": "helmet", "rarity": 5, "set": "set.test", "statPools": [ ["stat.max_life"] ] },
      { "id": "item.set_boots", "name": "Set Boots", "type": "boot", "rarity": 5, "set": "set.test", "statPools": [ ["stat.max_life"] ] },
      { "id": "item.set_gloves", "name": "Set Gloves", "type": "glove", "rarity": 5, "set": "set.test", "statPools": [ ["stat.max_life"] ] }
    ] }
    """;

    private const string Sets = """
    { "sets": [ { "id": "set.test", "name": "Test Set", "bonuses": [
      { "pieces": 2, "effects": [ { "stat": "max_health", "op": "add", "value": 100 } ] },
      { "pieces": 3, "effects": [ { "stat": "i_infinity_loop", "op": "enable" } ] }
    ] } ] }
    """;

    [Fact]
    public void Set_bonuses_apply_at_their_piece_counts_and_go_away_when_unequipped()
    {
        var db = TestContent.Create(new() { ["items.json"] = Items, ["sets.json"] = Sets });
        var c = new PlayerCharacter(db);
        foreach (var id in new[] { "item.set_helm", "item.set_boots", "item.set_gloves" })
            c.Inventory.AddToBag(new ItemInstance { BaseId = id });

        c.Inventory.Equip("bag.0", db);
        Assert.Equal(0, c.Stats.Get("max_health"));
        c.Inventory.Equip("bag.1", db);
        Assert.Equal(100, c.Stats.Get("max_health"));
        Assert.False(c.Stats.IsOn("i_infinity_loop"));
        c.Inventory.Equip("bag.2", db);
        Assert.True(c.Stats.IsOn("i_infinity_loop"));

        c.Inventory.Move("equip.helmet", "bag.10", db);
        Assert.Equal(100, c.Stats.Get("max_health"));
        Assert.False(c.Stats.IsOn("i_infinity_loop"));
    }

    [Fact]
    public void Items_pointing_at_a_missing_set_are_reported()
    {
        var db = ContentDb.Load(new Dictionary<string, string>
        {
            ["player_stats.json"] = TestContent.PlayerStats,
            ["item_stats.json"] = TestContent.ItemStats,
            ["items.json"] = Items.Replace("set.test", "set.nope"),
            ["sets.json"] = Sets,
        });
        Assert.Contains(db.Errors, e => e.Contains("set.nope"));
    }
}

public class XpSharingAndToggleTests
{
    [Fact]
    public void Grouping_up_never_costs_a_player_much_and_the_pot_grows()
    {
        Assert.Equal(1000, EnemyScaler.KillXpShare(1000, 1), 6);
        Assert.Equal(600, EnemyScaler.KillXpShare(1000, 2), 6);
        Assert.True(EnemyScaler.KillXpShare(1000, 4) * 4 > 1000);
        Assert.Equal(0, EnemyScaler.KillXpShare(1000, 0));
    }

    [Fact]
    public void Host_disabled_abilities_never_roll()
    {
        var rng = new Rng(9);
        var kind = new EnemyKind { Id = "enemy.x" };
        var disabled = new[] { EnemyAbility.Meteor, EnemyAbility.BlackHole, EnemyAbility.Warp };
        for (int i = 0; i < 500; i++)
            Assert.DoesNotContain(EnemyScaler.PickAbilities(10, kind, Difficulties.Hell, rng, disabled), a => disabled.Contains(a));
    }

    [Fact]
    public void Radiance_needs_master_difficulty()
    {
        var rng = new Rng(10);
        var kind = new EnemyKind { Id = "enemy.x" };
        for (int i = 0; i < 500; i++)
            Assert.DoesNotContain(EnemyAbility.Radiance, EnemyScaler.PickAbilities(10, kind, Difficulties.All[2], rng));
    }
}

public class TextDbTests
{
    private static TextDb Db() => TextDb.Load(new Dictionary<string, string>
    {
        ["text/en.json"] = """{ "item.ring.name": "Ring", "item.ring.lore": "Old." }""",
        ["text\\de.json"] = """{ "item.ring.name": "Ring (de)" }""",
        ["items.json"] = "{}",
    });

    [Fact]
    public void Falls_back_to_english_then_to_the_key()
    {
        var db = Db();
        db.Language = "de";
        Assert.Equal("Ring (de)", db.Get("item.ring.name"));
        Assert.Equal("Old.", db.Get("item.ring.lore"));
        Assert.Equal("item.unknown.name", db.Get("item.unknown.name"));
        Assert.Equal(new[] { "item.ring.lore" }, db.MissingIn("de"));
    }

    [Fact]
    public void Broken_language_file_is_an_error_not_a_crash()
    {
        var db = TextDb.Load(new Dictionary<string, string> { ["text/en.json"] = "{}", ["text/fr.json"] = "{ nope" });
        Assert.Contains(db.Errors, e => e.Contains("fr.json"));
        Assert.Equal("x", db.Get("x"));
    }
}
