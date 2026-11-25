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

    private static ItemInstance Weapon(string baseId, float life = 0) =>
        new() { BaseId = baseId, Level = 1, Stats = life > 0 ? new[] { new RolledStat("stat.max_life", life) } : Array.Empty<RolledStat>() };

    [Fact]
    public void A_weapon_item_names_its_type()
    {
        var db = Db();
        Assert.Empty(db.Errors);
        Assert.Equal("long_sword", WeaponTypes.TypeOf(Weapon("item.sword"), db)?.Id);
        Assert.Equal("greatbow", WeaponTypes.TypeOf(Weapon("item.bow"), db)?.Id);
        Assert.Null(WeaponTypes.TypeOf(Weapon("item.club"), db));
        Assert.Null(WeaponTypes.TypeOf(null, db));
    }

    [Fact]
    public void Weapons_have_no_equipment_slot_but_stay_gear()
    {
        var db = Db();
        var inv = new Inventory();
        inv.AddToBag(Weapon("item.sword"));
        Assert.False(inv.Equip("bag.0", db));
        Assert.False(inv.Move("bag.0", WeaponTypes.LegacyWeaponSlot, db));
        Assert.True(LootRoller.IsEquippable(db.Items["item.sword"]));
    }

    [Fact]
    public void A_weapon_counts_only_while_drawn()
    {
        var db = Db();
        var pc = new PlayerCharacter(db);
        double bare = pc.Stats.Get("max_health");
        pc.Inventory.AddToBag(Weapon("item.sword", 50));
        Assert.False(pc.Select("bag.1"));
        Assert.True(pc.Select("bag.0"));
        Assert.Equal(bare, pc.Stats.Get("max_health"));
        pc.SetDrawn(true);
        Assert.Equal(bare + 50, pc.Stats.Get("max_health"));
        pc.SetDrawn(false);
        Assert.Equal(bare, pc.Stats.Get("max_health"));

        pc.SetDrawn(true);
        pc.Inventory.AddToBag(Weapon("item.bow"));
        Assert.True(pc.Inventory.Move("bag.0", "bag.5", db)); // the selection follows its item
        Assert.Equal("bag.5", pc.SelectedSlot);
        Assert.Equal(bare + 50, pc.Stats.Get("max_health"));
        pc.Inventory.Remove("bag.5"); // dropped, sold, destroyed: nothing selected, nothing drawn
        Assert.Null(pc.SelectedSlot);
        Assert.False(pc.Drawn);
        Assert.Equal(bare, pc.Stats.Get("max_health"));
    }

    [Fact]
    public void A_weapon_in_the_old_weapon_slot_moves_into_the_bag_and_keeps_its_key()
    {
        var db = Db();
        var save = new Saving.PlayerSave();
        save.Slots["bag.0"] = Saving.SavedItem.From(Weapon("item.bow"));
        save.Slots[WeaponTypes.LegacyWeaponSlot] = Saving.SavedItem.From(Weapon("item.sword"));
        save.WeaponBar[2] = WeaponTypes.LegacyWeaponSlot;
        save.WeaponBar[0] = "bag.0";
        var pc = new PlayerCharacter(db);
        pc.Load(Saving.SaveCodec.ReadPlayer(Saving.SaveCodec.Write(save)));
        Assert.Null(pc.Inventory.Get(WeaponTypes.LegacyWeaponSlot));
        Assert.Equal("bag.1", pc.WeaponBar.SlotOf(2));
        Assert.Equal("item.sword", pc.Inventory.Get("bag.1")!.BaseId);
        Assert.Equal("bag.0", pc.WeaponBar.SlotOf(0));
        Assert.Empty(pc.LoadOverflow);
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
    public void Quickbar_keys_take_bag_weapons_and_follow_them()
    {
        var db = Db();
        var inv = new Inventory();
        var bar = new WeaponBar();
        inv.Swapped += bar.OnSwapped;
        inv.Emptied += bar.OnEmptied;
        inv.AddToBag(Weapon("item.sword"));
        inv.AddToBag(Weapon("item.bow"));
        inv.AddToBag(new ItemInstance { BaseId = "item.ring_like", Level = 1 });
        Assert.True(bar.Bind(1, "bag.0", inv, db));
        Assert.False(bar.Bind(3, "bag.2", inv, db)); // not a weapon
        Assert.False(bar.Bind(6, "bag.1", inv, db)); // key 7: the game's item hotkey, not a weapon key
        Assert.Equal(0, bar.BindFirstFree("bag.1", inv, db));
        Assert.Equal(0, bar.BindFirstFree("bag.1", inv, db)); // already bound: keeps its key

        Assert.True(inv.Move("bag.0", "bag.9", db)); // the binding follows its item
        Assert.Equal("bag.9", bar.SlotOf(1));
        Assert.True(bar.SwapKeys(0, 1));
        Assert.Equal("bag.9", bar.SlotOf(0));
        Assert.Equal("bag.1", bar.SlotOf(1));
        bar.Remap(new Dictionary<string, string> { ["bag.9"] = "bag.0", ["bag.1"] = "bag.3" }); // sorted bag
        Assert.Equal("bag.0", bar.SlotOf(0));
        Assert.Equal("bag.3", bar.SlotOf(1));
        bar.Remap(new Dictionary<string, string> { ["bag.0"] = "bag.9", ["bag.3"] = "bag.1" }); // back to where the items are

        Assert.Equal(-1, bar.Toggle("bag.9", inv, db)); // right-click a bound weapon: off the bar
        Assert.Null(bar.SlotOf(0));
        inv.Remove("bag.1");
        Assert.Null(bar.SlotOf(1));
        bar.WeaponKeys = new[] { 0 };
        Assert.Equal(0, bar.Toggle("bag.9", inv, db));
        inv.AddToBag(Weapon("item.bow"));
        Assert.Equal(-1, bar.BindFirstFree("bag.0", inv, db)); // bar full
    }

    [Fact]
    public void Weapon_bar_bindings_survive_a_save_and_drop_what_no_longer_fits()
    {
        var db = Db();
        var inv = new Inventory();
        inv.AddToBag(new ItemInstance { BaseId = "item.sword", Level = 1 });
        var save = new Saving.PlayerSave { WeaponBar = { [0] = "bag.0", [4] = "bag.7", [8] = "bag.0" } };
        var loaded = Saving.SaveCodec.ReadPlayer(Saving.SaveCodec.Write(save));
        var bar = new WeaponBar();
        bar.Restore(loaded.WeaponBar, inv, db);
        Assert.Equal("bag.0", bar.SlotOf(0));
        Assert.Single(bar.Bindings);
    }
}
