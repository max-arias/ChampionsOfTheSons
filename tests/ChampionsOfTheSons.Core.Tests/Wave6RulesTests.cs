using ChampionsOfTheSons.Core.Rules;

namespace ChampionsOfTheSons.Core.Tests;

public class EnergyRefundTests
{
    [Fact]
    public void A_share_of_the_cost_comes_back_evenly_over_three_seconds()
    {
        var refund = new EnergyRefund();
        refund.Add(100, 0.15);
        Assert.Equal(5, refund.Tick(1), 6);
        refund.Add(30, 0.5); // a second cast overlaps the first
        Assert.Equal(5 + 5, refund.Tick(1), 6);
        Assert.Equal(5 + 10, refund.Tick(2), 6);
        Assert.Equal(0, refund.Tick(1), 6);
    }

    [Fact]
    public void Nothing_comes_back_without_the_perk() => Assert.Equal(0, Tick(new EnergyRefund(), 100, 0));

    private static double Tick(EnergyRefund r, double cost, double fraction)
    {
        r.Add(cost, fraction);
        return r.Tick(10);
    }
}

public class XpHeartDropTests
{
    [Fact]
    public void Bosses_always_drop_an_xp_heart_minibosses_about_one_in_four_others_never()
    {
        var rng = new Rng(7);
        Assert.All(Enumerable.Range(0, 200), _ => Assert.True(LootRoller.DropsXpHeart(EnemyTier.Boss, rng)));
        Assert.All(Enumerable.Range(0, 200), _ => Assert.False(LootRoller.DropsXpHeart(EnemyTier.Elite, rng)));
        Assert.All(Enumerable.Range(0, 200), _ => Assert.False(LootRoller.DropsXpHeart(EnemyTier.Normal, rng)));
        int hearts = Enumerable.Range(0, 4000).Count(_ => LootRoller.DropsXpHeart(EnemyTier.Miniboss, rng));
        Assert.InRange(hearts / 4000.0, 0.22, 0.28);
    }
}
