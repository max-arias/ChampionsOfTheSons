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
    public void Health_carried_items_and_save_time_round_trip_and_default_to_not_saved()
    {
        var save = SamplePlayer();
        save.HealthFraction = 0.5;
        save.CarriedItems[392] = 14;
        save.SavedAt = 1_800_000_000;
        var loaded = SaveCodec.ReadPlayer(SaveCodec.Write(save));
        Assert.Equal(0.5, loaded.HealthFraction);
        Assert.Equal(14, loaded.CarriedItems[392]);
        Assert.Equal(1_800_000_000, loaded.SavedAt);

        var old = SaveCodec.ReadPlayer("{\"version\":1,\"level\":7,\"lastGrantDay\":3}");
        Assert.Equal(PlayerSave.CurrentVersion, old.Version);
        Assert.Equal(-1, old.HealthFraction);
        Assert.Empty(old.CarriedItems);
        Assert.Equal(3, old.LastGrantDay);
    }

    [Fact]
    public void A_client_loads_its_own_backup_only_when_it_is_newer_than_the_host_save()
    {
        var host = new PlayerSave { Level = 5, SavedAt = 100 };
        Assert.Same(host, ClientBackup.Pick(host, null));
        Assert.Same(host, ClientBackup.Pick(host, new PlayerSave { Level = 6, SavedAt = 100 }));
        Assert.Equal(6, ClientBackup.Pick(host, new PlayerSave { Level = 6, SavedAt = 101 }).Level);
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
    public void Last_grant_day_round_trips()
    {
        var loaded = SaveCodec.ReadPlayer(SaveCodec.Write(new PlayerSave { LastGrantDay = 17 }));
        Assert.Equal(17, loaded.LastGrantDay);
    }

    [Fact]
    public void Saves_stay_at_version_1_and_old_dev_numbers_still_load()
    {
        Assert.Equal(1, PlayerSave.CurrentVersion);
        Assert.Contains("\"version\":1", SaveCodec.Write(new PlayerSave()));
        var dev = SaveCodec.ReadPlayer($"{{\"version\":{PlayerSave.LastDevVersion},\"level\":9}}");
        Assert.Equal(9, dev.Level);
        Assert.Equal(1, dev.Version);
        Assert.Equal(1, SaveCodec.ReadWorld($"{{\"version\":{WorldSave.LastDevVersion},\"extraEnemyLevels\":2}}").Version);
        Assert.Throws<SaveVersionException>(() => SaveCodec.ReadPlayer($"{{\"version\":{PlayerSave.LastDevVersion + 1}}}"));
    }
}
