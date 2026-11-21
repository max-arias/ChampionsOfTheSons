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
      { "id": "spell_shield_max", "kind": "add", "default": 20 },
      { "id": "spell_shield_persistance_lifetime", "kind": "add", "default": 5 },
      { "id": "spell_bia_health_taken", "kind": "add", "default": 0.5 },
      { "id": "spell_bia_health_dm_mult", "kind": "add", "default": 4 },
      { "id": "spell_bia_spell_dm_mult", "kind": "add", "default": 0.1 },
      { "id": "spell_bia_accumulated_damage", "kind": "add", "default": 0 },
      { "id": "spell_bia_crit", "kind": "bool", "default": 0 }
    ] }
    """;

    private readonly ContentDb _db = TestContent.Create(new() { ["spells.json"] = Spells, ["player_stats.json"] = Stats });

    [Fact]
    public void Black_hole_catch_radius_grows_from_zero_to_the_full_radius()
    {
        Assert.Equal(0, SpellTuning.BlackHoleCatchRadius(15, 0), 6);
        Assert.Equal(7.5, SpellTuning.BlackHoleCatchRadius(15, SpellTuning.BlackHoleCatchSeconds / 2), 6);
        Assert.Equal(15, SpellTuning.BlackHoleCatchRadius(15, 50), 6);
    }

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
    public void Frenzy_stacks_on_attacks_up_to_the_cap_refresh_and_drop_after_their_seconds()
    {
        var stats = new StatSheet(_db);
        var frenzy = new FrenzyState();
        for (int i = 0; i < 10; i++) frenzy.OnAttack(stats);
        Assert.Equal(3, frenzy.Stacks);
        stats.SetSource("spell.frenzy", SpellTuning.Frenzy(stats, frenzy.Stacks));
        Assert.Equal(1.3, stats.Get("all_damage"), 6);
        Assert.Equal(1.06, stats.Get("attack_speed"), 6);
        Assert.False(frenzy.Tick(SpellTuning.FrenzyStackSeconds - 1));
        frenzy.OnAttack(stats); // an attack refreshes the time
        Assert.False(frenzy.Tick(SpellTuning.FrenzyStackSeconds - 1));
        Assert.True(frenzy.Tick(2));
        Assert.Equal(0, frenzy.Stacks);
    }

    [Fact]
    public void Casting_frenzy_spends_every_stack()
    {
        var stats = new StatSheet(_db);
        var frenzy = new FrenzyState();
        frenzy.OnAttack(stats);
        frenzy.OnAttack(stats);
        Assert.Equal(2, frenzy.Spend());
        Assert.False(frenzy.Active);
        Assert.Equal(0, frenzy.Spend());
        Assert.Equal(2 * SpellTuning.FrenzyBurstShare * 200, SpellTuning.FrenzyBurst(2, 200), 6);
    }

    [Fact]
    public void Shield_fills_while_channeled_caps_absorbs_and_expires_after_release()
    {
        var stats = new StatSheet(_db);
        var shield = new SpellShield { Channeling = true };
        shield.Tick(1, 200, stats); // 10 % of 200 per second
        Assert.Equal(20, shield.Amount, 6);
        shield.Tick(5, 200, stats); // capped at 20 % of 200
        Assert.Equal(40, shield.Amount, 6);
        Assert.Equal(15, shield.Absorb(55), 6);
        Assert.Equal(0, shield.Amount, 6);
        shield.Tick(1, 200, stats);
        shield.Channeling = false;
        shield.Tick(4.9, 200, stats);
        Assert.True(shield.Amount > 0);
        shield.Tick(0.2, 200, stats);
        Assert.Equal(0, shield.Amount);
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
