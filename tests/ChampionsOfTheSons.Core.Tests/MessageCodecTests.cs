using ChampionsOfTheSons.Core;
using ChampionsOfTheSons.Core.Networking;

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

    public static TheoryData<IMessage> AllMessages() => new()
    {
        new Hello(MessageCodec.ProtocolVersion, "0.1.0"),
        new HelloReject("Mod version mismatch: host 0.2.0, you 0.1.0"),
        new WorldSettings("difficulty.hell", true),
        new EnemyProfileRequest(0xDEADBEEF12345678UL),
        new EnemySync(42UL, "enemy.cannibal_heavy", 250, 3, 1.5e6f, 1.2e6f, 4500f, new[] { "affix.meteor", "affix.steadfast" }, 9_000_000_000L),
        new EnemySync(43UL, "enemy.fingers", 1, 0, 100f, 100f, 0f, Array.Empty<string>(), 0),
        new EnemyHealth(42UL, 99.5f, 12f),
        new Hit(42UL, 12345.678f, HitFlags.Crit | HitFlags.Melee, 300f),
        new XpGranted(-5),
        new PlayerPos(new Vec3(1.5f, 2, -3), 42),
        new DamageShown(2u, 42UL, new Vec3(1, 2, 3), 777.5f, HitFlags.Crit | HitFlags.Ranged),
        new AuraPulse(2u, "buff.aura_of_valor", new Vec3(4, 5, 6), 12f, 2.5f),
        new XpGranted(long.MaxValue),
        new DropSpawned(7u, SampleItem, new Vec3(1.5f, -2f, 300.25f)),
        new PickupRequest(7u),
        new PickupGranted(uint.MaxValue, SampleItem),
        new DropRemoved(7u),
        new SpellCast(3u, "spell.blink", new Vec3(0, 1, 2), new Vec3(0, 0, 1), 123456789UL),
        new Ping(3u, PingKind.Enemy, new Vec3(10, 20, 30), 42UL),
        new WorldSettings("difficulty.ünïcødé", false),
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
