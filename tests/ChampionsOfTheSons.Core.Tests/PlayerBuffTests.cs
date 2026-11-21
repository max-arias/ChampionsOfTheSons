using ChampionsOfTheSons.Core.Content;
using ChampionsOfTheSons.Core.Rules;
using Xunit;

namespace ChampionsOfTheSons.Core.Tests;

/// <summary>COS-89: one player buff system: refresh, stacking rules, immunities, resistance, dispel.</summary>
public class PlayerBuffTests
{
    private static readonly string Stats = TestContent.StatsWith(
        """{ "id": "attack_speed", "kind": "multi", "default": 1, "defaultMult": 1 }""",
        """{ "id": "stun_immunity", "kind": "add", "default": 0 }""",
        """{ "id": "stunned", "kind": "bool", "default": 0 }""",
        """{ "id": "debuff_immunity", "kind": "add", "default": 0 }""",
        """{ "id": "debuff_resistance", "kind": "add", "default": 0 }""");

    private const string Buffs = """
    { "buffs": [
      { "id": "buff.attack_slow", "stack": "overwrite", "dispelLevel": 1, "effects": [ { "stat": "attack_speed", "op": "mult" } ] },
      { "id": "buff.poison", "stack": "accumulate", "dispelLevel": 1 },
      { "id": "buff.stun", "stack": "max", "dispelLevel": 2, "blockedBy": "stun_immunity", "effects": [ { "stat": "stunned", "op": "enable" } ] },
      { "id": "buff.curse", "stack": "max", "dispelLevel": 3 },
      { "id": "buff.stun_immune", "positive": true, "stack": "max", "effects": [ { "stat": "stun_immunity", "op": "add", "value": 1 } ] },
      { "id": "buff.debuff_immune", "positive": true, "stack": "max", "effects": [ { "stat": "debuff_immunity", "op": "add", "value": 1 } ] }
    ] }
    """;

    private readonly ContentDb _db = TestContent.Create(new() { ["player_stats.json"] = Stats, ["buffs.json"] = Buffs });

    private (PlayerBuffs Buffs, StatSheet Stats) Make()
    {
        var stats = new StatSheet(_db);
        return (new PlayerBuffs(stats, _db.Buffs), stats);
    }

    [Fact]
    public void The_same_source_refreshes_to_the_longer_time_and_stacks_by_the_buffs_rule()
    {
        var (b, s) = Make();
        Assert.Equal(BuffApply.Applied, b.Apply("buff.attack_slow", "blizzard", 6, 0.5));
        Assert.Equal(BuffApply.Refreshed, b.Apply("buff.attack_slow", "blizzard", 2, 0.25));
        Assert.Equal(0.25, s.Get("attack_speed"), 6);
        Assert.Equal(6, b.Remaining("buff.attack_slow", "blizzard"), 6);

        b.Apply("buff.poison", "spider", 6, 3);
        b.Apply("buff.poison", "spider", 6, 2);
        b.Apply("buff.poison", "snake", 6, 1);
        Assert.Equal(6, b.Total("buff.poison"), 6);
        Assert.Equal(2, b.CountOf("buff.poison"));
    }

    [Fact]
    public void Running_out_removes_the_buff_and_restores_the_stats()
    {
        var (b, s) = Make();
        b.Apply("buff.attack_slow", "x", 2, 0.5);
        b.Tick(2.5);
        Assert.False(b.Has("buff.attack_slow"));
        Assert.Equal(1, s.Get("attack_speed"), 6);
    }

    [Fact]
    public void An_immunity_blocks_its_state_and_ends_one_already_running()
    {
        var (b, s) = Make();
        b.Apply("buff.stun", "basher", 3);
        Assert.True(s.IsOn("stunned"));
        b.Apply("buff.stun_immune", "purge", 3);
        Assert.False(s.IsOn("stunned"));
        Assert.Equal(BuffApply.Immune, b.Apply("buff.stun", "basher", 3));
    }

    [Fact]
    public void Debuff_immunity_blocks_up_to_level_2_and_resistance_shortens()
    {
        var (b, s) = Make();
        s.SetSource("gear", new[] { new StatModifier("debuff_resistance", EffectOp.Add, 0.5) });
        b.Apply("buff.poison", "x", 10, 1);
        Assert.Equal(5, b.Remaining("buff.poison", "x"), 6);
        b.Apply("buff.debuff_immune", "golden_skin", 5);
        Assert.False(b.Has("buff.poison"));
        Assert.Equal(BuffApply.Resisted, b.Apply("buff.stun", "y", 3));
        Assert.Equal(BuffApply.Applied, b.Apply("buff.curse", "z", 3));
    }

    [Fact]
    public void Dispel_ends_negative_buffs_up_to_its_level_only()
    {
        var (b, _) = Make();
        b.Apply("buff.poison", "x", 10, 1);
        b.Apply("buff.curse", "z", 10);
        b.Apply("buff.stun_immune", "purge", 10);
        Assert.Equal(1, b.Dispel(2));
        Assert.True(b.Has("buff.curse"));
        Assert.True(b.Has("buff.stun_immune"));
    }
}
