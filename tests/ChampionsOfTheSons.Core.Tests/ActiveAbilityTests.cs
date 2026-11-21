using System.Numerics;
using ChampionsOfTheSons.Core.Rules;

namespace ChampionsOfTheSons.Core.Tests;

public class ActiveAbilityTests
{
    private static readonly EnemyAbility[] Active =
    {
        EnemyAbility.Meteor, EnemyAbility.Laser, EnemyAbility.BlackHole, EnemyAbility.Blizzard, EnemyAbility.Radiance,
        EnemyAbility.Flare, EnemyAbility.FireCataclysm, EnemyAbility.ArcaneCataclysm, EnemyAbility.Warp, EnemyAbility.Chains, EnemyAbility.Trapper,
    };

    [Fact]
    public void Every_active_affix_has_a_spec_and_passive_ones_do_not()
    {
        foreach (var a in Active) Assert.True(ActiveAbilityRules.IsActive(a), a.ToString());
        Assert.False(ActiveAbilityRules.IsActive(EnemyAbility.Tiny));
        Assert.False(ActiveAbilityRules.IsActive(EnemyAbility.Undead));
        Assert.Throws<ArgumentException>(() => ActiveAbilityRules.Spec(EnemyAbility.Juggernaut));
    }

    [Fact]
    public void Meteor_and_laser_roll_the_difficulty_table_and_the_rest_scale_meteor()
    {
        var rng = new Rng(1);
        for (int i = 0; i < 50; i++)
        {
            double m = ActiveAbilityRules.Damage(Difficulties.All[3], EnemyAbility.Meteor, rng);
            Assert.InRange(m, 660, 699);
            double l = ActiveAbilityRules.Damage(Difficulties.All[3], EnemyAbility.Laser, rng);
            Assert.InRange(l, 444, 469);
        }
        Assert.Equal(75000, ActiveAbilityRules.Damage(Difficulties.Hell, EnemyAbility.Meteor, rng));
        Assert.Equal(75000 * 0.05, ActiveAbilityRules.Damage(Difficulties.Hell, EnemyAbility.BlackHole, rng), 6);
        Assert.Equal(0, ActiveAbilityRules.Damage(Difficulties.Hell, EnemyAbility.Warp, rng));
    }

    [Fact]
    public void Casting_needs_the_target_inside_the_range_band()
    {
        var warp = ActiveAbilityRules.Spec(EnemyAbility.Warp);
        Assert.False(ActiveAbilityRules.InRange(warp, 10)); // too close to warp
        Assert.True(ActiveAbilityRules.InRange(warp, 30));
        Assert.False(ActiveAbilityRules.InRange(warp, 80));
        Assert.True(ActiveAbilityRules.InRange(ActiveAbilityRules.Spec(EnemyAbility.Meteor), 0));
    }

    [Fact]
    public void Clock_waits_then_fires_once_per_cooldown()
    {
        var clock = new AbilityClock(3);
        int casts = 0;
        for (int i = 0; i < 100; i++) // 10 s in 0.1 s steps
        {
            if (clock.Tick(0.1))
            {
                casts++;
                clock.Fire(4);
            }
            if (i < 28) Assert.Equal(0, casts); // not before the start delay
        }
        Assert.Equal(2, casts); // at 3 s and 7 s
    }

    [Fact]
    public void Start_delay_is_spread_per_enemy()
    {
        var rng = new Rng(7);
        var delays = Enumerable.Range(0, 20).Select(_ => AbilityClock.Start(rng).Left).ToList();
        Assert.All(delays, d => Assert.InRange(d, ActiveAbilityRules.StartDelayMin, ActiveAbilityRules.StartDelayMin + ActiveAbilityRules.StartDelaySpread));
        Assert.True(delays.Distinct().Count() > 1);
    }

    [Fact]
    public void Black_hole_pulls_harder_towards_the_centre_and_not_at_all_outside()
    {
        Assert.Equal(0, ActiveAbilityRules.BlackHolePull(12, 12));
        Assert.Equal(0, ActiveAbilityRules.BlackHolePull(20, 12));
        Assert.True(ActiveAbilityRules.BlackHolePull(2, 12) > ActiveAbilityRules.BlackHolePull(10, 12));
        Assert.True(ActiveAbilityRules.InBlackHoleCore(2.9, 12));
        Assert.False(ActiveAbilityRules.InBlackHoleCore(3.1, 12));
    }

    [Fact]
    public void Laser_beam_hits_only_inside_its_width_and_length()
    {
        var from = new Vector3(0, 0, 0);
        var to = new Vector3(20, 0, 0);
        Assert.True(ActiveAbilityRules.BeamHits(new Vector3(10, 0, 0.5f), from, to, 0.8));
        Assert.False(ActiveAbilityRules.BeamHits(new Vector3(10, 0, 2), from, to, 0.8));
        Assert.False(ActiveAbilityRules.BeamHits(new Vector3(25, 0, 0), from, to, 0.8)); // past the end
        Assert.True(ActiveAbilityRules.BeamHits(new Vector3(5, 0, 0), from, from, 6)); // degenerate beam = a point
    }

    [Fact]
    public void Warp_lands_on_the_enemys_side_of_the_player()
    {
        var p = ActiveAbilityRules.LandingPoint(new Vector3(40, 3, 0), new Vector3(0, 0, 0), 5);
        Assert.Equal(5, p.X, 3);
        Assert.Equal(0, p.Z, 3);
        var same = ActiveAbilityRules.LandingPoint(new Vector3(0, 0, 0), new Vector3(0, 0, 0), 5); // on top of the player: still defined
        Assert.Equal(5, new Vector3(same.X, 0, same.Z).Length(), 3);
    }

    [Fact]
    public void Cast_gate_allows_one_cast_at_a_time_and_a_lockout_after_it()
    {
        var gate = new CastGate();
        Assert.True(gate.TryBegin());
        Assert.False(gate.TryBegin()); // one at a time
        Assert.False(gate.CanStart);
        var rng = new Rng(7);
        gate.End(rng);
        Assert.InRange(gate.Lockout, 0, CastGate.MaxLockout);
        gate.Tick(CastGate.MaxLockout);
        Assert.True(gate.CanStart);
        Assert.True(gate.TryBegin());
        gate.Release(); // cut short: free at once, no lockout
        Assert.True(gate.CanStart);
    }

    [Fact]
    public void Cast_gate_lockout_is_shared_by_every_ability_of_the_enemy()
    {
        var s = new EnemyCombatState(new EnemyProfile { Abilities = new[] { EnemyAbility.Meteor, EnemyAbility.Laser }, MaxHealth = 100 }, Difficulties.Easy);
        Assert.True(s.Gate.TryBegin());
        s.Gate.End(new Rng(3));
        double lock1 = s.Gate.Lockout;
        if (lock1 > 0) Assert.False(s.Gate.CanStart);
        s.Tick(lock1 + 0.01, true); // the combat tick runs the lockout down
        Assert.True(s.Gate.CanStart);
    }
}
