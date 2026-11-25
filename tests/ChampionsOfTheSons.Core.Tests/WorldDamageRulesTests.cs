using ChampionsOfTheSons.Core.Rules;
using Xunit;

namespace ChampionsOfTheSons.Core.Tests;

public class WorldDamageRulesTests
{
    [Fact]
    public void Tree_falls_only_once_its_pool_is_spent()
    {
        var (left, felled) = WorldDamageRules.TreeHit(WorldDamageRules.TreeHealth, WorldDamageRules.TreeHealth / 2);
        Assert.False(felled);
        (left, felled) = WorldDamageRules.TreeHit(left, WorldDamageRules.TreeHealth / 2);
        Assert.True(felled);
        Assert.Equal(0, left);
    }

    [Fact]
    public void No_damage_leaves_the_tree_standing()
    {
        Assert.Equal((10.0, false), WorldDamageRules.TreeHit(10, 0));
        Assert.Equal((10.0, false), WorldDamageRules.TreeHit(10, -5));
    }
}
