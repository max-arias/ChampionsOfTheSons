using ChampionsOfTheSons.Core.Content;
using ChampionsOfTheSons.Core.Rules;

namespace ChampionsOfTheSons.Core.Tests;

/// <summary>COS-85/86/87/88 rules: ranged and melee perks, Smolder Mark, Infinity, spell items, slowdown buy rule, per-piece sets, Wide Reach.</summary>
public class PerkRulesTests
{
    private static string Stat(string id, string kind, double def) =>
        $"{{ \"id\": \"{id}\", \"kind\": \"{kind}\", \"default\": {def.ToString(System.Globalization.CultureInfo.InvariantCulture)} }}";

    private static readonly string Stats = "{ \"playerStats\": [" + string.Join(",", new[]
    {
        Stat("all_damage", "mult", 1), Stat("ranged_increased_dmg", "mult", 1), Stat("melee_increased_dmg", "mult", 1), Stat("spell_increased_dmg", "mult", 1),
        Stat("crit_chance", "add", 0), Stat("crit_damage", "add", 0.5), Stat("head_shot_damage", "add", 6), Stat("ranged_flat_dmg", "add", 0),
        Stat("ranged_armor_piercing", "add", 0), Stat("melee_armor_piercing", "add", 0), Stat("all_armor_piercing", "add", 0),
        Stat("projectile_speed", "mult", 1), Stat("projectile_size", "mult", 1), Stat("attack_speed", "mult", 1), Stat("spell_flat_dmg", "add", 0),
        Stat("fire_damage", "add", 0), Stat("thorns", "add", 0), Stat("thorns_dmg_mult", "mult", 1), Stat("max_health", "add", 0),
        Stat("cooldown", "mult", 1), Stat("cooldown_rate", "mult", 1), Stat("spell_cost", "mult", 1), Stat("spell_cost_energy_cost", "mult", 1),
        Stat("perk_bow_damage_mult", "mult", 1), Stat("perk_crossbow_damage_mult", "mult", 1), Stat("perk_bullet_damage_mult", "mult", 1),
        Stat("perk_thrown_spear_damage_mult", "mult", 1), Stat("perk_bullet_crit_chance", "add", 0.1), Stat("perk_thrown_spear_crit_chance", "add", 0.05),
        Stat("projectile_damage_per_distance", "add", 0), Stat("perk_projectile_damage_increased_by_size", "bool", 0),
        Stat("perk_projectile_damage_increased_by_speed", "bool", 0), Stat("perk_thrown_spear_extra_armor_reduction", "bool", 0),
        Stat("perk_true_aim", "bool", 0), Stat("perk_true_aim_upgrade", "bool", 0), Stat("perk_dance_of_firegod", "bool", 0),
        Stat("perk_dance_of_firegod_atk_cap", "bool", 0), Stat("perk_fire_dmg_increase_on_hit", "bool", 0), Stat("perk_infinity_magic", "bool", 0),
        Stat("spell_firebolt_energy_cost", "add", 5), Stat("spell_healing_dome_cooldown_rate", "add", 0), Stat("spell_healing_dome_spell_cost_reduction", "add", 1),
        Stat("all_damage_taken", "mult", 1),
    }) + "] }";

    private const string Spells = """
    { "spells": [ { "id": "spell.firebolt", "name": "F", "levelReq": 1, "energyCost": 45, "cooldown": 20 },
                  { "id": "spell.healing_dome", "name": "D", "levelReq": 1, "energyCost": 80, "cooldown": 60 },
                  { "id": "spell.golden_skin", "name": "G", "levelReq": 1, "energyCost": 65, "cooldown": 160 },
                  { "id": "spell.war_cry", "name": "W", "levelReq": 1, "energyCost": 85, "cooldown": 180 } ] }
    """;

    private const string Perks = """
    { "perks": [
      { "id": "perk.power_slowdown", "name": "P", "category": "melee_offense", "cost": 0, "stackable": true, "canBuyManual": true },
      { "id": "perk.golden_cooldown", "name": "G", "category": "magic_offense", "cost": 0 }
    ] }
    """;

    private const string Items = """
    { "items": [
      { "id": "item.ring", "name": "Ring", "type": "ring", "rarity": 2, "set": "set.storm", "statPools": [ ["stat.max_life"] ] },
      { "id": "item.boots", "name": "Boots", "type": "boot", "rarity": 2, "set": "set.storm", "statPools": [ ["stat.max_life"] ] },
      { "id": "item.dust", "name": "Dust", "type": "material", "rarity": 1, "statPools": [] }
    ] }
    """;

    private const string Sets = """
    { "sets": [ { "id": "set.storm", "name": "Storm", "bonuses": [
      { "pieces": 1, "perPiece": true, "effects": [ { "stat": "all_damage", "op": "add", "value": 0.5 } ] },
      { "pieces": 2, "effects": [ { "stat": "crit_chance", "op": "add", "value": 0.1 } ] } ] } ] }
    """;

    private static readonly ContentDb Db = TestContent.Create(new()
    {
        ["player_stats.json"] = Stats, ["spells.json"] = Spells, ["perks.json"] = Perks, ["items.json"] = Items, ["sets.json"] = Sets,
    });

    private static StatSheet Sheet(params StatModifier[] mods)
    {
        var s = new StatSheet(Db);
        s.SetSource("test", mods);
        return s;
    }

    private static StatModifier On(string stat) => new(stat, EffectOp.Enable, 1);

    private static readonly RangedShot Bow = new(RangedKind.Bow, 0, false);

    // ---------------------------------------------------------------- ranged

    [Theory]
    [InlineData("Crossbow", RangedKind.Crossbow)]
    [InlineData("CompoundBow", RangedKind.Bow)]
    [InlineData("CraftedBow", RangedKind.Bow)]
    [InlineData("Pistol", RangedKind.Bullet)]
    [InlineData("Shotgun", RangedKind.Bullet)]
    [InlineData("CraftedSpear", RangedKind.ThrownSpear)]
    [InlineData("Slingshot", RangedKind.Slingshot)]
    [InlineData("Rock", RangedKind.Other)]
    [InlineData(null, RangedKind.Other)]
    public void Weapon_kind_comes_from_the_item_name(string? name, RangedKind kind) => Assert.Equal(kind, RangedRules.KindOf(name));

    [Fact]
    public void A_weapon_multiplier_only_changes_its_own_weapon()
    {
        var s = Sheet(new StatModifier("perk_crossbow_damage_mult", EffectOp.Mult, 2));
        double bow = RangedRules.Hit(s, 50, Bow, new Rng(1)).Hit.Amount;
        double crossbow = RangedRules.Hit(s, 50, Bow with { Kind = RangedKind.Crossbow }, new Rng(1)).Hit.Amount;
        Assert.Equal(50, bow, 6);
        Assert.Equal(100, crossbow, 6);
    }

    [Fact]
    public void Bullets_and_spears_roll_headshots_other_weapons_do_not()
    {
        var sure = Sheet(new StatModifier("perk_bullet_crit_chance", EffectOp.Add, 1), new StatModifier("perk_thrown_spear_crit_chance", EffectOp.Add, 1));
        Assert.True(RangedRules.Hit(sure, 10, Bow with { Kind = RangedKind.Bullet }, new Rng(1)).Headshot);
        Assert.True(RangedRules.Hit(sure, 10, Bow with { Kind = RangedKind.ThrownSpear }, new Rng(1)).Headshot);
        Assert.False(RangedRules.Hit(sure, 10, Bow, new Rng(1)).Headshot);
        Assert.Equal(60, RangedRules.Hit(sure, 10, Bow with { Kind = RangedKind.Bullet }, new Rng(1)).Hit.Amount, 6);
    }

    [Fact]
    public void Distance_size_and_speed_perks()
    {
        var far = Sheet(new StatModifier("projectile_damage_per_distance", EffectOp.Add, 0.02));
        Assert.Equal(100 * 1.5, RangedRules.Hit(far, 100, Bow with { Distance = 25 }, new Rng(1)).Hit.Amount, 6);

        var big = Sheet(On("perk_projectile_damage_increased_by_size"), new StatModifier("projectile_size", EffectOp.Mult, 2));
        Assert.Equal(200, RangedRules.Hit(big, 100, Bow, new Rng(1)).Hit.Amount, 6);

        // Crit: 1 + 0.5 crit damage + (speed 3 - 1) = x3.5.
        var fast = Sheet(On("perk_projectile_damage_increased_by_speed"), new StatModifier("projectile_speed", EffectOp.Mult, 3), new StatModifier("crit_chance", EffectOp.Add, 1));
        Assert.Equal(350, RangedRules.Hit(fast, 100, Bow, new Rng(1)).Hit.Amount, 6);
    }

    [Fact]
    public void Spear_armor_strip_pierces_three_times_ranged_plus_melee()
    {
        var s = Sheet(On("perk_thrown_spear_extra_armor_reduction"), new StatModifier("ranged_armor_piercing", EffectOp.Add, 10), new StatModifier("melee_armor_piercing", EffectOp.Add, 5));
        Assert.Equal(35, RangedRules.Hit(s, 1, Bow with { Kind = RangedKind.ThrownSpear }, new Rng(1)).Hit.ArmorPierce, 6);
        Assert.Equal(10, RangedRules.Hit(s, 1, Bow, new Rng(1)).Hit.ArmorPierce, 6);
    }

    [Fact]
    public void True_aim_needs_an_arrow_headshot_from_far_enough()
    {
        var s = Sheet(On("perk_true_aim"), On("perk_true_aim_upgrade"));
        Assert.Equal((1.0, 0), RangedRules.TrueAim(s, Bow with { Distance = 30 }, headshot: false));
        Assert.Equal((1.0, 0), RangedRules.TrueAim(s, Bow with { Distance = 10 }, headshot: true));
        Assert.Equal((RangedRules.TrueAimMult, RangedRules.TrueAimExtraHits), RangedRules.TrueAim(s, Bow with { Distance = 30 }, headshot: true));
        Assert.Equal((RangedRules.TrueAimUpgradeMult, RangedRules.TrueAimUpgradeExtraHits), RangedRules.TrueAim(s, Bow with { Distance = 40 }, headshot: true));
        Assert.Equal((1.0, 0), RangedRules.TrueAim(s, Bow with { Distance = 40, Seeking = true }, headshot: true));
        Assert.Equal((1.0, 0), RangedRules.TrueAim(s, Bow with { Kind = RangedKind.Bullet, Distance = 40 }, headshot: true));
    }

    // ---------------------------------------------------------------- melee, thorns, fire

    [Fact]
    public void Fire_dance_scales_melee_with_attack_speed_while_black_flame_burns()
    {
        var s = Sheet(On("perk_dance_of_firegod"), new StatModifier("attack_speed", EffectOp.Mult, 1.5));
        Assert.Equal(1 + 1.5 * 1.1, MeleeRules.FireDanceMultiplier(s, blackFlameOn: true), 6);
        Assert.Equal(1, MeleeRules.FireDanceMultiplier(s, blackFlameOn: false));
        Assert.Equal(1.5, MeleeRules.SwingSpeed(s, true), 6);
        var locked = Sheet(On("perk_dance_of_firegod_atk_cap"), new StatModifier("attack_speed", EffectOp.Mult, 1.5));
        Assert.Equal(RangedRules.FireDanceLockedSpeed, MeleeRules.SwingSpeed(locked, true));
        Assert.Equal(1.5, MeleeRules.SwingSpeed(locked, false), 6);
    }

    [Fact]
    public void Thorns_also_take_the_melee_and_all_damage_multipliers()
    {
        var s = Sheet(new StatModifier("thorns", EffectOp.Add, 10), new StatModifier("melee_increased_dmg", EffectOp.Mult, 2), new StatModifier("all_damage", EffectOp.Mult, 1.5));
        Assert.Equal(30, PlayerHitEffects.Thorns(s), 6);
    }

    [Fact]
    public void Smolder_mark_raises_fire_ticks_only_while_it_lasts()
    {
        var profile = new EnemyProfile { MaxHealth = 1000 };
        var enemy = new EnemyCombatState(profile, Difficulties.Easy);
        enemy.ApplyDot("burn", DotKind.Fire, 10, 5);
        enemy.ApplyDot("bleed", DotKind.Bleed, 10, 5);
        enemy.ApplyFireMark(0.5, 2);
        var first = enemy.Tick(1, true);
        Assert.Equal(15, first.Single(t => t.Kind == DotKind.Fire).Damage, 6);
        Assert.Equal(10, first.Single(t => t.Kind == DotKind.Bleed).Damage, 6);
        enemy.Tick(1, true);
        Assert.Equal(10, enemy.Tick(1, true).Single(t => t.Kind == DotKind.Fire).Damage, 6);
    }

    [Fact]
    public void Smolder_mark_bonus_follows_spell_power()
    {
        var s = Sheet(On("perk_fire_dmg_increase_on_hit"), new StatModifier("spell_flat_dmg", EffectOp.Add, 30));
        Assert.Equal(11 * 0.35, PerkRules.SmolderMarkBonus(s, ranged: false), 6);
        Assert.Equal(11 * 0.3, PerkRules.SmolderMarkBonus(s, ranged: true), 6);
        Assert.Equal(0, PerkRules.SmolderMarkBonus(Sheet(), false));
    }

    // ---------------------------------------------------------------- spells

    private static SpellBook Book() => new PlayerCharacter(Db).Spells;

    [Fact]
    public void Infinity_shortens_every_remaining_cooldown_on_cast()
    {
        var pc = new PlayerCharacter(Db);
        pc.Spells.Equip(0, "spell.firebolt");
        pc.Spells.Equip(1, "spell.war_cry");
        pc.Spells.Tick(1e6, pc.Stats);
        pc.Spells.TryCast(1, 1e6, pc.Stats, out _);
        pc.Spells.Tick(18, pc.Stats); // war cry: 10% done
        pc.Stats.SetSource("test", new[] { On("perk_infinity_magic") });
        pc.Spells.TryCast(0, 1e6, pc.Stats, out _);
        Assert.Equal(1 - 0.9 * PerkRules.InfinityFactor, pc.Spells.CooldownProgress(1), 6);
    }

    [Fact]
    public void Gilded_pace_cuts_only_golden_skin()
    {
        var pc = new PlayerCharacter(Db);
        pc.Perks.Restore(new Dictionary<string, int> { ["perk.golden_cooldown"] = 1 }, Array.Empty<string>());
        Assert.Equal(160 - PerkRules.GoldenSkinCut, pc.Spells.CooldownOf("spell.golden_skin", pc.Stats), 6);
        Assert.Equal(180, pc.Spells.CooldownOf("spell.war_cry", pc.Stats), 6);
    }

    [Fact]
    public void Spell_items_change_one_spell_cost_and_cooldown_rate()
    {
        var pc = new PlayerCharacter(Db);
        pc.Stats.SetSource("test", new[]
        {
            new StatModifier("spell_firebolt_energy_cost", EffectOp.Add, 5),
            new StatModifier("spell_healing_dome_spell_cost_reduction", EffectOp.Add, -0.4),
            new StatModifier("spell_healing_dome_cooldown_rate", EffectOp.Add, 1),
        });
        Assert.Equal(50, pc.Spells.CostOf("spell.firebolt", pc.Stats).Energy, 6);
        Assert.Equal(48, pc.Spells.CostOf("spell.healing_dome", pc.Stats).Energy, 6);
        Assert.Equal(85, pc.Spells.CostOf("spell.war_cry", pc.Stats).Energy, 6);
        pc.Spells.Equip(0, "spell.healing_dome");
        pc.Spells.Equip(1, "spell.war_cry");
        pc.Spells.Tick(30, pc.Stats);
        Assert.Equal(1, pc.Spells.CooldownProgress(0), 6);
        Assert.Equal(30.0 / 180, pc.Spells.CooldownProgress(1), 6);
    }

    // ---------------------------------------------------------------- perks, sets, Wide Reach

    [Fact]
    public void Slowdown_perks_are_buyable_only_above_110_percent()
    {
        var pc = new PlayerCharacter(Db);
        Assert.Equal(BuyResult.NotBuyable, pc.CanBuyPerk("perk.power_slowdown"));
        pc.Stats.SetSource("test", new[] { new StatModifier("attack_speed", EffectOp.Mult, 1.2) });
        Assert.Equal(BuyResult.Ok, pc.BuyPerk("perk.power_slowdown"));
    }

    [Fact]
    public void Per_piece_set_bonuses_stack_with_each_piece()
    {
        var one = ItemEffects.SetBonuses(new[] { new ItemInstance { BaseId = "item.ring" } }, Db);
        var two = ItemEffects.SetBonuses(new[] { new ItemInstance { BaseId = "item.ring" }, new ItemInstance { BaseId = "item.boots" } }, Db);
        Assert.Single(one);
        Assert.Equal(2, two.Count(m => m.Stat == "all_damage"));
        Assert.Single(two, m => m.Stat == "crit_chance");
    }

    [Fact]
    public void Wide_reach_takes_materials_and_drops_at_least_as_rare_as_the_worst_equipped_item()
    {
        var inv = new Inventory();
        Assert.Equal(0, WideReachRules.LowestEquippedRarity(inv));
        foreach (var slot in LootRoller.EquipSlots) inv.Restore(inv.Slots.Append(new(slot, new ItemInstance { BaseId = "item.ring", Rarity = Rarity.Rare })).ToList());
        int lowest = WideReachRules.LowestEquippedRarity(inv);
        Assert.Equal((int)Rarity.Rare, lowest);
        Assert.True(WideReachRules.Takes(new ItemInstance { BaseId = "item.dust", Rarity = Rarity.Common }, Db, lowest));
        Assert.False(WideReachRules.Takes(new ItemInstance { BaseId = "item.boots", Rarity = Rarity.Common }, Db, lowest));
        Assert.True(WideReachRules.Takes(new ItemInstance { BaseId = "item.boots", Rarity = Rarity.Rare }, Db, lowest));
    }

}
