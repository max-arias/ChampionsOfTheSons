using ChampionsOfTheSons.Core.Rules;
using Xunit;

namespace ChampionsOfTheSons.Core.Tests;

public class EnemyAuraTests
{
    [Fact]
    public void The_first_ability_with_an_element_sets_the_aura()
    {
        Assert.Equal(EnemyElement.Cold, EnemyAura.ElementOf(new[] { EnemyAbility.Juggernaut, EnemyAbility.Blizzard, EnemyAbility.Meteor }));
        Assert.Equal(EnemyElement.None, EnemyAura.ElementOf(new[] { EnemyAbility.Juggernaut, EnemyAbility.ExtraHealth }));
    }

    [Fact]
    public void A_boss_outshines_a_miniboss_and_more_abilities_grow_the_aura()
    {
        Assert.True(EnemyAura.Intensity(EnemyTier.Boss, 0) > EnemyAura.Intensity(EnemyTier.Miniboss, EnemyAura.MaxAbilities) - 1e-9);
        Assert.True(EnemyAura.Intensity(EnemyTier.Boss, 6) > EnemyAura.Intensity(EnemyTier.Boss, 2));
        Assert.Equal(EnemyAura.Intensity(EnemyTier.Boss, EnemyAura.MaxAbilities), EnemyAura.Intensity(EnemyTier.Boss, 20));
        Assert.Equal(0, EnemyAura.Intensity(EnemyTier.Elite, 5));
    }
}
