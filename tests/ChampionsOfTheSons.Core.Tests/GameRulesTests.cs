using ChampionsOfTheSons.Core.Rules;
using Xunit;

namespace ChampionsOfTheSons.Core.Tests;

public class GameRulesTests
{
    [Fact]
    public void Explosions_cannot_one_shot_a_scaled_enemy()
    {
        Assert.Equal(850, GameRules.ExplosionToEnemy(1e7, 0));
        Assert.Equal(40, GameRules.ExplosionToEnemy(1, 0));
        Assert.Equal(850 * 1.5, GameRules.ExplosionToEnemy(5000, 0.5), 6);
    }

    [Fact]
    public void Explosions_hurt_players_only_with_friendly_fire_and_grow_with_difficulty()
    {
        Assert.Equal(0, GameRules.ExplosionToPlayer(5, friendlyFire: false));
        Assert.Equal(85, GameRules.ExplosionToPlayer(0, true));
        Assert.Equal(40000, GameRules.ExplosionToPlayer(10, true));
        for (int t = 1; t <= 10; t++)
            Assert.True(GameRules.ExplosionToPlayer(t, true) > GameRules.ExplosionToPlayer(t - 1, true));
    }

    [Fact]
    public void Only_enemy_attacks_can_be_dodged_or_blocked()
    {
        foreach (PlayerDamageSource s in Enum.GetValues(typeof(PlayerDamageSource)))
            Assert.Equal(s == PlayerDamageSource.Enemy, GameRules.Avoidable(s));
    }

    [Fact]
    public void Environmental_damage_scales_with_max_health()
    {
        Assert.Equal(3 + 500, GameRules.PlayerHit(PlayerDamageSource.Drowning, 10, 5000), 6);
        Assert.Equal((3 + 50) * 0.5, GameRules.PlayerHit(PlayerDamageSource.Fire, 10, 5000, fireTaken: 0.5), 6);
        Assert.Equal(1000, GameRules.PlayerHit(PlayerDamageSource.Thirst, 1, 5000), 6);
        Assert.Equal(17, GameRules.PlayerHit(PlayerDamageSource.Environment, 17, 5000), 6);
        // Cold takes the same share of max vitality as in the game's own 100-health world, never less than the game's amount.
        Assert.Equal(25, GameRules.ColdVitalityLoss(0.5, 5000), 6);
        Assert.Equal(0.5, GameRules.ColdVitalityLoss(0.5, 60), 6);
    }

    [Fact]
    public void Falls_hurt_above_the_threshold_and_kill_when_very_long()
    {
        Assert.Equal(5, GameRules.FallDamage(8, 5, 1000));
        Assert.Equal(5 + 0.008 * 1000 * 10, GameRules.FallDamage(-22, 5, 1000), 6);
        Assert.True(GameRules.FallDamage(45, 5, 1000) >= 1000);
    }

    [Fact]
    public void Meds_and_herbs_heal_a_share_of_max_health()
    {
        Assert.Equal(600, GameRules.Heal(HealKind.Meds, 30, 1000), 6);
        Assert.Equal(60, GameRules.Heal(HealKind.Herb, 5, 1000), 6);
        Assert.Equal(10 + 0.002 * 1000 * 10, GameRules.Heal(HealKind.Other, 10, 1000), 6);
    }

    [Fact]
    public void Worn_armor_soaks_seventy_percent_of_what_it_would()
    {
        Assert.Equal(100 - 0.7 * 60, GameRules.WornArmorRemaining(100, 40), 6);
        Assert.Equal(100, GameRules.WornArmorRemaining(100, 100), 6);
    }

    [Fact]
    public void A_death_pays_unless_the_enemy_drowned()
    {
        Assert.True(GameRules.DeathPays(killedByPlayer: false, drowned: false));
        Assert.False(GameRules.DeathPays(killedByPlayer: false, drowned: true));
        Assert.True(GameRules.DeathPays(killedByPlayer: true, drowned: true));
    }
}
