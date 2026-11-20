using ChampionsOfTheSons.Core.Content;
using ChampionsOfTheSons.Core.Rules;

namespace ChampionsOfTheSons.Core.Tests;

public class EnemyInfoTests
{
    [Theory]
    [InlineData(1, 10, DangerRating.Trivial)]  // far below
    [InlineData(10, 10, DangerRating.Even)]
    [InlineData(13, 10, DangerRating.Even)]    // one step (3 levels) above
    [InlineData(14, 10, DangerRating.Tough)]
    [InlineData(20, 10, DangerRating.Dangerous)]
    [InlineData(120, 100, DangerRating.Tough)] // steps grow with the player's level: 10 per step at 100
    [InlineData(75, 100, DangerRating.Trivial)]
    public void Danger_follows_the_level_gap(int enemy, int player, DangerRating expected) =>
        Assert.Equal(expected, EnemyInfo.Danger(EnemyTier.Normal, enemy, player));

    [Fact]
    public void Tiers_raise_danger_and_it_caps_at_deadly()
    {
        Assert.Equal(DangerRating.Tough, EnemyInfo.Danger(EnemyTier.Elite, 10, 10));
        Assert.Equal(DangerRating.Tough, EnemyInfo.Danger(EnemyTier.Miniboss, 10, 10));
        Assert.Equal(DangerRating.Dangerous, EnemyInfo.Danger(EnemyTier.Boss, 10, 10));
        Assert.Equal(DangerRating.Deadly, EnemyInfo.Danger(EnemyTier.Boss, 50, 10));
        Assert.Equal(DangerRating.Even, EnemyInfo.Danger(EnemyTier.Elite, 1, 30));
    }

    [Fact]
    public void Only_tiered_enemies_get_a_name()
    {
        var rng = new Rng(7);
        string[] given = { "g.a", "g.b" }, epithets = { "e.a" };
        Assert.Null(EnemyInfo.PickName(EnemyTier.Normal, given, epithets, rng));
        var name = EnemyInfo.PickName(EnemyTier.Elite, given, epithets, rng);
        Assert.NotNull(name);
        Assert.Contains(name!.GivenId, given);
        Assert.Equal("e.a", name.EpithetId);
        Assert.Null(EnemyInfo.PickName(EnemyTier.Boss, Array.Empty<string>(), epithets, rng));
    }

    [Fact]
    public void Elite_names_load_by_part_and_reject_unknown_parts()
    {
        const string names = """
        { "eliteNames": [
          { "id": "n.b", "part": "given", "name": "B" },
          { "id": "n.a", "part": "given", "name": "A" },
          { "id": "n.e", "part": "epithet", "name": "the E" },
          { "id": "n.x", "part": "surname", "name": "X" }
        ] }
        """;
        var db = ContentDb.Load(new Dictionary<string, string>
        {
            ["player_stats.json"] = TestContent.PlayerStats,
            ["item_stats.json"] = TestContent.ItemStats,
            ["items.json"] = TestContent.Items,
            ["elite_names.json"] = names,
        });
        Assert.Equal(new[] { "n.a", "n.b" }, db.EliteGivenIds);
        Assert.Equal(new[] { "n.e" }, db.EliteEpithetIds);
        Assert.Single(db.Errors);
        Assert.Contains("n.x", db.Errors[0]);
    }
}
