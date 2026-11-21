using ChampionsOfTheSons.Core.Rules;
using Xunit;

namespace ChampionsOfTheSons.Core.Tests;

public class HintsTests
{
    private static HintContext Fresh(int level = 1, int points = 0) =>
        new(level, points, BagItems: 0, EquippedItems: 0, SpellsUnlocked: 0, SpellsEquipped: 0,
            InCoop: false, HasMergeSet: false, HasUsableItem: false, StreakSeen: false);

    [Fact]
    public void Shows_the_first_hint_that_applies_and_was_not_seen()
    {
        var seen = new HashSet<string>();
        Assert.Equal("welcome", Hints.Next(Fresh(points: 2), seen));
        seen.Add("welcome");
        Assert.Equal("perk_points", Hints.Next(Fresh(points: 2), seen));
        seen.Add("perk_points");
        Assert.Null(Hints.Next(Fresh(points: 2), seen));
    }

    [Fact]
    public void A_hint_waits_until_its_condition_holds()
    {
        var seen = new HashSet<string> { "welcome", "settings" };
        Assert.Null(Hints.Next(Fresh(level: 9), seen));
        Assert.Equal("no_spell", Hints.Next(Fresh(level: 10), seen));
    }

    [Fact]
    public void Hint_ids_are_unique()
    {
        Assert.Equal(Hints.All.Count, Hints.All.Select(h => h.Id).Distinct().Count());
    }
}
