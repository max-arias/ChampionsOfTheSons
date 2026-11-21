using ChampionsOfTheSons.Core.Content;
using ChampionsOfTheSons.Core.Rules;

namespace ChampionsOfTheSons.Core.Tests;

public class SpellRulesTests
{
    private const string Spells = """
    { "spells": [
      { "id": "spell.hold", "name": "Hold", "levelReq": 1, "energyCost": 50, "cooldown": 0, "channeled": true },
      { "id": "spell.aura", "name": "Aura", "levelReq": 1, "energyCost": 25, "cooldown": 2.5, "toggle": true },
      { "id": "spell.shot", "name": "Shot", "levelReq": 1, "energyCost": 10, "cooldown": 5 }
    ] }
    """;

    private const string Stats = """
    { "playerStats": [
      { "id": "max_health", "kind": "add", "default": 0 },
      { "id": "i_infinity_loop", "kind": "bool", "default": 0 },
      { "id": "all_damage", "kind": "multi", "default": 1, "defaultMult": 1 },
      { "id": "attack_speed", "kind": "multi", "default": 1, "defaultMult": 1 },
      { "id": "all_damage_taken", "kind": "mult", "default": 1 },
      { "id": "spell_increased_dmg", "kind": "multi", "default": 1, "defaultMult": 1 },
      { "id": "spell_dmg_from_int", "kind": "add", "default": 0 },
      { "id": "intelligence", "kind": "add", "default": 0 },
      { "id": "spell_flat_dmg", "kind": "add", "default": 0 },
      { "id": "crit_chance", "kind": "add", "default": 0 },
      { "id": "crit_damage", "kind": "add", "default": 0 },
      { "id": "cooldown_rate", "kind": "mult", "default": 1 },
      { "id": "cooldown", "kind": "mult", "default": 1 },
      { "id": "spell_cost", "kind": "mult", "default": 1 },
      { "id": "spell_cost_energy_cost", "kind": "mult", "default": 1 },
      { "id": "spell_frenzy_max_stacks", "kind": "add", "default": 3 },
      { "id": "spell_frenzy_dmg", "kind": "add", "default": 0.1 },
      { "id": "spell_frenzy_atk_speed", "kind": "add", "default": 0.02 },
      { "id": "spell_shield_per_second", "kind": "add", "default": 10 },
      { "id": "spell_shield_max", "kind": "add", "default": 40 },
      { "id": "spell_shield_persistance_lifetime", "kind": "add", "default": 5 },
      { "id": "ranged_flat_dmg", "kind": "add", "default": 0 },
      { "id": "spell_snap_damage_scaling", "kind": "add", "default": 6.5 },
      { "id": "spell_magic_arrow_damage_scaling", "kind": "add", "default": 4.2 },
      { "id": "spell_magic_arrow_crit", "kind": "bool", "default": 0 },
      { "id": "health_recovery_per_second", "kind": "add", "default": 0 },
      { "id": "health_per_sec_rate", "kind": "add", "default": 0 },
      { "id": "all_recovery_mult", "kind": "mult", "default": 1 },
      { "id": "max_health_mult", "kind": "mult", "default": 1 },
      { "id": "vitality", "kind": "add", "default": 0 },
      { "id": "max_health_from_vit", "kind": "add", "default": 0 },
      { "id": "spell_bia_health_taken", "kind": "add", "default": 0.5 },
      { "id": "spell_bia_health_dm_mult", "kind": "add", "default": 4 },
      { "id": "spell_bia_spell_dm_mult", "kind": "add", "default": 0.1 },
      { "id": "spell_bia_accumulated_damage", "kind": "add", "default": 0 },
      { "id": "spell_bia_crit", "kind": "bool", "default": 0 }
    ] }
    """;

    private readonly ContentDb _db = TestContent.Create(new() { ["spells.json"] = Spells, ["player_stats.json"] = Stats });

    [Fact]
    public void Toggle_costs_upkeep_per_second_and_switching_off_starts_the_cooldown()
    {
        var book = new SpellBook(_db);
        var stats = new StatSheet(_db);
        book.Restore(new[] { "spell.aura" });
        Assert.Equal(CastResult.Ok, book.TryCast(0, 100, stats, out _));
        Assert.True(book.IsActive(0));
        var tick = book.SustainTick(2, 100, stats);
        Assert.Equal(50, tick.Spent, 6);
        Assert.Empty(tick.Stopped);
        Assert.Equal(CastResult.Stopped, book.TryCast(0, 100, stats, out _));
        Assert.False(book.IsActive(0));
        Assert.Equal(CastResult.OnCooldown, book.TryCast(0, 100, stats, out _));
        book.Tick(2.5, stats);
        Assert.Equal(CastResult.Ok, book.TryCast(0, 100, stats, out _));
    }

    [Fact]
    public void A_running_spell_switches_off_when_energy_runs_out_and_needs_one_second_to_start()
    {
        var book = new SpellBook(_db);
        var stats = new StatSheet(_db);
        book.Restore(new[] { "spell.hold" });
        Assert.Equal(CastResult.NotEnoughEnergy, book.TryCast(0, 49, stats, out _));
        Assert.False(book.IsActive(0));
        Assert.Equal(CastResult.Ok, book.TryCast(0, 50, stats, out _));
        var tick = book.SustainTick(1, 30, stats);
        Assert.Equal(0, tick.Spent);
        Assert.Equal(new[] { 0 }, tick.Stopped);
        Assert.False(book.IsActive(0));
    }

    [Fact]
    public void Re_equipping_a_running_spell_stops_it_and_normal_spells_ignore_upkeep()
    {
        var book = new SpellBook(_db);
        var stats = new StatSheet(_db);
        book.Restore(new[] { "spell.aura", "spell.shot" });
        book.TryCast(0, 100, stats, out _);
        book.TryCast(1, 100, stats, out _);
        Assert.False(book.IsActive(1));
        Assert.Equal(25, book.SustainTick(1, 100, stats).Spent, 6);
        book.Equip(2, "spell.aura");
        Assert.False(book.IsActive(0));
        Assert.Equal(0, book.SustainTick(1, 100, stats).Spent);
    }

    [Fact]
    public void Buffs_change_stats_while_they_last_and_refresh_instead_of_stacking()
    {
        var stats = new StatSheet(_db);
        var buffs = new SpellBuffs(stats);
        var mods = new[] { new StatModifier("all_damage", EffectOp.Mult, 1.5) };
        buffs.Apply("war_cry", 10, mods);
        buffs.Apply("war_cry", 4, mods);
        Assert.Equal(1.5, stats.Get("all_damage"), 6);
        Assert.Equal(10, buffs.Remaining("war_cry"), 6);
        Assert.Empty(buffs.Tick(9));
        Assert.Equal(new[] { "war_cry" }, buffs.Tick(1.5));
        Assert.Equal(1, stats.Get("all_damage"), 6);
        Assert.False(buffs.Has("war_cry"));
    }

    [Fact]
    public void Frenzy_stacks_on_hits_up_to_the_cap_and_drops_when_it_ends()
    {
        var stats = new StatSheet(_db);
        var frenzy = new FrenzyState();
        Assert.False(frenzy.OnHit(stats)); // not cast yet
        frenzy.Cast();
        for (int i = 0; i < 10; i++) frenzy.OnHit(stats);
        Assert.Equal(3, frenzy.Stacks);
        var mods = SpellTuning.Frenzy(stats, frenzy.Stacks);
        stats.SetSource("spell.frenzy", mods);
        Assert.Equal(1.3, stats.Get("all_damage"), 6);
        Assert.Equal(1.06, stats.Get("attack_speed"), 6);
        Assert.False(frenzy.Tick(10));
        Assert.True(frenzy.Tick(10.1));
        Assert.Equal(0, frenzy.Stacks);
    }

    [Fact]
    public void Shield_gains_by_spell_flat_caps_absorbs_holds_then_fades()
    {
        var stats = new StatSheet(_db);
        var shield = new SpellShield { Channeling = true };
        shield.Tick(1, stats); // 10 per second
        Assert.Equal(10, shield.Amount, 6);
        stats.SetSource("flat", new[] { new StatModifier("spell_flat_dmg", EffectOp.Add, 70) }); // +2 per second, cap 40 + 70
        Assert.Equal(12, SpellShield.GainPerSecond(stats), 6);
        Assert.Equal(110, SpellShield.Cap(stats), 6);
        shield.Tick(100, stats);
        Assert.Equal(110, shield.Amount, 6);
        Assert.Equal(15, shield.Absorb(125), 6);
        Assert.Equal(0, shield.Amount, 6);
        shield.Tick(1, stats);
        shield.Channeling = false;
        double held = shield.Amount;
        shield.Tick(4.9, stats); // inside the 5 s lifetime: unchanged
        Assert.Equal(held, shield.Amount, 6);
        shield.Tick(0.2 + SpellTuning.ShieldDecaySeconds / 2, stats); // lifetime over, half of the fade
        Assert.InRange(shield.Amount, held * 0.4, held * 0.6);
        shield.Tick(SpellTuning.ShieldDecaySeconds, stats);
        Assert.Equal(0, shield.Amount, 6);
    }

    [Fact]
    public void Healing_dome_heals_by_regen_recovery_and_spell_flat()
    {
        var stats = new StatSheet(_db);
        double baseRegen = Derived.HealthRegenPerSecond(stats, 0);
        Assert.Equal(baseRegen * baseRegen * 10 + 43.5, SpellTuning.HealingDomeHeal(stats), 6);
        stats.SetSource("t", new[]
        {
            new StatModifier("spell_flat_dmg", EffectOp.Add, 100),
            new StatModifier("health_recovery_per_second", EffectOp.Add, 2),
            new StatModifier("all_recovery_mult", EffectOp.Mult, 1.5),
        });
        double regen = Derived.HealthRegenPerSecond(stats, 0);
        Assert.Equal((regen * regen * 10 + 43.5) * 1.5 + 10, SpellTuning.HealingDomeHeal(stats), 6);
    }

    [Fact]
    public void Black_hole_catch_radius_grows_over_its_first_seconds()
    {
        Assert.Equal(0, SpellTuning.BlackHoleCatchRadius(15, 0), 6);
        Assert.Equal(7.5, SpellTuning.BlackHoleCatchRadius(15, SpellTuning.BlackHoleCatchSeconds / 2), 6);
        Assert.Equal(15, SpellTuning.BlackHoleCatchRadius(15, 10), 6);
    }

    [Fact]
    public void Magic_arrow_and_snap_freeze_scale_with_spell_flat_and_ranged_flat()
    {
        var stats = new StatSheet(_db);
        Assert.Equal(110, SpellTuning.MagicArrowHit(stats, new Rng(1)).Amount, 6); // (55 + 0) x 2
        Assert.Equal(23, SpellTuning.SnapFreezeHit(stats, new Rng(1)).Amount, 6);
        stats.SetSource("t", new[]
        {
            new StatModifier("spell_flat_dmg", EffectOp.Add, 10),
            new StatModifier("ranged_flat_dmg", EffectOp.Add, 20),
        });
        Assert.Equal((55 + 4.2 * 10 + 20 / 2.0) * 2, SpellTuning.MagicArrowHit(stats, new Rng(1)).Amount, 6);
        Assert.Equal(23 + 6.5 * 10, SpellTuning.SnapFreezeHit(stats, new Rng(1)).Amount, 6);
    }

    [Fact]
    public void Parry_negates_exactly_one_hit_inside_its_window()
    {
        var parry = new ParryState();
        Assert.False(parry.TryParry());
        parry.Cast();
        parry.Tick(SpellTuning.ParryWindow - 0.1);
        Assert.True(parry.TryParry());
        Assert.False(parry.TryParry());
        parry.Cast();
        parry.Tick(SpellTuning.ParryWindow + 0.1);
        Assert.False(parry.TryParry());
    }

    [Fact]
    public void Blood_arrow_trades_health_for_damage_and_never_kills_the_caster()
    {
        var stats = new StatSheet(_db);
        var (cost, hit) = SpellTuning.BloodArrow(stats, 100, new Rng(1));
        Assert.Equal(50, cost, 6);
        Assert.Equal(200, hit.Amount, 6);
        Assert.Equal(DamageKind.Magic, hit.Kind);
        var (lowCost, _) = SpellTuning.BloodArrow(stats, 1, new Rng(1));
        Assert.Equal(0, lowCost);
        stats.SetSource("perk", new[] { new StatModifier("spell_flat_dmg", EffectOp.Add, 100) });
        Assert.Equal(210, SpellTuning.BloodArrow(stats, 100, new Rng(1)).Hit.Amount, 6);
    }
}
