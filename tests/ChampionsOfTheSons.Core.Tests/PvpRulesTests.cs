using ChampionsOfTheSons.Core.Rules;
using Xunit;

namespace ChampionsOfTheSons.Core.Tests;

public class PvpRulesTests
{
    [Theory]
    [InlineData("Off", 0)]
    [InlineData("Reduced", PvpRules.ReducedFactor)]
    [InlineData("Normal", 1)]
    [InlineData("something new", 0)]
    [InlineData(null, 0)]
    public void Spell_damage_between_players_follows_the_games_setting(string? setting, double factor) =>
        Assert.Equal(factor, PvpRules.Factor(PvpRules.Parse(setting)));
}
