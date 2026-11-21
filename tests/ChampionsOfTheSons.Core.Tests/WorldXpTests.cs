using ChampionsOfTheSons.Core.Content;
using ChampionsOfTheSons.Core.Rules;
using Xunit;

namespace ChampionsOfTheSons.Core.Tests;

public class WorldXpTests
{
    [Fact]
    public void Animal_xp_stays_in_its_range()
    {
        var rabbit = new AnimalDef { Actor = "Rabbit", XpMin = 45, XpMax = 59 };
        var rng = new Rng(7);
        for (int i = 0; i < 200; i++)
        {
            double xp = WorldXp.AnimalXp(rabbit, rng);
            Assert.InRange(xp, 45, 59);
        }
    }

    [Fact]
    public void Only_animals_worth_more_than_the_floor_drop_by_default_half_the_time()
    {
        var a = new AnimalDef { Actor = "Deer", XpMin = 75, XpMax = 84 };
        var rng = new Rng(3);
        Assert.All(Enumerable.Range(0, 100), _ => Assert.Empty(WorldXp.DropWorths(a, 80, rng)));
        int drops = Enumerable.Range(0, 2000).Count(_ => WorldXp.DropWorths(a, 83, rng).Count == 1);
        Assert.InRange(drops, 850, 1150);
    }

    [Fact]
    public void A_shark_always_drops_at_its_own_worths()
    {
        var shark = new AnimalDef { Actor = "Shark", XpMin = 1500, XpMax = 1500, Everyone = true, DropChance = 1, DropWorths = { 270, 310, 370 } };
        Assert.Equal(new[] { 270.0, 310, 370 }, WorldXp.DropWorths(shark, 1500, new Rng(1)));
        Assert.Equal(1500, WorldXp.AnimalXp(shark, new Rng(1)));
    }

    [Fact]
    public void Bush_xp_is_three_to_five()
    {
        var rng = new Rng(11);
        var seen = Enumerable.Range(0, 300).Select(_ => WorldXp.BushXp(rng)).Distinct().OrderBy(x => x).ToArray();
        Assert.Equal(new[] { 3.0, 4, 5 }, seen);
    }
}
