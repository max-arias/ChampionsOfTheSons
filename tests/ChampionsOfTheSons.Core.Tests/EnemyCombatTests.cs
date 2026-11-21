using ChampionsOfTheSons.Core.Rules;

namespace ChampionsOfTheSons.Core.Tests;

public class EnemyCombatStateTests
{
    private static EnemyCombatState State(params EnemyAbility[] abilities) =>
        new(new EnemyProfile { Abilities = abilities, MaxHealth = 1000, Armor = 100, DamageMult = 1000 }, Difficulties.Easy);

    [Fact]
    public void Nothing_advances_while_the_enemy_is_dead_or_inactive()
    {
        var s = State();
        s.ApplyDot("bleed", DotKind.Bleed, 10, 5);
        s.ApplySlow("a", 0.5, 2);
        Assert.Empty(s.Tick(10, aliveAndActive: false));
        Assert.True(s.HasSlow);
        Assert.Equal(5, Enumerable.Range(0, 5).Sum(_ => s.Tick(1, true).Count));
        Assert.False(s.HasSlow);
    }

    [Fact]
    public void Dot_ticks_once_per_second_for_ceil_duration()
    {
        var s = State();
        s.ApplyDot("fire", DotKind.Fire, 3, 2.2);
        int ticks = 0;
        for (int i = 0; i < 100; i++) ticks += s.Tick(0.1, true).Count;
        Assert.Equal(3, ticks);
        Assert.Equal(0, s.DotCount);
    }

    [Fact]
    public void Slows_multiply_across_sources_and_keep_the_best_per_source()
    {
        var s = State();
        s.ApplySlow("a", 0.5, 4);
        s.ApplySlow("a", 0.8, 8);
        s.ApplySlow("b", 0.5, 4);
        Assert.Equal(0.25, s.SpeedMultiplier, 6);
        s.Tick(5, true);
        Assert.Equal(0.5, s.SpeedMultiplier, 6); // a (8 s, kept 0.5) still on, b gone
    }

    [Fact]
    public void Juggernaut_ignores_slow_and_stun()
    {
        var s = State(EnemyAbility.Juggernaut);
        Assert.False(s.ApplySlow("a", 0.5, 5));
        Assert.False(s.ApplyStun(2));
        Assert.Equal(1, s.SpeedMultiplier);
        Assert.True(State().ApplyStun(1));
    }

    [Fact]
    public void Juggernaut_refuses_damage_over_time_and_other_enemies_take_it()
    {
        var j = State(EnemyAbility.Juggernaut);
        Assert.False(j.ApplyDot("fire", DotKind.Fire, 5, 4));
        Assert.Equal(0, j.DotCount);
        Assert.True(State().ApplyDot("fire", DotKind.Fire, 5, 4));
    }

    [Fact]
    public void Undead_after_reviving_is_faster_and_larger()
    {
        var s = State(EnemyAbility.Undead);
        Assert.Equal(1, s.SpeedMultiplier);
        Assert.Equal(1, s.ScaleMultiplier);
        s.TryUndeadRevive();
        Assert.Equal(AbilityTuning.UndeadSpeed, s.SpeedMultiplier, 6);
        Assert.Equal(AbilityTuning.UndeadScale, s.ScaleMultiplier, 6);
    }

    [Fact]
    public void Burn_ticks_melt_armor_up_to_the_enemys_armor()
    {
        var e = new EnemyProfile { MaxHealth = 1000, Armor = 100 };
        double r = EnemyHitEffects.MeltArmor(e, 0, 30);
        Assert.Equal(30, r);
        r = EnemyHitEffects.MeltArmor(e, r, 50);
        Assert.Equal(80, r);
        Assert.Equal(100, EnemyHitEffects.MeltArmor(e, r, 50)); // capped at its armor
        Assert.Equal(80, EnemyHitEffects.MeltArmor(e, 80, -5)); // never negative
    }

    [Fact]
    public void Shielding_absorbs_for_three_seconds_then_recharges_for_the_difficulty_cooldown()
    {
        var s = State(EnemyAbility.Shielding);
        Assert.True(s.AbsorbsHit());
        s.Tick(2.9, true);
        Assert.True(s.AbsorbsHit());
        s.Tick(0.2, true);
        Assert.False(s.AbsorbsHit()); // shield down, recharging
        s.Tick(Difficulties.Easy.ShieldingCooldown, true);
        Assert.True(s.AbsorbsHit());
        Assert.False(State().AbsorbsHit());
    }

    [Fact]
    public void Undead_revives_once()
    {
        var s = State(EnemyAbility.Undead);
        Assert.True(s.TryUndeadRevive());
        Assert.False(s.TryUndeadRevive());
    }

    [Fact]
    public void Rain_empowerment_only_acts_in_the_rain()
    {
        var s = State(EnemyAbility.RainEmpowerment);
        Assert.Equal(1, s.DamageDealtMultiplier);
        s.Raining = true;
        Assert.Equal(AbilityTuning.RainDamage, s.DamageDealtMultiplier);
        Assert.Equal(AbilityTuning.RainArmor, s.ArmorMultiplier);
        Assert.Equal(AbilityTuning.RainSpeed, s.SpeedMultiplier);
        var plain = State();
        plain.Raining = true;
        Assert.Equal(1, plain.DamageDealtMultiplier);
    }

    [Fact]
    public void Avenger_stacks_from_nearby_deaths_only_and_wears_off()
    {
        var s = State(EnemyAbility.Avenger);
        s.OnAllyDied(AbilityTuning.AvengerRadius + 1);
        Assert.Equal(0, s.FuryStacks);
        for (int i = 0; i < 9; i++) s.OnAllyDied(5);
        Assert.Equal(AbilityTuning.AvengerMaxStacks, s.FuryStacks);
        Assert.True(s.DamageDealtMultiplier > 2);
        s.Tick(AbilityTuning.AvengerSeconds + 1, true);
        Assert.Equal(0, s.FuryStacks);
    }

    [Fact]
    public void Armor_multiplier_raises_effective_armor_in_the_defense_math()
    {
        var p = new EnemyProfile { Armor = 500, MaxHealth = 1e6 };
        var hit = new OutgoingHit(1000, DamageKind.Physical, false, 0);
        double plain = EnemyDefense.Apply(p, 0, hit).Damage;
        double rain = EnemyDefense.Apply(p, 0, hit, 1, AbilityTuning.RainArmor).Damage;
        Assert.True(rain < plain);
    }

    [Fact]
    public void Poison_scales_with_enemy_damage_and_needs_the_ability()
    {
        var plain = new EnemyProfile { DamageMult = 1000 };
        var poisonous = new EnemyProfile { DamageMult = 1000, Abilities = new[] { EnemyAbility.Poisonous } };
        Assert.Equal(0, EnemyHitEffects.PoisonPerSecond(plain, 100));
        Assert.Equal(100, EnemyHitEffects.PoisonPerSecond(poisonous, 100), 6);
        Assert.Equal(AbilityTuning.BasherStunSeconds, EnemyHitEffects.StunSeconds(new EnemyProfile { Abilities = new[] { EnemyAbility.Basher } }));
    }
}

public class UniqueItemTests
{
    private static Inventory With(params string[] baseIds)
    {
        var inv = new Inventory();
        inv.Restore(baseIds.Select((id, i) => new KeyValuePair<string, ItemInstance>("equip." + (i == 0 ? "ring1" : "ring2"), new ItemInstance { BaseId = id })));
        return inv;
    }

    [Fact]
    public void Vampire_items_leech_and_stack()
    {
        Assert.Equal(0, UniqueItems.LifeStealFraction(With()));
        Assert.Equal(UniqueItems.BandLifeSteal + UniqueItems.RingLifeSteal, UniqueItems.LifeStealFraction(With(UniqueItems.VampiricBand, UniqueItems.VampireRing)), 9);
        Assert.Equal(UniqueItems.RingKillHeal, UniqueItems.KillHealFraction(With(UniqueItems.VampireRing)));
    }

    [Fact]
    public void Blood_bag_heals_only_when_low()
    {
        var inv = With(UniqueItems.BloodBag);
        Assert.Equal(UniqueItems.BloodBagPerSecond, UniqueItems.BloodBagRegen(inv, 0.3));
        Assert.Equal(0, UniqueItems.BloodBagRegen(inv, 0.8));
    }

    [Fact]
    public void Undying_promise_triggers_on_lethal_damage_then_cools_down()
    {
        var inv = With(UniqueItems.UndyingPromise);
        var u = new UndyingPromiseState();
        Assert.False(u.TryTrigger(inv, 100, 50));
        Assert.True(u.TryTrigger(inv, 100, 100));
        Assert.False(u.TryTrigger(inv, 100, 100));
        u.Tick(UniqueItems.UndyingCooldown);
        Assert.True(u.TryTrigger(inv, 100, 500));
        Assert.False(new UndyingPromiseState().TryTrigger(With(), 100, 500));
    }
}
