using ChampionsOfTheSons.Core.Content;
using ChampionsOfTheSons.Core.Rules;

namespace ChampionsOfTheSons.Core.Tests;

public class WeaponTypeTests
{
    private const string Stats = """
    { "playerStats": [ { "id": "i_hammer_stun", "kind": "bool", "default": 0 } ] }
    """;

    private const string Items = """
    { "items": [
      { "id": "item.sword", "name": "Sword", "type": "weapon", "weaponModel": "long_sword", "statPools": [] },
      { "id": "item.bow", "name": "Bow", "type": "weapon", "weaponModel": "greatbow", "statPools": [] },
      { "id": "item.club", "name": "Club", "type": "weapon", "statPools": [] }
    ] }
    """;

    private const string Types = """
    { "weaponTypes": [
      { "id": "long_sword", "base": "melee", "damage": 20, "swingSpeed": 0.8 },
      { "id": "hammer", "base": "melee", "stunSlow": 0.25, "stunSlowSeconds": 0.4 },
      { "id": "greatbow", "base": "bow", "rangedFlatDamage": 10, "rangedDamageMult": 2 }
    ] }
    """;

    private static ContentDb Db(string? items = null) => ContentDb.Load(new Dictionary<string, string>
    {
        ["player_stats.json"] = Stats, ["item_stats.json"] = """{ "itemStats": [] }""", ["items.json"] = items ?? Items, ["weapon_types.json"] = Types,
    });

    private static Inventory Wielding(ContentDb db, string baseId)
    {
        var inv = new Inventory();
        inv.AddToBag(new ItemInstance { BaseId = baseId, Level = 1 });
        Assert.True(inv.Equip(Inventory.Bag(0), db));
        return inv;
    }

    [Fact]
    public void Type_applies_only_while_a_fitting_game_weapon_is_held()
    {
        var db = Db();
        Assert.Empty(db.Errors);
        var inv = Wielding(db, "item.sword");
        Assert.Equal("long_sword", WeaponTypes.Active(inv, db, HeldKind.Melee)?.Id);
        Assert.Null(WeaponTypes.Active(inv, db, HeldKind.Bow));
        Assert.Null(WeaponTypes.Active(inv, db, HeldKind.None));
        Assert.Equal("greatbow", WeaponTypes.Active(Wielding(db, "item.bow"), db, HeldKind.Bow)?.Id);
        Assert.Null(WeaponTypes.Active(Wielding(db, "item.club"), db, HeldKind.Melee));
        Assert.Null(WeaponTypes.Active(new Inventory(), db, HeldKind.Melee));
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
        var db = Db("""{ "items": [ { "id": "item.x", "name": "X", "type": "weapon", "weaponModel": "nunchaku", "statPools": [] } ] }""");
        Assert.Contains(db.Errors, e => e.Contains("nunchaku"));
    }
}
