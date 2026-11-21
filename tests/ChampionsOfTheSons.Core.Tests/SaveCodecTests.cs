using ChampionsOfTheSons.Core;
using ChampionsOfTheSons.Core.Saving;

namespace ChampionsOfTheSons.Core.Tests;

public class SaveCodecTests
{
    private static PlayerSave SamplePlayer()
    {
        var save = new PlayerSave { Xp = 123_456_789, Level = 42, BonusPerkPoints = 3 };
        save.Slots["bag.0"] = SavedItem.From(new ItemInstance
        {
            BaseId = "item.some_amulet",
            Level = 40,
            Rarity = Rarity.Rare,
            Stats = new[] { new RolledStat("stat.max_health", 55.5f) },
        });
        save.Slots["equip.ring1"] = new SavedItem { BaseId = "item.removed_in_a_later_version", Level = 3 };
        save.Perks["perk.some_perk"] = 2;
        save.UnlockedSpells.Add("spell.blink");
        save.SpellLoadout.AddRange(new[] { "spell.blink", null });
        return save;
    }

    [Fact]
    public void Player_save_round_trips()
    {
        var original = SamplePlayer();
        var loaded = SaveCodec.ReadPlayer(SaveCodec.Write(original));

        Assert.Equal(original.Xp, loaded.Xp);
        Assert.Equal(original.Level, loaded.Level);
        Assert.Equal(original.BonusPerkPoints, loaded.BonusPerkPoints);
        Assert.Equal(original.Perks, loaded.Perks);
        Assert.Equal(original.UnlockedSpells, loaded.UnlockedSpells);
        Assert.Equal(original.SpellLoadout, loaded.SpellLoadout);
        Assert.Equal(original.Slots.Keys.OrderBy(k => k), loaded.Slots.Keys.OrderBy(k => k));
        Assert.Equal(original.Slots["bag.0"].ToItem(), loaded.Slots["bag.0"].ToItem());
    }

    [Fact]
    public void Items_with_unknown_content_ids_are_kept()
    {
        var loaded = SaveCodec.ReadPlayer(SaveCodec.Write(SamplePlayer()));
        Assert.Equal("item.removed_in_a_later_version", loaded.Slots["equip.ring1"].BaseId);
    }

    [Fact]
    public void World_save_round_trips()
    {
        var loaded = SaveCodec.ReadWorld(SaveCodec.Write(new WorldSave { ExtraEnemyLevels = 4, FriendlyFire = true, LootLevel = "Closest" }));
        Assert.Equal(4, loaded.ExtraEnemyLevels);
        Assert.True(loaded.FriendlyFire);
        Assert.Equal("Closest", loaded.LootLevel);
    }

    [Fact]
    public void Version_3_worlds_get_the_default_loot_level()
    {
        var world = SaveCodec.ReadWorld("{\"version\":3,\"extraEnemyLevels\":2}");
        Assert.Equal(WorldSave.CurrentVersion, world.Version);
        Assert.Equal("Highest", world.LootLevel);
        Assert.Equal(2, world.ExtraEnemyLevels);
    }

    [Fact]
    public void Version_2_worlds_drop_their_difficulty_tier_and_keep_the_host_settings()
    {
        var world = SaveCodec.ReadWorld("{\"version\":2,\"difficulty\":\"difficulty.master\",\"extraEnemyLevels\":7,\"xp\":2,\"friendlyFire\":true}");
        Assert.Equal(WorldSave.CurrentVersion, world.Version);
        Assert.Equal(7, world.ExtraEnemyLevels);
        Assert.Equal(2, world.Xp);
        Assert.True(world.FriendlyFire);
        Assert.DoesNotContain("difficulty", SaveCodec.Write(world));
    }

    [Fact]
    public void Save_from_a_newer_mod_version_is_refused()
    {
        var json = SaveCodec.Write(SamplePlayer()).Replace($"\"version\":{PlayerSave.CurrentVersion}", "\"version\":999");
        var e = Assert.Throws<SaveVersionException>(() => SaveCodec.ReadPlayer(json));
        Assert.Equal(999, e.Found);
    }

    [Fact]
    public void Garbage_is_a_format_error_not_a_crash()
    {
        Assert.Throws<SaveFormatException>(() => SaveCodec.ReadPlayer("not json"));
        Assert.Throws<SaveFormatException>(() => SaveCodec.ReadPlayer("[1,2,3]"));
        Assert.Throws<SaveFormatException>(() => SaveCodec.ReadPlayer("{\"version\":1,\"level\":\"high\"}"));
    }

    [Fact]
    public void Save_without_a_version_field_is_read_as_version_1()
    {
        var loaded = SaveCodec.ReadPlayer("{\"xp\":10,\"level\":2}");
        Assert.Equal(2, loaded.Level);
        Assert.Equal(PlayerSave.CurrentVersion, loaded.Version);
    }

    [Fact]
    public void Version_1_saves_drop_deleted_perks_and_get_their_points_back()
    {
        string deleted = SaveCodec.DeletedPerks[0];
        var v1 = $"{{\"version\":1,\"level\":10,\"perks\":{{\"{deleted}\":1,\"perk.root\":1}}}}";
        var loaded = SaveCodec.ReadPlayer(v1);
        Assert.Equal(PlayerSave.CurrentVersion, loaded.Version);
        Assert.False(loaded.Perks.ContainsKey(deleted));
        Assert.Equal(1, loaded.Perks["perk.root"]);

        const string perks = """
        { "perks": [ { "id": "perk.root", "name": "Root", "category": "defense", "cost": 1 } ] }
        """;
        var tree = new Rules.PerkTree(TestContent.Create(new() { ["perks.json"] = perks }));
        tree.Restore(loaded.Perks, loaded.UnlockedSpells);
        Assert.Equal(9, tree.AvailablePoints(new Rules.Progress(loaded.Level, 0, loaded.BonusPerkPoints)));
    }

    [Fact]
    public void Version_3_saves_fold_eight_rarities_into_four()
    {
        var v3 = "{\"version\":3,\"level\":5,\"slots\":{\"bag.0\":{\"baseId\":\"item.a\",\"rarity\":1},\"bag.1\":{\"baseId\":\"item.b\",\"rarity\":4},\"bag.2\":{\"baseId\":\"item.c\",\"rarity\":7}}}";
        var loaded = SaveCodec.ReadPlayer(v3);
        Assert.Equal(new[] { 0, 2, 3 }, new[] { "bag.0", "bag.1", "bag.2" }.Select(k => loaded.Slots[k].Rarity));
        var world = SaveCodec.ReadWorld("{\"version\":1,\"minLootRarity\":5}");
        Assert.Equal((int)Rarity.Legendary, world.MinLootRarity);
    }

    [Fact]
    public void Loaded_items_take_their_base_rarity_so_old_uniques_become_legendary()
    {
        var db = TestContent.Create();
        var save = new PlayerSave { Level = 5 };
        save.Slots["bag.0"] = new SavedItem { BaseId = "item.ring", Level = 3, Rarity = (int)Rarity.Common };
        var pc = new Rules.PlayerCharacter(db);
        pc.Load(SaveCodec.ReadPlayer(SaveCodec.Write(save)));
        Assert.Equal((Rarity)db.Items["item.ring"].Rarity, pc.Inventory.Get("bag.0")!.Rarity);
    }

    [Fact]
    public void Version_4_saves_drop_crafting_perks_materials_and_socket_stats()
    {
        string perk = SaveCodec.CraftingPerks[0], material = SaveCodec.DeletedItems[0];
        var v4 = $$"""
        { "version": 4, "level": 10, "perks": { "{{perk}}": 1, "perk.root": 1 },
          "slots": {
            "bag.0": { "baseId": "{{material}}", "level": 1, "rarity": 2, "amount": 7, "stats": [] },
            "bag.1": { "baseId": "item.ring", "level": 30, "rarity": 1, "amount": 1,
              "stats": [ { "id": "stat.max_life", "value": 40, "slot": 0 }, { "id": "stat.emptysocket", "value": 0, "slot": -1 },
                         { "id": "stat.socket_armor", "value": 25, "slot": -1 } ] } } }
        """;
        var loaded = SaveCodec.ReadPlayer(v4);
        Assert.Equal(PlayerSave.CurrentVersion, loaded.Version);
        Assert.False(loaded.Perks.ContainsKey(perk));
        Assert.Equal(1, loaded.Perks["perk.root"]);
        Assert.False(loaded.Slots.ContainsKey("bag.0"));
        Assert.Equal(new[] { "stat.max_life" }, loaded.Slots["bag.1"].Stats.Select(s => s.Id));
    }

    [Fact]
    public void Version_5_saves_drop_the_island_foods_and_start_with_no_grant_day()
    {
        var v5 = """
        { "version": 5, "level": 3,
          "slots": {
            "bag.0": { "baseId": "item.minibar_whiskey", "level": 12, "rarity": 0, "amount": 3, "stats": [] },
            "bag.1": { "baseId": "item.ring", "level": 30, "rarity": 1, "amount": 1, "stats": [] } } }
        """;
        var loaded = SaveCodec.ReadPlayer(v5);
        Assert.Equal(PlayerSave.CurrentVersion, loaded.Version);
        Assert.False(loaded.Slots.ContainsKey("bag.0"));
        Assert.True(loaded.Slots.ContainsKey("bag.1"));
        Assert.Equal(-1, loaded.LastGrantDay);
    }

    [Fact]
    public void Last_grant_day_round_trips()
    {
        var loaded = SaveCodec.ReadPlayer(SaveCodec.Write(new PlayerSave { LastGrantDay = 17 }));
        Assert.Equal(17, loaded.LastGrantDay);
    }
}
