using ChampionsOfTheSons.Core.Rules;

namespace ChampionsOfTheSons.Core.Tests;

public class NearDeathTests
{
    [Fact]
    public void Only_a_lethal_hit_with_the_perk_triggers_then_the_lockout_holds()
    {
        var nde = new NearDeathState();
        Assert.False(nde.TryTrigger(true, health: 100, damage: 50)); // not lethal
        Assert.False(nde.TryTrigger(false, health: 100, damage: 150)); // no perk
        Assert.True(nde.TryTrigger(true, health: 100, damage: 150));
        Assert.False(nde.TryTrigger(true, health: 100, damage: 150)); // locked out
        nde.Tick(NearDeathState.Cooldown);
        Assert.True(nde.TryTrigger(true, health: 100, damage: 150));
    }
}
