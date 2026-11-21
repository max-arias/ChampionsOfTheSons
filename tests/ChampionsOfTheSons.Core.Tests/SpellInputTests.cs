using ChampionsOfTheSons.Core.Content;
using ChampionsOfTheSons.Core.Rules;
using Xunit;

namespace ChampionsOfTheSons.Core.Tests;

/// <summary>COS-82: cast types and silence.</summary>
public class SpellInputTests
{
    [Fact]
    public void A_release_spell_aims_while_held_and_casts_on_key_up()
    {
        var input = new SpellInput();
        Assert.Equal(SpellInputAction.BeginAim, input.Step(0, true, CastMode.Release, true, false));
        Assert.Equal(SpellInputAction.None, input.Step(0, true, CastMode.Release, true, false));
        Assert.True(input.IsAiming(0));
        Assert.Equal(SpellInputAction.Cast, input.Step(0, false, CastMode.Release, true, false));
        Assert.False(input.IsAiming(0));
    }

    [Fact]
    public void Pressing_a_second_aimed_spell_drops_the_first_aim_without_casting_it()
    {
        var input = new SpellInput();
        input.Step(0, true, CastMode.Release, true, false);
        input.Step(1, true, CastMode.Release, true, false);
        Assert.Equal(1, input.AimingSlot);
        Assert.Equal(SpellInputAction.None, input.Step(0, false, CastMode.Release, true, false));
        Assert.Equal(SpellInputAction.Cast, input.Step(1, false, CastMode.Release, true, false));
    }

    [Fact]
    public void An_instant_spell_casts_on_press_and_again_while_held_once_ready()
    {
        var input = new SpellInput();
        Assert.Equal(SpellInputAction.Cast, input.Step(0, true, CastMode.Instant, false, false));
        Assert.Equal(SpellInputAction.None, input.Step(0, true, CastMode.Instant, false, false));
        Assert.Equal(SpellInputAction.Cast, input.Step(0, true, CastMode.Instant, true, false));
        Assert.Equal(SpellInputAction.None, input.Step(0, false, CastMode.Instant, true, false));
    }

    [Fact]
    public void A_channel_runs_while_held_and_stops_on_release()
    {
        var input = new SpellInput();
        Assert.Equal(SpellInputAction.Cast, input.Step(0, true, CastMode.Channel, true, false));
        Assert.Equal(SpellInputAction.None, input.Step(0, true, CastMode.Channel, true, true));
        Assert.Equal(SpellInputAction.StopChannel, input.Step(0, false, CastMode.Channel, true, true));
    }

    [Fact]
    public void Silence_blocks_every_cast_but_still_lets_a_running_spell_be_switched_off()
    {
        var db = TestContent.Create(new()
        {
            ["player_stats.json"] = """
            { "playerStats": [
              { "id": "silenced", "kind": "bool", "default": 0 },
              { "id": "max_health", "kind": "add", "default": 0 },
              { "id": "crit_chance", "kind": "add", "default": 0 },
              { "id": "all_damage_taken", "kind": "mult", "default": 1 },
              { "id": "i_infinity_loop", "kind": "bool", "default": 0 }
            ] }
            """,
            ["spells.json"] = """
            { "spells": [
              { "id": "spell.blink", "name": "Blink", "energyCost": 25, "cooldown": 0 },
              { "id": "spell.sustain_shield", "name": "Shield", "energyCost": 5, "cooldown": 0, "channeled": true }
            ] }
            """,
        });
        var book = new SpellBook(db);
        var stats = new StatSheet(db);
        book.Restore(new[] { "spell.blink", "spell.sustain_shield" });
        Assert.Equal(CastResult.Ok, book.TryCast(1, 1000, stats, out _));
        stats.SetSource("buff.silence", new[] { new StatModifier(StatIds.Silenced, EffectOp.Enable, 1) });
        Assert.Equal(CastResult.Silenced, book.TryCast(0, 1000, stats, out _));
        Assert.True(book.IsReady(0));
        Assert.Equal(CastResult.Stopped, book.TryCast(1, 1000, stats, out _));
    }
}
