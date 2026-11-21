using ChampionsOfTheSons.Core.Content;
using ChampionsOfTheSons.Core.Rules;

namespace ChampionsOfTheSons.Core.Tests;

/// <summary>Wave 7: stats that belonged to stand-in spells get a use in today's spells.</summary>
public class StandInSpellStatTests
{
    private static StatSheet Sheet(params StatModifier[] mods)
    {
        string extra = """
            { "id": "debuff_resistance", "kind": "add", "default": 0 },
            { "id": "perk_multishot_projectile_count", "kind": "add", "default": 1, "integer": true },
            { "id": "perk_multishot_damage_pennalty", "kind": "mult", "default": 0.75 },
            { "id": "spell_snowstorm_max_charge", "kind": "add", "default": 10 },
            """;
        var stats = TestContent.PlayerStats.Replace("{ \"playerStats\": [", "{ \"playerStats\": [" + extra);
        var s = new StatSheet(TestContent.Create(new() { ["player_stats.json"] = stats }));
        s.SetSource("test", mods);
        return s;
    }

    [Fact]
    public void Debuff_resistance_shortens_debuffs_but_never_by_more_than_three_quarters()
    {
        Assert.Equal(8, SpellTuning.DebuffSeconds(Sheet(), 8), 6);
        Assert.Equal(6, SpellTuning.DebuffSeconds(Sheet(new StatModifier("debuff_resistance", EffectOp.Add, 0.25)), 8), 6);
        Assert.Equal(2, SpellTuning.DebuffSeconds(Sheet(new StatModifier("debuff_resistance", EffectOp.Add, 5)), 8), 6);
    }

    [Fact]
    public void Multishot_volley_size_and_falling_damage_per_arrow()
    {
        var plain = Sheet();
        Assert.Equal(1, SpellTuning.MultishotShots(plain));
        var perk = Sheet(new StatModifier("perk_multishot_projectile_count", EffectOp.Add, 2));
        Assert.Equal(3, SpellTuning.MultishotShots(perk));
        Assert.Equal(1, SpellTuning.MultishotCopyDamage(perk, 0), 6);
        Assert.Equal(0.5625, SpellTuning.MultishotCopyDamage(perk, 2), 6);
    }

    [Fact]
    public void Snow_storm_grows_with_max_charge()
    {
        Assert.Equal(1, SpellTuning.SnowStormScale(Sheet()), 6);
        Assert.Equal(1.5, SpellTuning.SnowStormScale(Sheet(new StatModifier("spell_snowstorm_max_charge", EffectOp.Add, 5))), 6);
    }
}
