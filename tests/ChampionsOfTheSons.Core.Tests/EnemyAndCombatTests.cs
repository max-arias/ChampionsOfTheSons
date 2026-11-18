using ChampionsOfTheSons.Core.Rules;

namespace ChampionsOfTheSons.Core.Tests;

public class EnemyScalerTests
{
    private static readonly EnemyKind Cannibal = new() { Id = "enemy.cannibal", LevelExtra = 1 };
    private static readonly EnemyKind Boss = new() { Id = "enemy.boss", LevelExtra = 6, BossType = true };

    [Fact]
    public void Scaling_never_decreases_with_level()
    {
        for (int l = 1; l < 400; l++)
        {
            Assert.True(EnemyScaler.MaxHealth(l + 1, 100, 1) > EnemyScaler.MaxHealth(l, 100, 1));
            Assert.True(EnemyScaler.DamageMultiplier(l + 1) >= EnemyScaler.DamageMultiplier(l), $"damage drops at {l}");
        }
    }

    [Fact]
    public void Higher_difficulty_gives_higher_levels()
    {
        var rng = new Rng(1);
        int previousMax = 0;
        foreach (var d in Difficulties.All)
        {
            var levels = Enumerable.Range(0, 50).Select(_ => EnemyScaler.RollLevel(Cannibal, new WorldRules { Difficulty = d }, rng)).ToList();
            Assert.True(levels.Min() >= previousMax, $"{d.Name} overlaps the tier below");
            Assert.All(levels, l => Assert.InRange(l, d.BaseLevelMin + 1, d.BaseLevelMax + 1));
            previousMax = levels.Max();
        }
    }

    [Fact]
    public void Hell_makes_every_enemy_elite_unless_elites_are_off()
    {
        var rng = new Rng(2);
        var hell = new WorldRules { Difficulty = Difficulties.Hell };
        for (int i = 0; i < 100; i++)
            Assert.NotEqual(EnemyTier.Normal, EnemyScaler.Roll(Cannibal, 100, hell, rng).Tier);
        var noElites = hell with { AllowElites = false };
        for (int i = 0; i < 100; i++)
            Assert.Equal(EnemyTier.Normal, EnemyScaler.Roll(Cannibal, 100, noElites, rng).Tier);
    }

    [Fact]
    public void Ability_picks_follow_the_exclusivity_and_gating_rules()
    {
        var rng = new Rng(3);
        for (int i = 0; i < 2000; i++)
        {
            var d = Difficulties.All[i % 11];
            var picks = EnemyScaler.PickAbilities(10, Cannibal, d, rng);
            Assert.Equal(picks.Count, picks.Distinct().Count());
            Assert.True(picks.Count(a => a is EnemyAbility.Steadfast or EnemyAbility.EliteSteadfast or EnemyAbility.BossSteadfast) <= 1);
            Assert.False(picks.Contains(EnemyAbility.Tiny) && picks.Contains(EnemyAbility.Gargantuan));
            Assert.DoesNotContain(EnemyAbility.Undead, picks);
            if (d.Index < 3) Assert.DoesNotContain(EnemyAbility.Meteor, picks);
        }
    }

    [Fact]
    public void Bosses_are_reachable_and_tougher_than_minibosses()
    {
        var rng = new Rng(4);
        var rules = new WorldRules { Difficulty = Difficulties.All[6] };
        var rolls = Enumerable.Range(0, 3000).Select(_ => EnemyScaler.Roll(Boss, 100, rules, rng)).ToList();
        Assert.Contains(rolls, r => r.Tier == EnemyTier.Boss);
        var boss = rolls.First(r => r.Tier == EnemyTier.Boss);
        Assert.Equal(0.01, boss.HitCap);
        Assert.Contains(EnemyAbility.BossSteadfast, boss.Abilities);
    }

    [Fact]
    public void Bounty_grows_with_health_level_and_ability_count()
    {
        Assert.True(EnemyScaler.Bounty(2000, 10, 0, 1) > EnemyScaler.Bounty(1000, 10, 0, 1));
        Assert.True(EnemyScaler.Bounty(1000, 40, 0, 1) > EnemyScaler.Bounty(1000, 10, 0, 1));
        Assert.Equal(3 * EnemyScaler.Bounty(1000, 10, 0, 1), EnemyScaler.Bounty(1000, 10, 3, 1), 6);
    }
}

public class CombatTests
{
    private static EnemyProfile Enemy(double hp, double armor, params EnemyAbility[] abilities) =>
        new() { KindId = "enemy.test", Level = 10, MaxHealth = hp, Armor = armor, Abilities = abilities };

    [Fact]
    public void Armor_reduction_is_squared_and_never_negative()
    {
        Assert.Equal(0, ArmorMath.Reduction(-50));
        Assert.Equal(0.25, ArmorMath.Reduction(500), 9);
        Assert.True(ArmorMath.Reduction(1e9) < 1);
    }

    [Fact]
    public void Physical_magic_and_pure_hits_respect_armor_differently()
    {
        var hit = new OutgoingHit(1000, DamageKind.Physical, false, 0);
        Assert.Equal(750, new EnemyHealthState(Enemy(1e6, 500)).TakeHit(hit), 6);
        Assert.Equal(1000 * (1 - 0.25 / 1.5), new EnemyHealthState(Enemy(1e6, 500)).TakeHit(hit with { Kind = DamageKind.Magic }), 6);
        Assert.Equal(1000, new EnemyHealthState(Enemy(1e6, 500)).TakeHit(hit with { Kind = DamageKind.Pure }), 6);
    }

    [Fact]
    public void Armor_pierce_strips_armor_for_later_hits_but_not_below_zero()
    {
        var e = new EnemyHealthState(Enemy(1e6, 500));
        e.TakeHit(new OutgoingHit(1, DamageKind.Physical, false, 300));
        Assert.Equal(200, e.EffectiveArmor);
        e.TakeHit(new OutgoingHit(1, DamageKind.Physical, false, 1000));
        Assert.Equal(0, e.EffectiveArmor);
    }

    [Fact]
    public void Steadfast_caps_each_hit()
    {
        var e = new EnemyHealthState(Enemy(10_000, 0, EnemyAbility.EliteSteadfast));
        Assert.Equal(400, e.TakeHit(new OutgoingHit(1e9, DamageKind.Pure, false, 0)), 6);
    }

    [Fact]
    public void Undead_revives_once_at_half_health()
    {
        var e = new EnemyHealthState(Enemy(1000, 0, EnemyAbility.Undead));
        e.TakeHit(new OutgoingHit(5000, DamageKind.Pure, false, 0));
        Assert.False(e.Dead);
        Assert.Equal(500, e.Health, 6);
        e.TakeHit(new OutgoingHit(5000, DamageKind.Pure, false, 0));
        Assert.True(e.Dead);
    }

    [Fact]
    public void Player_weapon_hit_uses_flat_bonus_multipliers_and_crit()
    {
        var s = new StatSheet(TestContent.Create(new() { ["player_stats.json"] = CombatStats }));
        s.SetSource("gear", new[]
        {
            new StatModifier(StatIds.MeleeFlatDmg, Content.EffectOp.Add, 10),
            new StatModifier(StatIds.MeleeIncreasedDmg, Content.EffectOp.Add, 1), // ×2
            new StatModifier(StatIds.CritChance, Content.EffectOp.Add, 1), // always crit
        });
        var hit = PlayerAttack.Weapon(s, WeaponClass.Melee, 40, new Rng(5));
        Assert.True(hit.Crit);
        // (40 + 10) × (1 + 0.5 crit damage) × 2
        Assert.Equal(150, hit.Amount, 6);
    }

    [Fact]
    public void Player_takes_less_damage_with_armor_and_block_and_can_dodge()
    {
        var s = new StatSheet(TestContent.Create(new() { ["player_stats.json"] = CombatStats }));
        var rng = new Rng(6);
        Assert.Equal(100, PlayerDefense.Incoming(s, 100, DamageKind.Physical, rng).Damage, 6);
        s.SetSource("gear", new[] { new StatModifier(StatIds.Armor, Content.EffectOp.Add, 500), new StatModifier(StatIds.Block, Content.EffectOp.Add, 5) });
        Assert.Equal(70, PlayerDefense.Incoming(s, 100, DamageKind.Physical, rng).Damage, 6);
        s.SetSource("dodge", new[] { new StatModifier(StatIds.GetHitChance, Content.EffectOp.Mult, 0) });
        Assert.True(PlayerDefense.Incoming(s, 100, DamageKind.Physical, rng).Dodged);
        Assert.False(PlayerDefense.Incoming(s, 100, DamageKind.Magic, rng).Dodged);
    }

    private const string CombatStats = """
    { "playerStats": [
      { "id": "strength", "kind": "add", "default": 1 },
      { "id": "all_damage", "kind": "multi", "default": 1, "defaultMult": 1 },
      { "id": "melee_increased_dmg", "kind": "multi", "default": 1, "defaultMult": 1 },
      { "id": "melee_dmg_from_str", "kind": "add", "default": 0 },
      { "id": "melee_flat_dmg", "kind": "multi", "default": 0, "defaultMult": 1 },
      { "id": "heavy_attack_dmg", "kind": "add", "default": 1 },
      { "id": "crit_chance", "kind": "add", "default": 0 },
      { "id": "crit_damage", "kind": "add", "default": 0.5 },
      { "id": "all_armor_piercing", "kind": "add", "default": 0 },
      { "id": "melee_armor_piercing", "kind": "add", "default": 0 },
      { "id": "armor", "kind": "add", "default": 0 },
      { "id": "block", "kind": "add", "default": 0 },
      { "id": "all_damage_taken", "kind": "mult", "default": 1 },
      { "id": "magic_damage_taken", "kind": "mult", "default": 1 },
      { "id": "get_hit_chance", "kind": "mult", "default": 1 },
      { "id": "max_health", "kind": "add", "default": 0 },
      { "id": "i_infinity_loop", "kind": "bool", "default": 0 }
    ] }
    """;
}
