using ChampionsOfTheSons.Core.Content;
using ChampionsOfTheSons.Core.Rules;

namespace ChampionsOfTheSons.Core.Tests;

public class StatSheetTests
{
    private readonly StatSheet _sheet = new(TestContent.Create());

    [Fact]
    public void Adds_then_multiplies()
    {
        _sheet.SetSource("a", new[] { new StatModifier("max_health_mult", EffectOp.Add, 0.5) });
        _sheet.SetSource("b", new[] { new StatModifier("max_health_mult", EffectOp.Mult, 2), new StatModifier("max_health_mult", EffectOp.Add, 0.5) });
        // (1 + 0.5 + 0.5) * 1 * 2
        Assert.Equal(4, _sheet.Get("max_health_mult"), 6);
    }

    [Fact]
    public void Removing_a_source_restores_the_exact_previous_value()
    {
        _sheet.SetSource("ring", new[] { new StatModifier("crit_chance", EffectOp.Add, 0.0123456) });
        double before = _sheet.Get("crit_chance");
        for (int i = 0; i < 1000; i++)
        {
            _sheet.SetSource("gloves", new[] { new StatModifier("crit_chance", EffectOp.Add, 0.1 * i), new StatModifier("all_damage_taken", EffectOp.Mult, 0.97) });
            _sheet.RemoveSource("gloves");
        }
        Assert.Equal(before, _sheet.Get("crit_chance"));
        Assert.Equal(1, _sheet.Get("all_damage_taken"));
    }

    [Fact]
    public void Replacing_a_source_does_not_stack()
    {
        _sheet.SetSource("equip.ring1", new[] { new StatModifier("max_health", EffectOp.Add, 50) });
        _sheet.SetSource("equip.ring1", new[] { new StatModifier("max_health", EffectOp.Add, 20) });
        Assert.Equal(20, _sheet.Get("max_health"));
    }

    [Fact]
    public void Integer_stats_round()
    {
        _sheet.SetSource("a", new[] { new StatModifier("max_health", EffectOp.Add, 10.5) });
        Assert.Equal(11, _sheet.Get("max_health"));
    }

    [Fact]
    public void Bool_stat_is_on_while_any_source_enables_it_and_none_disables_it()
    {
        Assert.False(_sheet.IsOn("i_infinity_loop"));
        _sheet.SetSource("a", new[] { new StatModifier("i_infinity_loop", EffectOp.Enable, 0) });
        _sheet.SetSource("b", new[] { new StatModifier("i_infinity_loop", EffectOp.Enable, 0) });
        _sheet.RemoveSource("a");
        Assert.True(_sheet.IsOn("i_infinity_loop"));
        _sheet.SetSource("curse", new[] { new StatModifier("i_infinity_loop", EffectOp.Disable, 0) });
        Assert.False(_sheet.IsOn("i_infinity_loop"));
    }

    [Fact]
    public void Changed_fires_on_every_change()
    {
        int count = 0;
        _sheet.Changed += () => count++;
        _sheet.SetSource("a", Array.Empty<StatModifier>());
        _sheet.RemoveSource("a");
        _sheet.RemoveSource("missing");
        Assert.Equal(2, count);
    }
}
