using ChampionsOfTheSons.Core.Content;
using ChampionsOfTheSons.Core.Rules;

namespace ChampionsOfTheSons.Core.Tests;

/// <summary>COS-148: fire duration and tick rate on the player's burns.</summary>
public class FireRulesTests
{
    private static readonly ContentDb Db = TestContent.Create(new()
    {
        ["player_stats.json"] = TestContent.StatsWith("""{ "id": "fire_duration", "kind": "add", "default": 0 }""", """{ "id": "fire_tick_rate", "kind": "add", "default": 0 }"""),
    });

    private static StatSheet Sheet(double duration, double tickRate)
    {
        var s = new StatSheet(Db);
        s.SetSource("test", new[] { new StatModifier(FireRules.Duration, EffectOp.Add, duration), new StatModifier(FireRules.TickRate, EffectOp.Add, tickRate) });
        return s;
    }

    [Fact]
    public void No_fire_stats_leave_the_burn_alone() => Assert.Equal((10.0, 4.0), FireRules.Burn(Sheet(0, 0), 10, 4));

    [Fact]
    public void Longer_burns_and_faster_ticks_scale_time_and_damage_per_second()
    {
        // "Burn 700% longer, ticks 300% faster": 8x the time and 4x the damage per second.
        var (perSecond, seconds) = FireRules.Burn(Sheet(7, 3), 10, 4);
        Assert.Equal(40, perSecond, 6);
        Assert.Equal(32, seconds, 6);
    }

    [Fact]
    public void Greatbow_burn_is_a_share_of_the_hit() =>
        Assert.Equal((100 * SpellTuning.FireboltBurnFraction, SpellTuning.FireboltBurnSeconds), FireRules.GreatBowBurn(0, 0, 100));
}

/// <summary>COS-148: charged melee attacks and hammer smashes.</summary>
public class ChargedAttackTests
{
    private static readonly ContentDb Db = TestContent.Create(new()
    {
        ["player_stats.json"] = TestContent.StatsWith("""{ "id": "heavy_attack_dmg", "kind": "mult", "default": 1 }""", """{ "id": "smash_damage", "kind": "mult", "default": 1 }"""),
    });

    private static StatSheet Sheet(double heavy, double smash)
    {
        var s = new StatSheet(Db);
        s.SetSource("test", new[] { new StatModifier(MeleeRules.HeavyAttackDamage, EffectOp.Mult, heavy), new StatModifier(MeleeRules.SmashDamage, EffectOp.Mult, smash) });
        return s;
    }

    [Fact]
    public void Plain_swings_ignore_both_stats() => Assert.Equal(1, MeleeRules.ChargedMultiplier(Sheet(3, 4.5), charged: false, hammer: true));

    [Fact]
    public void Charged_attacks_use_heavy_damage_and_hammers_add_smash()
    {
        Assert.Equal(3, MeleeRules.ChargedMultiplier(Sheet(3, 4.5), charged: true, hammer: false), 6);
        Assert.Equal(13.5, MeleeRules.ChargedMultiplier(Sheet(3, 4.5), charged: true, hammer: true), 6);
    }
}
