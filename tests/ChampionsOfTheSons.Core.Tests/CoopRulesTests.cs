using ChampionsOfTheSons.Core;
using ChampionsOfTheSons.Core.Networking;
using ChampionsOfTheSons.Core.Rules;

namespace ChampionsOfTheSons.Core.Tests;

public class CoopRulesTests
{
    [Fact]
    public void Handshake_accepts_equal_versions_and_explains_a_mismatch()
    {
        Assert.Null(Handshake.Check(new Hello(1, "0.1.0"), 1, "0.1.0"));
        Assert.Contains("0.2.0", Handshake.Check(new Hello(1, "0.1.0"), 1, "0.2.0"));
        Assert.Contains("protocol", Handshake.Check(new Hello(2, "0.1.0"), 1, "0.1.0"));
    }

    [Fact]
    public void Only_players_in_range_share_kill_xp()
    {
        var players = new[] { (1u, new Vec3(0, 0, 0)), (2u, new Vec3(10, 0, 0)), (3u, new Vec3(500, 0, 0)) };
        var got = KillCredit.Recipients(players, new Vec3(5, 0, 0), 80);
        Assert.Equal(new uint[] { 1, 2 }, got);
        Assert.Equal(EnemyScaler.KillXpShare(1000, 2), KillCredit.ShareFor(1000, got.Count));
    }

    [Fact]
    public void Team_magic_find_is_the_sum_over_players()
    {
        Assert.Equal(1.0, KillCredit.TeamMagicFind(Array.Empty<double>()));
        Assert.Equal(1.5, KillCredit.TeamMagicFind(new[] { 1.5 }));
        Assert.Equal(3.7, KillCredit.TeamMagicFind(new[] { 1.5, 1.2, 1.0 }), 9);
        // A broken report counts as the base 1, never as zero or a negative.
        Assert.Equal(2.5, KillCredit.TeamMagicFind(new[] { 1.5, double.NaN }), 9);
        Assert.Equal(2.0, KillCredit.TeamMagicFind(new[] { 1.0, -4 }), 9);
    }

    [Fact]
    public void Loot_level_follows_the_policy()
    {
        var near = new[] { (10, 30f), (50, 5f), (30, 12f) };
        Assert.Equal(50, KillCredit.LootLevel(LootLevelPolicy.Highest, near, 1));
        Assert.Equal(10, KillCredit.LootLevel(LootLevelPolicy.Lowest, near, 1));
        Assert.Equal(30, KillCredit.LootLevel(LootLevelPolicy.Average, near, 1));
        Assert.Equal(50, KillCredit.LootLevel(LootLevelPolicy.Closest, near, 1));
        Assert.Equal(7, KillCredit.LootLevel(LootLevelPolicy.Host, near, 7));
        Assert.Equal(7, KillCredit.LootLevel(LootLevelPolicy.Highest, Array.Empty<(int, float)>(), 7));
    }

    [Fact]
    public void Aura_buff_applies_in_range_and_ends_when_pulses_stop()
    {
        var board = new AuraBoard();
        var pulse = new AuraPulse(2, "buff.valor", new Vec3(0, 0, 0), 10, 2);
        Assert.False(board.Apply(pulse, new Vec3(50, 0, 0), 0));
        Assert.False(board.IsActive("buff.valor", 0.1));
        Assert.True(board.Apply(pulse, new Vec3(3, 0, 0), 0));
        Assert.True(board.IsActive("buff.valor", 1.9));
        Assert.False(board.IsActive("buff.valor", 2.1)); // walked away: no more pulses reach us
    }

    [Fact]
    public void A_player_has_one_ping_and_it_expires()
    {
        var board = new PingBoard();
        var expired = new List<Ping>();
        board.Expired += expired.Add;
        board.Add(new Ping(1, PingKind.Location, new Vec3(1, 1, 1), 0), 0);
        board.Add(new Ping(1, PingKind.Enemy, new Vec3(2, 2, 2), 9), 1);
        Assert.Single(board.Active);
        Assert.Single(expired);
        board.Tick(1 + PingBoard.Lifetime + 0.1);
        Assert.Empty(board.Active);
        Assert.Equal(2, expired.Count);
    }
}

public class CoopPickupTests
{
    private static readonly ItemInstance Item = new() { BaseId = "item.test_ring", Level = 5, Rarity = Rarity.Rare };

    [Fact]
    public void Only_the_first_of_two_pickup_requests_gets_the_item()
    {
        var ground = new GroundLoot();
        var drop = ground.Add(Item, 0, 0, 0);
        var removed = new List<(int, bool)>();
        ground.Removed += (d, picked) => removed.Add((d.Id, picked));
        Assert.Equal(Item, PickupArbiter.Claim(ground, (uint)drop.Id));
        Assert.Null(PickupArbiter.Claim(ground, (uint)drop.Id));
        Assert.Single(removed);
        Assert.Equal(0, ground.Count);
    }

    [Theory]
    [InlineData(DropSource.Enemy, Rarity.Common, 120)]
    [InlineData(DropSource.Enemy, Rarity.Legendary, 170)]
    [InlineData(DropSource.Thrown, Rarity.Magic, 68)]
    [InlineData(DropSource.Overflow, Rarity.Rare, 918)]
    public void Drops_last_by_source_and_rarity(DropSource source, Rarity rarity, double seconds) =>
        Assert.Equal(seconds, GroundLoot.LifetimeFor(source, rarity));

    [Fact]
    public void Each_drop_expires_on_its_own_lifetime_and_a_mirror_keeps_the_hosts_remaining_time()
    {
        var ground = new GroundLoot();
        var thrown = ground.Add(Item with { Rarity = Rarity.Common }, 0, 0, 0, source: DropSource.Thrown);
        var enemy = ground.Add(Item with { Rarity = Rarity.Common }, 0, 0, 0);
        ground.Tick(61);
        Assert.Null(ground.Get(thrown.Id));
        Assert.NotNull(ground.Get(enemy.Id));
        var mirrored = ground.Mirror(99, Item, 0, 0, 0, remaining: 5);
        ground.Tick(5.5);
        Assert.Null(ground.Get(mirrored.Id));
        Assert.NotNull(ground.Get(enemy.Id));
    }

    [Fact]
    public void A_mirrored_drop_keeps_the_hosts_id_and_can_be_removed()
    {
        var ground = new GroundLoot();
        var added = new List<int>();
        ground.Added += d => added.Add(d.Id);
        ground.Mirror(77, Item, 1, 2, 3);
        Assert.Equal(new[] { 77 }, added);
        Assert.NotNull(ground.Get(77));
        Assert.True(ground.Remove(77));
    }
}

public class AdminAccessTests
{
    [Theory]
    [InlineData(CommandSource.SinglePlayer, true)]
    [InlineData(CommandSource.Host, true)]
    [InlineData(CommandSource.ServerAdmin, true)]
    [InlineData(CommandSource.Client, false)]
    [InlineData(CommandSource.ServerPlayer, false)]
    public void Only_the_host_and_server_admins_may_run_commands(CommandSource source, bool allowed)
    {
        Assert.Equal(allowed, AdminAccess.MayRun(source, commandsEnabled: true));
        Assert.False(AdminAccess.MayRun(source, commandsEnabled: false));
    }

    [Fact]
    public void Parse_drops_the_cots_prefix_and_defaults_to_help()
    {
        var (name, args) = AdminAccess.Parse("cots XP 500 2");
        Assert.Equal("xp", name);
        Assert.Equal(new[] { "500", "2" }, args);
        Assert.Equal("help", AdminAccess.Parse("cots").Name);
        Assert.Equal("give", AdminAccess.Parse("  give item.x ").Name);
    }
}
