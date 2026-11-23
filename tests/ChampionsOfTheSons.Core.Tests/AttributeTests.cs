using ChampionsOfTheSons.Core.Content;
using ChampionsOfTheSons.Core.Rules;
using ChampionsOfTheSons.Core.Saving;

namespace ChampionsOfTheSons.Core.Tests;

/// <summary>COS-174: attribute points the player spends.</summary>
public class AttributeTests
{
    private const string Stats = """
    { "playerStats": [
      { "id": "strength", "kind": "add", "default": 0, "integer": true },
      { "id": "agility", "kind": "add", "default": 0, "integer": true },
      { "id": "vitality", "kind": "add", "default": 0, "integer": true },
      { "id": "intelligence", "kind": "add", "default": 0, "integer": true },
      { "id": "max_health", "kind": "add", "default": 0 },
      { "id": "max_health_mult", "kind": "multi", "default": 1, "defaultMult": 1 },
      { "id": "max_health_from_vit", "kind": "add", "default": 0 },
      { "id": "melee_increased_dmg", "kind": "multi", "default": 1, "defaultMult": 1 },
      { "id": "melee_dmg_from_str", "kind": "add", "default": 0 },
      { "id": "all_damage", "kind": "multi", "default": 1, "defaultMult": 1 },
      { "id": "cooldown", "kind": "mult", "default": 1 },
      { "id": "crit_chance", "kind": "add", "default": 0.05 },
      { "id": "all_damage_taken", "kind": "mult", "default": 1 },
      { "id": "i_infinity_loop", "kind": "bool", "default": 0 }
    ] }
    """;

    private readonly ContentDb _db = TestContent.Create(new() { ["player_stats.json"] = Stats });

    private PlayerCharacter Pc(int level)
    {
        var pc = new PlayerCharacter(_db);
        pc.Load(new PlayerSave { Level = level });
        return pc;
    }

    [Fact]
    public void Level_one_has_the_start_points_and_levels_add_more()
    {
        Assert.Equal(AttributeRules.StartPoints, AttributeRules.TotalPoints(1));
        Assert.True(AttributeRules.TotalPoints(2) > AttributeRules.TotalPoints(1));
        Assert.True(AttributeRules.TotalPoints(30) > AttributeRules.TotalPoints(20));
        Assert.Equal(AttributeRules.StartPoints, Pc(1).AttributePointsLeft);
    }

    [Fact]
    public void Spending_is_capped_by_the_points_left_and_refunds_are_free()
    {
        var pc = Pc(1);
        Assert.Equal(4, pc.SpendAttribute(StatIds.Strength, 4));
        Assert.Equal(AttributeRules.StartPoints - 4, pc.SpendAttribute(StatIds.Vitality, 999));
        Assert.Equal(0, pc.AttributePointsLeft);
        Assert.Equal(0, pc.SpendAttribute(StatIds.Agility));
        Assert.Equal(2, pc.RefundAttribute(StatIds.Strength, 2));
        Assert.Equal(2, pc.AttributePointsLeft);
        Assert.Equal(0, pc.RefundAttribute(StatIds.Agility));
        pc.ResetAttributes();
        Assert.Equal(AttributeRules.StartPoints, pc.AttributePointsLeft);
        Assert.Equal(0, pc.Stats.Get(StatIds.Strength));
    }

    [Fact]
    public void Points_raise_the_attribute_and_its_own_bonuses()
    {
        var pc = Pc(1);
        double hp = Derived.MaxHealth(pc.Stats), melee = Derived.MeleeMultiplier(pc.Stats);
        pc.SpendAttribute(StatIds.Strength, 5);
        pc.SpendAttribute(StatIds.Vitality, 5);
        Assert.Equal(5, pc.Stats.Get(StatIds.Strength));
        Assert.True(Derived.MeleeMultiplier(pc.Stats) > melee);
        Assert.True(Derived.MaxHealth(pc.Stats) > hp);
        Assert.Equal(1, pc.Stats.Get(StatIds.Cooldown)); // no Intelligence spent: cooldowns unchanged
    }

    [Fact]
    public void Intelligence_cooldown_cut_stops_at_its_cap()
    {
        Assert.Equal(0, AttributeRules.CooldownCut(0));
        Assert.True(AttributeRules.CooldownCut(100) > AttributeRules.CooldownCut(10));
        Assert.Equal(AttributeRules.MaxCooldownCut, AttributeRules.CooldownCut(1_000_000));
    }

    [Fact]
    public void A_lower_level_takes_back_what_no_longer_fits_from_the_largest_first()
    {
        var pc = Pc(10);
        int total = AttributeRules.TotalPoints(10);
        pc.SpendAttribute(StatIds.Strength, total - 3);
        pc.SpendAttribute(StatIds.Agility, 3);
        pc.SetLevel(1);
        Assert.Equal(AttributeRules.TotalPoints(1), pc.Attributes.Total);
        Assert.Equal(3, pc.Attributes.Get(StatIds.Agility));
        Assert.Equal(0, pc.AttributePointsLeft);
    }

    [Fact]
    public void Spent_points_survive_a_save_and_an_old_save_gets_all_points_to_spend()
    {
        var pc = Pc(5);
        pc.SpendAttribute(StatIds.Intelligence, 6);
        var loaded = new PlayerCharacter(_db);
        loaded.Load(SaveCodec.ReadPlayer(SaveCodec.Write(pc.ToSave())));
        Assert.Equal(6, loaded.Attributes.Get(StatIds.Intelligence));
        Assert.Equal(pc.AttributePointsLeft, loaded.AttributePointsLeft);

        var old = SaveCodec.ReadPlayer("{\"version\":1,\"level\":5}");
        Assert.Equal(PlayerSave.CurrentVersion, old.Version);
        Assert.Empty(old.Attributes);
        var fromOld = new PlayerCharacter(_db);
        fromOld.Load(old);
        Assert.Equal(AttributeRules.TotalPoints(5), fromOld.AttributePointsLeft);
    }

    [Fact]
    public void Strength_carries_more_of_every_kind()
    {
        Assert.Empty(ItemCarry.FromStrength(0));
        var carry = ItemCarry.FromStrength(100);
        Assert.Equal(AttributeRules.CarryBonus(100), carry["Stick"]);
        Assert.Equal(carry["Stick"], carry["Meat"]);
    }
}
