using ChampionsOfTheSons.Core.Content;
using ChampionsOfTheSons.Core.Rules;

namespace ChampionsOfTheSons.Core.Tests;

public class WeaponTypeTests
{
    private const string Stats = """
    { "playerStats": [ { "id": "i_hammer_stun", "kind": "bool", "default": 0 }, { "id": "max_health", "kind": "add", "default": 0 } ] }
    """;

    private const string ItemStats = """
    { "itemStats": [ { "id": "stat.max_life", "name": "Maximum Life", "min": 1, "max": 2, "combine": "sum", "rarity": 0,
      "effects": [ { "stat": "max_health", "op": "add" } ] } ] }
    """;

    private const string Items = """
    { "items": [
      { "id": "item.sword", "name": "Sword", "type": "weapon", "weaponModel": "long_sword", "statPools": [ ["stat.max_life"] ] },
      { "id": "item.bow", "name": "Bow", "type": "weapon", "weaponModel": "greatbow", "statPools": [ ["stat.max_life"] ] },
      { "id": "item.club", "name": "Club", "type": "weapon", "statPools": [ ["stat.max_life"] ] }
    ] }
    """;

    private const string Types = """
    { "weaponTypes": [
      { "id": "long_sword", "base": "melee", "damage": 20, "swingSpeed": 0.8 },
      { "id": "hammer", "base": "melee", "stunSlow": 0.25, "stunSlowSeconds": 0.4 },
      { "id": "greatbow", "base": "bow", "rangedFlatDamage": 10, "rangedDamageMult": 2, "drawSpeed": 0.5 },
      { "id": "polearm", "base": "spear", "canThrow": false }
    ] }
    """;

    private static ContentDb Db(string? items = null) => ContentDb.Load(new Dictionary<string, string>
    {
        ["player_stats.json"] = Stats, ["item_stats.json"] = ItemStats, ["items.json"] = items ?? Items, ["weapon_types.json"] = Types,
    });

    private static Inventory Wielding(ContentDb db, string baseId)
    {
        var inv = new Inventory();
        inv.AddToBag(new ItemInstance { BaseId = baseId, Level = 1 });
        Assert.True(inv.Equip(Inventory.Bag(0), db));
        return inv;
    }

    [Fact]
    public void The_weapon_slot_item_names_the_type()
    {
        var db = Db();
        Assert.Empty(db.Errors);
        Assert.Equal("long_sword", WeaponTypes.Equipped(Wielding(db, "item.sword"), db)?.Id);
        Assert.Equal("greatbow", WeaponTypes.Equipped(Wielding(db, "item.bow"), db)?.Id);
        Assert.Null(WeaponTypes.Equipped(Wielding(db, "item.club"), db));
        Assert.Null(WeaponTypes.Equipped(new Inventory(), db));
    }

    [Fact]
    public void Melee_type_damage_replaces_the_game_weapon_and_bow_type_adds_then_multiplies()
    {
        var db = Db();
        var sword = db.WeaponTypes["long_sword"];
        var bow = db.WeaponTypes["greatbow"];
        Assert.Equal(20, WeaponTypes.MeleeBaseDamage(sword, 14));
        Assert.Equal(14, WeaponTypes.MeleeBaseDamage(db.WeaponTypes["hammer"], 14));
        Assert.Equal(14, WeaponTypes.MeleeBaseDamage(null, 14));
        Assert.Equal((30 + 10) * 2, WeaponTypes.RangedBaseDamage(bow, 30));
        Assert.Equal(30, WeaponTypes.RangedBaseDamage(sword, 30));
        Assert.Equal(1, WeaponTypes.SwingSpeedFactor(bow));
    }

    [Fact]
    public void Hammer_slows_on_every_hit_only_with_the_stun_stat()
    {
        var db = Db();
        var hammer = db.WeaponTypes["hammer"];
        var sheet = new StatSheet(db);
        Assert.Null(WeaponTypes.HitSlow(hammer, sheet));
        sheet.SetSource("gear", new[] { new StatModifier(CombatStatIds.HammerStun, EffectOp.Enable, 1) });
        Assert.Equal((0.25, 0.4), WeaponTypes.HitSlow(hammer, sheet));
        Assert.Null(WeaponTypes.HitSlow(db.WeaponTypes["long_sword"], sheet));
    }

    [Fact]
    public void Unknown_weapon_type_on_an_item_is_a_content_error()
    {
        var db = Db("""{ "items": [ { "id": "item.x", "name": "X", "type": "weapon", "weaponModel": "nunchaku", "statPools": [ ["stat.max_life"] ] } ] }""");
        Assert.Contains(db.Errors, e => e.Contains("nunchaku"));
    }

    [Fact]
    public void Only_spear_types_can_forbid_the_throw_and_only_bow_types_change_the_draw()
    {
        var db = Db();
        Assert.False(WeaponTypes.CanThrow(db.WeaponTypes["polearm"]));
        Assert.True(WeaponTypes.CanThrow(null));
        Assert.True(WeaponTypes.CanThrow(new Content.WeaponTypeDef { Base = WeaponBase.Melee, CanThrow = false }));
        Assert.Equal(2, WeaponTypes.ShotDelayFactor(db.WeaponTypes["greatbow"]));
        Assert.Equal(1, WeaponTypes.ShotDelayFactor(new Content.WeaponTypeDef { Base = WeaponBase.Melee, DrawSpeed = 0.5 }));
    }

    [Fact]
    public void A_weapon_alone_kills_an_on_level_enemy_in_the_same_hits_at_every_level()
    {
        var sword = Db().WeaponTypes["long_sword"]; // the reference damage
        foreach (int level in new[] { 1, 10, 31, 60, 120 })
        {
            double hits = EnemyScaler.MaxHealth(level + WeaponTypes.OnLevelExtra, 100, 1) / WeaponTypes.WeaponDamage(sword, level, Rarity.Legendary);
            Assert.InRange(hits, WeaponTypes.LegendaryHitsToKill - 0.01, WeaponTypes.LegendaryHitsToKill + 0.01);
            double strip = EnemyScaler.MeanArmor(level + WeaponTypes.OnLevelExtra) / WeaponTypes.WeaponPierce(level, Rarity.Legendary);
            Assert.InRange(strip, WeaponTypes.LegendaryHitsToStrip - 0.01, WeaponTypes.LegendaryHitsToStrip + 0.01);
        }
    }

    [Fact]
    public void Weapon_damage_grows_with_rarity_level_and_type_damage()
    {
        var db = Db();
        var sword = db.WeaponTypes["long_sword"];
        double Damage(Rarity r, int level) => WeaponTypes.WeaponDamage(sword, level, r);
        Assert.True(Damage(Rarity.Common, 30) < Damage(Rarity.Magic, 30));
        Assert.True(Damage(Rarity.Magic, 30) < Damage(Rarity.Rare, 30));
        Assert.True(Damage(Rarity.Rare, 30) < Damage(Rarity.Legendary, 30));
        Assert.True(Damage(Rarity.Legendary, 30) < Damage(Rarity.Legendary, 31));
        var heavy = new WeaponTypeDef { Base = WeaponBase.Melee, Damage = 40 };
        Assert.Equal(2 * Damage(Rarity.Rare, 30), WeaponTypes.WeaponDamage(heavy, 30, Rarity.Rare), 6);
        Assert.True(WeaponTypes.WeaponPierce(30, Rarity.Common) < WeaponTypes.WeaponPierce(30, Rarity.Legendary));
    }

    [Fact]
    public void A_bound_weapon_keeps_its_key_when_equipping_swaps_slots()
    {
        var db = Db();
        var inv = new Inventory();
        var bar = new WeaponBar();
        inv.Swapped += bar.OnSwapped;
        inv.Emptied += bar.OnEmptied;
        inv.AddToBag(new ItemInstance { BaseId = "item.sword", Level = 1 });
        inv.AddToBag(new ItemInstance { BaseId = "item.bow", Level = 1 });
        inv.AddToBag(new ItemInstance { BaseId = "item.ring_like", Level = 1 });
        Assert.True(bar.Bind(1, "bag.0", inv, db));
        Assert.True(bar.Bind(2, "bag.1", inv, db));
        Assert.False(bar.Bind(3, "bag.2", inv, db)); // not a weapon
        Assert.False(bar.Bind(WeaponBar.Keys, "bag.0", inv, db));

        Assert.True(inv.Move("bag.0", WeaponTypes.WeaponSlot, db)); // sword equipped
        Assert.Equal(WeaponTypes.WeaponSlot, bar.SlotOf(1));
        Assert.True(inv.Move("bag.1", WeaponTypes.WeaponSlot, db)); // bow over sword: they swap
        Assert.Equal("bag.1", bar.SlotOf(1));
        Assert.Equal("item.sword", inv.Get(bar.SlotOf(1)!)!.BaseId);
        Assert.Equal("item.bow", inv.Get(bar.SlotOf(2)!)!.BaseId);

        Assert.True(bar.Bind(2, "bag.1", inv, db)); // rebinding moves the weapon to the new key
        Assert.Null(bar.SlotOf(1));
        inv.Remove("bag.1");
        Assert.Null(bar.SlotOf(2));
    }

    [Fact]
    public void Weapon_bar_bindings_survive_a_save_and_drop_what_no_longer_fits()
    {
        var db = Db();
        var inv = new Inventory();
        inv.AddToBag(new ItemInstance { BaseId = "item.sword", Level = 1 });
        var save = new Saving.PlayerSave { WeaponBar = { [0] = "bag.0", [4] = "bag.7", [12] = "bag.0" } };
        var loaded = Saving.SaveCodec.ReadPlayer(Saving.SaveCodec.Write(save));
        var bar = new WeaponBar();
        bar.Restore(loaded.WeaponBar, inv, db);
        Assert.Equal("bag.0", bar.SlotOf(0));
        Assert.Single(bar.Bindings);
    }
}
