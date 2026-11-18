using ChampionsOfTheSons.Core.Rules;

namespace ChampionsOfTheSons.Core.Tests;

public class ProgressionTests
{
    [Fact]
    public void Xp_goal_grows_with_level_and_matches_the_curve()
    {
        Assert.Equal(30, XpCurve.Goal(1));
        for (int l = 1; l < XpCurve.PracticalMaxLevel; l++)
            Assert.True(XpCurve.Goal(l + 1) > XpCurve.Goal(l), $"goal({l + 1}) <= goal({l})");
    }

    [Fact]
    public void Exact_goal_levels_up_once_and_keeps_the_remainder()
    {
        var p = new Progress();
        var r = p.AddXp(XpCurve.Goal(1) + 5);
        Assert.Equal(1, r.OldLevel);
        Assert.Equal(2, r.NewLevel);
        Assert.Equal(5, p.Xp, 6);
    }

    [Fact]
    public void Large_xp_gives_several_levels_and_their_rewards()
    {
        var p = new Progress();
        double total = Enumerable.Range(1, 25).Sum(XpCurve.Goal);
        var r = p.AddXp(total);
        Assert.Equal(26, p.Level);
        // levels 10 and 20: Heart of Purity; level 25: Greater Mutated Heart
        Assert.Equal(new[] { Progress.HeartOfPurity, Progress.HeartOfPurity, Progress.GreaterMutatedHeart }, r.RewardItemIds);
    }

    [Fact]
    public void Xp_gain_stat_scales_xp()
    {
        var p = new Progress();
        p.AddXp(10, expGain: 2);
        Assert.Equal(20, p.Xp, 6);
    }

    [Fact]
    public void Death_keeps_the_level_and_clears_xp()
    {
        var p = new Progress(12, 500, 0);
        p.OnDeath();
        Assert.Equal(12, p.Level);
        Assert.Equal(0, p.Xp);
    }

    [Fact]
    public void Perk_points_are_level_plus_bonus()
    {
        Assert.Equal(15, new Progress(12, 0, 3).TotalPerkPoints);
    }

    [Fact]
    public void Attribute_base_grows_by_rounded_square_roots()
    {
        // A(10) = 22 in the original, base = 1 + A(L)
        Assert.Equal(23, XpCurve.AttributeBase(10));
        Assert.True(XpCurve.AttributeBase(50) > XpCurve.AttributeBase(49));
    }
}

public class MassacreTests
{
    [Fact]
    public void Streak_pays_banked_xp_times_multiplier_when_the_timer_ends()
    {
        var m = new Massacre();
        for (int i = 0; i < 5; i++) m.OnKill(100, maxTime: 15, bonusPerKill: 7.5);
        Assert.Equal(0, m.Tick(14.9));
        Assert.Equal(500 * 1.5, m.Tick(1), 6);
        Assert.False(m.Active);
        Assert.Equal(0, m.Kills);
    }

    [Fact]
    public void Kills_extend_the_timer_up_to_the_max()
    {
        var m = new Massacre();
        m.OnKill(1, 15, 7.5);
        m.Tick(10);
        m.OnKill(1, 15, 7.5);
        Assert.Equal(12.5, m.TimeLeft, 6);
        m.OnKill(1, 15, 7.5);
        Assert.Equal(15, m.TimeLeft, 6);
    }

    [Fact]
    public void Multiplier_never_decreases_with_more_kills()
    {
        for (int k = 0; k < 200; k++)
            Assert.True(Massacre.Multiplier(k + 1) >= Massacre.Multiplier(k));
        Assert.Equal(1000, Massacre.Multiplier(100));
    }
}
