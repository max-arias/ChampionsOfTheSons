using ChampionsOfTheSons.Core.Rules;

namespace ChampionsOfTheSons.Core.Tests;

public class DailyGrantTests
{
    [Fact]
    public void First_day_seen_is_a_baseline_then_each_later_day_pays_once()
    {
        var p = new Progress();
        Assert.False(p.TakeDailyGrant(4)); // first reading: baseline only
        Assert.False(p.TakeDailyGrant(4));
        Assert.True(p.TakeDailyGrant(5));
        Assert.False(p.TakeDailyGrant(5)); // the same day never pays twice
        Assert.True(p.TakeDailyGrant(9));  // several days later: one grant
        Assert.Equal(9, p.LastGrantDay);
    }

    [Fact]
    public void A_reload_keeps_the_day_so_the_grant_is_neither_repeated_nor_lost()
    {
        var p = new Progress { LastGrantDay = 6 };
        Assert.False(p.TakeDailyGrant(6));
        Assert.True(p.TakeDailyGrant(7));
    }
}
