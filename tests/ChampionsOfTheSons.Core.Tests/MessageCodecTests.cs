using ChampionsOfTheSons.Core;
using ChampionsOfTheSons.Core.Networking;
using ChampionsOfTheSons.Core.Rules;

namespace ChampionsOfTheSons.Core.Tests;

public class MessageCodecTests
{
    private static readonly ItemInstance SampleItem = new()
    {
        BaseId = "item.test_ring",
        Level = 37,
        Rarity = Rarity.Legendary,
        Amount = 1,
        Upgrades = 2,
        Stats = new[] { new RolledStat("stat.max_health", 120.5f), new RolledStat("stat.crit_chance", 0.07f) },
    };

    private static readonly FxShape Shape = new(FxKind.Disc, 6, new Vec3(1, 2, 3), 7.5f, 0, 0xFF8040u, 12f, false, new Vec3(0, 0, 30), new Vec3(1, 0, 0));

    public static TheoryData<IMessage> AllMessages() => new()
    {
        new Hello(MessageCodec.ProtocolVersion, "0.1.0"),
        new HelloReject("Mod version mismatch: host 0.2.0, you 0.1.0"),
        new WorldSettings(GameMode.Hard, 5, 3, PvpMode.Reduced),
        new EnemySync(42UL, "enemy.cannibal_heavy", 250, 3, 1.5e6f, 1.2e6f, 4500f, new[] { "affix.meteor", "affix.steadfast" }, 9_000_000_000L),
        new EnemySync(43UL, "enemy.fingers", 1, 0, 100f, 100f, 0f, Array.Empty<string>(), 0),
        new EnemySync(44UL, "Carl", 77, 1, 9e5f, 4e5f, 1200f, new[] { "Meteor" }, 123_456L, 3.4e4f, "elite_name.ashjaw", "elite_name.unburied"),
        new EnemySync(45UL, "Carl", 77, 4, 9e5f, 4e5f, 1200f, new[] { "Gargantuan" }, 1L, 1f, "", "", 1.9f),
        new EnemyHealth(42UL, 99.5f, 12f),
        new Hit(42UL, 12345.678f, HitFlags.Crit | HitFlags.Melee, 300f),
        new XpGranted(-5),
        new EnemyEffectNet(42UL, EnemyEffectKind.Dot, "player.bleed", 125.5f, 10f, DotKind.Bleed),
        new EnemyEffectNet(43UL, EnemyEffectKind.Stun, "", 0f, 1.5f, default),
        new EnemyPush(42UL, new Vec3(-1.5f, 0, 3)),
        new WorldHit(WorldHitKind.AreaToward, new Vec3(1, 2, 3), 12f, 345.5f, new Vec3(4, 5, 6), 0f),
        new PlayerSpellHit(3u, 812.5f),
        new WorldHit(WorldHitKind.Pull, new Vec3(1, 2, 3), 8f, 4f, default, 0.3f),
        new EnemyAware(42UL, true),
        new EnemyState(42UL, EnemyStateFlags.ShieldUp | EnemyStateFlags.Revived),
        new FlatXpGranted(30L, new Vec3(5, 6, 7)),
        new PlayerStateNet(2u, PlayerStateFlags.GoldenSkin | PlayerStateFlags.BlackFlame, new Vec3(1, 2, 3)),
        new PlayerPos(new Vec3(1.5f, 2, -3), 42),
        new DamageShown(2u, 42UL, new Vec3(1, 2, 3), 777.5f, HitFlags.Crit | HitFlags.Ranged),
        new AuraPulse(2u, "buff.aura_of_valor", new Vec3(4, 5, 6), 12f, 2.5f),
        new AuraPulse(2u, "spell.war_cry", new Vec3(4, 5, 6), 20f, 20f, 1.35f, 1.2f),
        new XpGranted(long.MaxValue),
        new DropSpawned(7u, SampleItem, new Vec3(1.5f, -2f, 300.25f), 42.5f),
        new PickupRequest(7u),
        new PickupGranted(uint.MaxValue, SampleItem),
        new DropRemoved(7u),
        new DropRequest(SampleItem, new Vec3(1, 2, 3), DropSource.Thrown),
        new SpellCast(3u, "spell.blink", new Vec3(0, 1, 2), new Vec3(0, 0, 1), 123456789UL),
        new SpellCast(3u, "spell.black_hole", new Vec3(0, 1, 2), new Vec3(0, 0, 1), 5UL, SpellCastMode.Cast, 17u, new[] { Shape, Shape with { Kind = FxKind.Sphere, Follow = true, HomingTarget = 42UL } }),
        new SpellCast(0u, "spell.black_flame", new Vec3(0, 1, 2), new Vec3(0, 0, 1), 5UL, SpellCastMode.Off, uint.MaxValue, Array.Empty<FxShape>()),
        new SpellFxEvent(3u, 17u, new byte[] { 0, 2 }, new Vec3(4, 5, 6), new[] { Shape }),
        new SpellFxEvent(3u, 18u, Array.Empty<byte>(), new Vec3(0, 0, 0), Array.Empty<FxShape>()),
        new AbilityEvent(42UL, "Laser", AbilityPhase.Lock, new Vec3(1, 2, 3), ulong.MaxValue, true),
        new AbilityEvent(43UL, "Meteor", AbilityPhase.Begin, new Vec3(-1, 0, 9), 7UL, false),
        new PlayerInfo(2u, "Wanderer ünï", 87, 2.35f, new Vec3(1, 2, 3)),
        new PlayerInfo(3u, "Second", 12, 1f, new Vec3(1, 2, 3), 0.75f),
        new Announce(AnnounceKind.LevelUp, 2u, "Wanderer", 88),
        new Announce(AnnounceKind.Left, 0u, "", 0),
        new Ping(3u, PingKind.Enemy, new Vec3(10, 20, 30), 42UL),
        new Ping(uint.MaxValue, PingKind.Clear, new Vec3(1, 2, 3), 0UL),
        new HeldWeapon(2u, "long_sword", new Vec3(1, 2, 3), 7, "item.rage"),
        new WorldSettings(GameMode.Custom, -4, 0, PvpMode.Off),
        new AdminGrant(AdminAction.GiveItem, 42, "item.test_ring"),
        new AdminGrant(AdminAction.AddXp, 1.5e15, ""),
    };

    [Theory]
    [MemberData(nameof(AllMessages))]
    public void Every_message_round_trips(IMessage message)
    {
        var decoded = MessageCodec.Decode(MessageCodec.Encode(message));
        Assert.Equal(message, decoded);
    }

    [Fact]
    public void Every_message_type_has_a_round_trip_case()
    {
        var covered = new HashSet<MessageType>();
        foreach (object[] row in (IEnumerable<object[]>)AllMessages()) covered.Add(((IMessage)row[0]).Type);
        foreach (MessageType type in Enum.GetValues(typeof(MessageType)))
            Assert.Contains(type, covered);
    }

    [Fact]
    public void Truncated_packet_is_rejected()
    {
        var bytes = MessageCodec.Encode(new DropSpawned(1u, SampleItem, new Vec3(1, 2, 3)));
        Assert.Throws<PacketException>(() => MessageCodec.Decode(bytes[..^3]));
    }

    [Fact]
    public void Unknown_type_and_trailing_bytes_are_rejected()
    {
        Assert.Throws<PacketException>(() => MessageCodec.Decode(new byte[] { 250 }));
        var bytes = MessageCodec.Encode(new PickupRequest(1u)).Append((byte)0).ToArray();
        Assert.Throws<PacketException>(() => MessageCodec.Decode(bytes));
    }
}
