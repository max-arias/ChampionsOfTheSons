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
            Assert.True(EnemyScaler.MaxHealth(l + 1, 100, 1) >= EnemyScaler.MaxHealth(l, 100, 1));
            Assert.True(EnemyScaler.DamageMultiplier(l + 1) >= EnemyScaler.DamageMultiplier(l), $"damage drops at {l}");
        }
    }

    [Fact]
    public void Low_level_enemies_never_drop_below_vanilla_health_or_damage()
    {
        for (int l = 1; l <= 10; l++)
            Assert.True(EnemyScaler.MaxHealth(l, 100, 1) >= 100);
        var weak = new EnemyProfile { Level = 1, DamageMult = EnemyScaler.DamageMultiplier(1) };
        Assert.Equal(30, EnemyScaler.DamageToPlayer(weak, 30), 6);
    }

    [Theory]
    [InlineData(1, GameMode.Normal, 0)]
    [InlineData(20, GameMode.Hard, 0)]
    [InlineData(80, GameMode.HardSurvival, 7)]
    [InlineData(1, GameMode.Peaceful, 0)]
    public void Enemy_level_is_the_party_level_plus_the_game_difficulty_and_extra_levels(int party, GameMode mode, int extra)
    {
        var rng = new Rng(1);
        var game = GameDifficulty.For(mode);
        var rules = new WorldRules { GameDifficulty = game, ExtraEnemyLevels = extra };
        int center = party + Cannibal.LevelExtra + game.LevelOffset + extra;
        var levels = Enumerable.Range(0, 200).Select(_ => EnemyScaler.RollLevel(Cannibal, rules, party, rng)).ToList();
        Assert.All(levels, l => Assert.InRange(l, Math.Max(1, center - 1), Math.Max(1, center + 1)));
        if (center > 2) Assert.Equal(3, levels.Distinct().Count());
        Assert.All(Enumerable.Range(0, 50).Select(_ => EnemyScaler.RollLevel(Cannibal, rules with { PartyLevelOverride = 200 }, party, rng)),
            l => Assert.True(l >= 200 + Cannibal.LevelExtra + game.LevelOffset + extra - 1));
    }

    [Fact]
    public void Harder_game_modes_add_levels_and_elites()
    {
        var ladder = new[] { GameMode.Peaceful, GameMode.Normal, GameMode.Hard, GameMode.HardSurvival }.Select(m => GameDifficulty.For(m)).ToList();
        for (int i = 1; i < ladder.Count; i++)
        {
            Assert.True(ladder[i].LevelOffset > ladder[i - 1].LevelOffset);
            Assert.True(ladder[i].EliteChance > ladder[i - 1].EliteChance);
        }
        Assert.Equal(GameDifficulty.For(GameMode.Normal).LevelOffset, GameDifficulty.For(GameMode.Creative).LevelOffset);
    }

    [Fact]
    public void Custom_game_offset_comes_from_the_enemy_sliders()
    {
        Assert.Equal(GameDifficulty.Normal.LevelOffset, GameDifficulty.For(GameMode.Custom).LevelOffset);
        var allHigh = GameDifficulty.For(GameMode.Custom, GameSlider.High, GameSlider.High, GameSlider.High, GameSlider.High);
        Assert.Equal(GameDifficulty.For(GameMode.HardSurvival).LevelOffset, allHigh.LevelOffset);
        var allLow = GameDifficulty.For(GameMode.Custom, GameSlider.Low, GameSlider.Low, GameSlider.Low, GameSlider.Low);
        Assert.True(allLow.LevelOffset < 0);
        Assert.True(allLow.EliteChance < GameDifficulty.Normal.EliteChance);
        var oneHigh = GameDifficulty.For(GameMode.Custom, damage: GameSlider.High);
        Assert.InRange(oneHigh.LevelOffset, 1, allHigh.LevelOffset - 1);
        // The elite chance stays a sensible probability at any offset.
        Assert.InRange(new GameDifficulty(GameMode.Custom, 500).EliteChance, 0.05, 0.25);
        Assert.InRange(new GameDifficulty(GameMode.Custom, -500).EliteChance, 0.05, 0.25);
    }

    [Fact]
    public void Party_level_is_the_rounded_up_average_of_players_near_the_enemy()
    {
        var players = new List<(int, float)> { (10, 5), (21, 100), (90, EnemyLevels.PartyRange + 1) };
        Assert.Equal(16, EnemyLevels.PartyLevel(players));
        Assert.Equal(40, EnemyLevels.PartyLevel(new List<(int, float)> { (30, 900), (50, 1000) }));
        Assert.Equal(7, EnemyLevels.PartyLevel(new List<(int, float)> { (7, 0) }));
        Assert.Equal(1, EnemyLevels.PartyLevel(new List<(int, float)>()));
    }

    [Fact]
    public void An_enemy_band_follows_its_level()
    {
        Assert.Equal(Difficulties.Easy, Difficulties.ForLevel(1));
        Assert.Equal(Difficulties.Easy, Difficulties.ForLevel(-5));
        for (int i = 1; i < Difficulties.All.Count; i++)
        {
            var d = Difficulties.All[i];
            Assert.Equal(d, Difficulties.ForLevel(d.BaseLevelMin));
            Assert.Equal(Difficulties.All[i - 1], Difficulties.ForLevel(d.BaseLevelMin - 1));
        }
        Assert.Equal(Difficulties.Hell, Difficulties.ForLevel(5000));
        var rolled = EnemyScaler.Roll(Cannibal, 100, new WorldRules { PartyLevelOverride = 35, AllowElites = false }, 1, new Rng(9));
        Assert.Equal(Difficulties.ForLevel(rolled.Level), new EnemyCombatState(rolled).Difficulty);
    }

    [Theory]
    [InlineData(EnemyTier.Normal)]
    [InlineData(EnemyTier.Elite)]
    [InlineData(EnemyTier.Miniboss)]
    [InlineData(EnemyTier.Boss)]
    public void A_forced_tier_overrides_the_roll(EnemyTier tier)
    {
        var rng = new Rng(3);
        var rules = new WorldRules { PartyLevelOverride = 300, ForcedTier = tier };
        for (int i = 0; i < 50; i++)
        {
            var p = EnemyScaler.Roll(Cannibal, 100, rules, 1, rng);
            Assert.Equal(tier, p.Tier);
            Assert.Equal(tier == EnemyTier.Normal, p.Abilities.Count == 0);
        }
    }

    [Fact]
    public void Hell_level_enemies_are_all_elite_unless_elites_are_off()
    {
        var rng = new Rng(2);
        var hell = new WorldRules { PartyLevelOverride = Difficulties.Hell.BaseLevelMin };
        for (int i = 0; i < 100; i++)
            Assert.NotEqual(EnemyTier.Normal, EnemyScaler.Roll(Cannibal, 100, hell, 1, rng).Tier);
        var noElites = hell with { AllowElites = false };
        for (int i = 0; i < 100; i++)
            Assert.Equal(EnemyTier.Normal, EnemyScaler.Roll(Cannibal, 100, noElites, 1, rng).Tier);
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
        var rules = new WorldRules { PartyLevelOverride = Difficulties.All[6].BaseLevelMin };
        var rolls = Enumerable.Range(0, 3000).Select(_ => EnemyScaler.Roll(Boss, 100, rules, 1, rng)).ToList();
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
