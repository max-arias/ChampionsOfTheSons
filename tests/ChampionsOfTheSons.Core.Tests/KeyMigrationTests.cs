using ChampionsOfTheSons.Core.Rules;

namespace ChampionsOfTheSons.Core.Tests;

public class KeyMigrationTests
{
    [Fact]
    public void Keys_on_old_defaults_move_and_changed_keys_stay()
    {
        var plan = KeyMigration.Plan(new[]
        {
            ("spell_1", "z", "z", "f"),
            ("spell_2", "g", "x", "y"), // changed by hand
            ("perks", "P", "p", "u"),   // case does not matter
        });
        Assert.Equal("f", plan["spell_1"]);
        Assert.Equal("g", plan["spell_2"]);
        Assert.Equal("u", plan["perks"]);
    }

    [Fact]
    public void A_key_can_take_a_default_another_key_moves_away_from()
    {
        // Old slot 5 default "n" is new slot 4's default: both on old defaults, both move.
        var plan = KeyMigration.Plan(new[] { ("spell_4", "b", "b", "n"), ("spell_5", "n", "n", "comma") });
        Assert.Equal("n", plan["spell_4"]);
        Assert.Equal("comma", plan["spell_5"]);
    }

    [Fact]
    public void No_move_lands_on_a_key_another_entry_keeps()
    {
        var plan = KeyMigration.Plan(new[]
        {
            ("spell_3", "v", "v", "j"),
            ("ping", "j", "h", "h"),     // the player put ping on J
            ("spell_4", "b", "b", "v"), // would move onto spell_3's old key, which spell_3 now keeps
        });
        Assert.Equal("v", plan["spell_3"]);
        Assert.Equal("b", plan["spell_4"]);
        Assert.Equal("j", plan["ping"]);
    }
}
