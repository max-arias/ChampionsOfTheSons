using System.Numerics;
using ChampionsOfTheSons.Core.Rules;

namespace ChampionsOfTheSons.Core.Tests;

public class FlickerStrikeTests
{
    [Fact]
    public void Route_hops_to_the_nearest_unvisited_enemy_from_where_the_caster_stands()
    {
        var targets = new[] { new Vector3(10, 0, 0), new Vector3(2, 0, 0), new Vector3(4, 0, 3), new Vector3(30, 0, 0) };
        // From the start the nearest is #1, then from #1 the nearest is #2 (dist 3.6), then #0; #3 is out of range.
        Assert.Equal(new[] { 1, 2, 0 }, FlickerStrikeRules.Route(Vector3.Zero, targets));
    }

    [Fact]
    public void Route_visits_at_most_eight()
    {
        var targets = Enumerable.Range(1, 12).Select(i => new Vector3(i, 0, 0)).ToArray();
        Assert.Equal(FlickerStrikeRules.MaxTargets, FlickerStrikeRules.Route(Vector3.Zero, targets).Count);
    }

    [Fact]
    public void Hop_pause_keeps_the_run_short_and_the_combo_grows_per_target()
    {
        Assert.Equal(0.12, FlickerStrikeRules.HopPause(3), 6);
        Assert.Equal(0.12, FlickerStrikeRules.HopPause(8), 6); // 1 s / 8 is above the cap: the cap holds
        Assert.Equal(0.1, FlickerStrikeRules.HopPause(10), 6); // more targets than the budget allows: shorter hops
        Assert.Equal(1.0, FlickerStrikeRules.Combo(1), 6);
        Assert.Equal(1.7, FlickerStrikeRules.Combo(8), 6);
    }
}
