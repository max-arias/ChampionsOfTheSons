using ChampionsOfTheSons.Core.Content;
using ChampionsOfTheSons.Core.Rules;

namespace ChampionsOfTheSons.Core.Tests;

/// <summary>COS-84: the rules of the spells that work through the player's real weapon hits and shots.</summary>
public class WeaponSpellTests
{
    private static StatSheet Sheet(params StatModifier[] mods)
    {
        const string extra = """
          { "id": "all_damage", "kind": "multi", "default": 1, "defaultMult": 1 },
          { "id": "spell_increased_dmg", "kind": "multi", "default": 1, "defaultMult": 1 },
          { "id": "spell_dmg_from_int", "kind": "add", "default": 0 },
          { "id": "intelligence", "kind": "add", "default": 0 },
          { "id": "spell_flat_dmg", "kind": "add", "default": 0 },
          { "id": "i_sora_bracers", "kind": "bool", "default": 0 },
          { "id": "perk_multishot_projectile_count", "kind": "add", "default": 1, "integer": true },
          { "id": "perk_multishot_damage_pennalty", "kind": "mult", "default": 0.75 },
          { "id": "spell_black_flame_damage_scaling", "kind": "add", "default": 0.4 },
          { "id": "spell_focus_on_hs", "kind": "add", "default": 0.5 },
          { "id": "spell_focus_on_bs", "kind": "add", "default": 0.15 },
          { "id": "spell_focus_bonus_dmg", "kind": "add", "default": 0 },
          { "id": "spell_parry_dmg_bonus", "kind": "add", "default": 0 },
          { "id": "spell_bia_triple_dmg", "kind": "bool", "default": 0 },
          { "id": "spell_bash_damage_buff", "kind": "add", "default": 0 },
          { "id": "spell_fury_swipes", "kind": "bool", "default": 0 },
        """;
        var stats = TestContent.PlayerStats.Replace("{ \"playerStats\": [", "{ \"playerStats\": [" + extra);
        var s = new StatSheet(TestContent.Create(new() { ["player_stats.json"] = stats }));
        s.SetSource("test", mods);
        return s;
    }

    [Fact]
    public void Multishot_always_adds_arrows_and_each_shot_costs_more_than_proportionally()
    {
        Assert.Equal(1, SpellTuning.MultishotExtras(Sheet()));
        var perk = Sheet(new StatModifier("perk_multishot_projectile_count", EffectOp.Add, 2));
        Assert.Equal(2, SpellTuning.MultishotExtras(perk));
        Assert.True(SpellTuning.MultishotShotCost(perk, 4) > 2 * SpellTuning.MultishotShotCost(perk, 2));
        // The bracers add their projectiles and make a shot cheaper.
        var sora = Sheet(new StatModifier("i_sora_bracers", EffectOp.Enable, 1));
        Assert.Equal(1 + UniqueTuning.SoraExtraProjectiles, SpellTuning.MultishotExtras(sora));
        Assert.True(SpellTuning.MultishotShotCost(sora, 3) < SpellTuning.MultishotShotCost(Sheet(), 3));
    }

    [Fact]
    public void Multishot_falloff_compounds_per_arrow()
    {
        var s = Sheet();
        Assert.Equal(1, SpellTuning.MultishotCopyDamage(s, 0), 6);
        Assert.Equal(SpellTuning.MultishotCopyDamage(s, 1) * SpellTuning.MultishotCopyDamage(s, 1), SpellTuning.MultishotCopyDamage(s, 2), 6);
    }

    [Fact]
    public void Focus_stores_more_for_a_headshot_and_the_next_shot_spends_it_once()
    {
        var s = Sheet();
        Assert.True(SpellTuning.FocusBonus(s, head: true) > SpellTuning.FocusBonus(s, head: false));
        var plus = Sheet(new StatModifier("spell_focus_bonus_dmg", EffectOp.Add, 0.2));
        Assert.Equal(SpellTuning.FocusBonus(s, false) + 0.2, SpellTuning.FocusBonus(plus, false), 6);

        var store = new StoredBonus();
        store.Store(0.5, SpellTuning.FocusStoredSeconds);
        Assert.Equal(0.5, store.Take());
        Assert.Null(store.Take());
    }

    [Fact]
    public void A_stored_bonus_runs_out_and_a_multi_charge_one_serves_each_arrow()
    {
        var store = new StoredBonus();
        store.Store(100, 5, charges: 3);
        Assert.Equal(100, store.Take());
        Assert.Equal(100, store.Take());
        Assert.True(store.Has);
        store.Tick(6);
        Assert.False(store.Has);
        Assert.Null(store.Take());
    }

    [Fact]
    public void Blood_arrow_stores_its_damage_and_the_triple_perk_triples_it()
    {
        var hit = new OutgoingHit(200, DamageKind.Magic, false, 0);
        Assert.Equal(200, SpellTuning.BloodArrowStored(Sheet(), hit), 6);
        Assert.Equal(600, SpellTuning.BloodArrowStored(Sheet(new StatModifier("spell_bia_triple_dmg", EffectOp.Enable, 1)), hit), 6);
    }

    [Fact]
    public void Parry_stores_the_counter_strike_bonus_as_a_multiplier()
    {
        Assert.Equal(1, SpellTuning.ParryStoredMult(Sheet()), 6);
        Assert.Equal(3.5, SpellTuning.ParryStoredMult(Sheet(new StatModifier("spell_parry_dmg_bonus", EffectOp.Add, 3.5))), 6);
    }

    [Fact]
    public void Repeated_hits_on_one_target_build_flat_damage_and_a_new_target_starts_over()
    {
        var s = Sheet(new StatModifier("spell_fury_swipes", EffectOp.Enable, 1));
        var tracker = new RepeatTracker();
        var a = new object();
        var b = new object();
        Assert.Equal(0, tracker.Hit(a));
        Assert.Equal(1, tracker.Hit(a));
        Assert.Equal(2, tracker.Hit(a));
        Assert.Equal(0, tracker.Hit(b));
        Assert.Equal(2 * SpellTuning.FuryMeleePerHit, SpellTuning.FuryDamage(s, melee: true, repeats: 2), 6);
        Assert.True(SpellTuning.FuryDamage(s, melee: true, repeats: 1) > SpellTuning.FuryDamage(s, melee: false, repeats: 1));
        Assert.Equal(0, SpellTuning.FuryDamage(Sheet(), true, 5));
    }

    [Fact]
    public void Black_flame_burn_grows_with_spell_power_and_netherflame_and_melee_always_ignites()
    {
        double plain = SpellTuning.BlackFlameBurn(Sheet(), nether: false);
        Assert.True(SpellTuning.BlackFlameBurn(Sheet(), nether: true) > plain);
        Assert.True(SpellTuning.BlackFlameBurn(Sheet(new StatModifier("spell_flat_dmg", EffectOp.Add, 50)), false) > plain);
        Assert.Equal(1, SpellTuning.BlackFlameIgnite(WeaponClass.Melee));
        Assert.True(SpellTuning.BlackFlameIgnite(WeaponClass.Ranged) < 1);
    }

    [Fact]
    public void Bash_stacks_raise_melee_damage_only_with_the_perk_and_stop_at_the_cap()
    {
        Assert.Equal(1, SpellTuning.BashStackFactor(Sheet(), 4), 6);
        var perk = Sheet(new StatModifier("spell_bash_damage_buff", EffectOp.Add, 1));
        Assert.True(SpellTuning.BashStackFactor(perk, 2) > SpellTuning.BashStackFactor(perk, 1));
        Assert.Equal(SpellTuning.BashStackFactor(perk, SpellTuning.BashMaxStacks), SpellTuning.BashStackFactor(perk, 99), 6);
    }
}
