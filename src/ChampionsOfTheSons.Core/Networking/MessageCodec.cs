namespace ChampionsOfTheSons.Core.Networking;

/// <summary>
/// Binary encoding of <see cref="IMessage"/>s: one type byte, then the fields in a fixed order.
/// Bump <see cref="ProtocolVersion"/> whenever an encoding changes; <see cref="Hello"/> carries it.
/// </summary>
public static class MessageCodec
{
    /// <remarks>2: <see cref="EnemySync"/> carries the damage multiplier and the elite name (COS-68).
    /// 3: <see cref="EnemySync"/> carries the scale; <see cref="SpellCast"/> its mode, cast id and visuals; new
    /// <see cref="SpellFxEvent"/>, <see cref="AbilityEvent"/>, <see cref="PlayerInfo"/>, <see cref="Announce"/> (COS-79/80/81).</remarks>
    public const int ProtocolVersion = 3;

    public static byte[] Encode(IMessage message)
    {
        var w = new PacketWriter();
        w.Byte((byte)message.Type);
        switch (message)
        {
            case Hello m: w.VarInt(m.ProtocolVersion).String(m.ModVersion); break;
            case HelloReject m: w.String(m.Reason); break;
            case WorldSettings m: w.String(m.DifficultyId).Bool(m.FriendlyFire); break;
            case EnemyProfileRequest m: w.UInt64(m.EnemyNetId); break;
            case EnemySync m:
                w.UInt64(m.EnemyNetId).String(m.KindId).VarInt(m.Level).Byte(m.Tier)
                    .Float(m.MaxHealth).Float(m.Health).Float(m.Armor);
                w.VarUInt((uint)m.AffixIds.Count);
                foreach (var a in m.AffixIds) w.String(a);
                WriteLong(w, m.Bounty);
                w.Float(m.DamageMult).String(m.NameGivenId).String(m.NameEpithetId).Float(m.Scale);
                break;
            case EnemyHealth m: w.UInt64(m.EnemyNetId).Float(m.Health).Float(m.Armor); break;
            case Hit m: w.UInt64(m.EnemyNetId).Float(m.Amount).Byte((byte)m.Flags).Float(m.ArmorPierce); break;
            case XpGranted m: WriteLong(w, m.Amount); break;
            case PlayerPos m: WriteVec(w, m.Position); w.VarInt(m.Level); break;
            case DamageShown m: w.VarUInt(m.PlayerId).UInt64(m.EnemyNetId); WriteVec(w, m.Position); w.Float(m.Amount).Byte((byte)m.Flags); break;
            case AuraPulse m: w.VarUInt(m.PlayerId).String(m.BuffId); WriteVec(w, m.Origin); w.Float(m.Radius).Float(m.Seconds); break;
            case DropSpawned m: w.VarUInt(m.DropId); WriteItem(w, m.Item); WriteVec(w, m.Position); break;
            case PickupRequest m: w.VarUInt(m.DropId); break;
            case PickupGranted m: w.VarUInt(m.DropId); WriteItem(w, m.Item); break;
            case DropRemoved m: w.VarUInt(m.DropId); break;
            case SpellCast m:
                w.VarUInt(m.PlayerId).String(m.SpellId);
                WriteVec(w, m.Origin);
                WriteVec(w, m.Direction);
                w.UInt64(m.Seed).Byte((byte)m.Mode).VarUInt(m.CastId);
                WriteShapes(w, m.AllShapes);
                break;
            case SpellFxEvent m:
                w.VarUInt(m.PlayerId).VarUInt(m.CastId);
                if (m.Stop.Count > MaxShapes) throw new ArgumentException($"At most {MaxShapes} stops per message");
                w.VarUInt((uint)m.Stop.Count);
                foreach (var b in m.Stop) w.Byte(b);
                WriteVec(w, m.At);
                WriteShapes(w, m.Shapes);
                break;
            case AbilityEvent m: w.UInt64(m.EnemyNetId).String(m.Ability).Byte((byte)m.Phase); WriteVec(w, m.Point); w.UInt64(m.Seed).Bool(m.TargetIsYou); break;
            case PlayerInfo m: w.VarUInt(m.PlayerId).String(m.Name).VarInt(m.Level).Float(m.MagicFind); WriteVec(w, m.Position); break;
            case Announce m: w.Byte((byte)m.Kind).VarUInt(m.PlayerId).String(m.Name).VarInt(m.Level); break;
            case Ping m: w.VarUInt(m.PlayerId).Byte((byte)m.Kind); WriteVec(w, m.Position); w.UInt64(m.TargetId); break;
            case AdminGrant m: w.Byte((byte)m.Action).UInt64(unchecked((ulong)BitConverter.DoubleToInt64Bits(m.Amount))).String(m.Arg); break;
            case HeldWeapon m: w.VarUInt(m.PlayerId).String(m.TypeId); WriteVec(w, m.Position); break;
            default: throw new ArgumentException($"No encoding for {message.GetType().Name}");
        }
        return w.ToArray();
    }

    /// <summary>Decodes one message. Throws <see cref="PacketException"/> for unknown types or malformed data.</summary>
    public static IMessage Decode(byte[] data)
    {
        var r = new PacketReader(data);
        var type = (MessageType)r.Byte();
        IMessage msg = type switch
        {
            MessageType.Hello => new Hello(r.VarInt(), r.String() ?? ""),
            MessageType.HelloReject => new HelloReject(r.String() ?? ""),
            MessageType.WorldSettings => new WorldSettings(r.String() ?? "", r.Bool()),
            MessageType.EnemyProfileRequest => new EnemyProfileRequest(r.UInt64()),
            MessageType.EnemySync => ReadEnemySync(r),
            MessageType.EnemyHealth => new EnemyHealth(r.UInt64(), r.Float(), r.Float()),
            MessageType.Hit => new Hit(r.UInt64(), r.Float(), (HitFlags)r.Byte(), r.Float()),
            MessageType.XpGranted => new XpGranted(ReadLong(r)),
            MessageType.PlayerPos => new PlayerPos(ReadVec(r), r.VarInt()),
            MessageType.DamageShown => new DamageShown(r.VarUInt(), r.UInt64(), ReadVec(r), r.Float(), (HitFlags)r.Byte()),
            MessageType.AuraPulse => new AuraPulse(r.VarUInt(), r.String() ?? "", ReadVec(r), r.Float(), r.Float()),
            MessageType.DropSpawned => new DropSpawned(r.VarUInt(), ReadItem(r), ReadVec(r)),
            MessageType.PickupRequest => new PickupRequest(r.VarUInt()),
            MessageType.PickupGranted => new PickupGranted(r.VarUInt(), ReadItem(r)),
            MessageType.DropRemoved => new DropRemoved(r.VarUInt()),
            MessageType.SpellCast => new SpellCast(r.VarUInt(), r.String() ?? "", ReadVec(r), ReadVec(r), r.UInt64(), (SpellCastMode)r.Byte(), r.VarUInt(), ReadShapes(r)),
            MessageType.SpellFxEvent => ReadSpellFxEvent(r),
            MessageType.AbilityEvent => new AbilityEvent(r.UInt64(), r.String() ?? "", (AbilityPhase)r.Byte(), ReadVec(r), r.UInt64(), r.Bool()),
            MessageType.PlayerInfo => new PlayerInfo(r.VarUInt(), r.String() ?? "", r.VarInt(), r.Float(), ReadVec(r)),
            MessageType.Announce => new Announce((AnnounceKind)r.Byte(), r.VarUInt(), r.String() ?? "", r.VarInt()),
            MessageType.Ping => new Ping(r.VarUInt(), (PingKind)r.Byte(), ReadVec(r), r.UInt64()),
            MessageType.AdminGrant => new AdminGrant((AdminAction)r.Byte(), BitConverter.Int64BitsToDouble(unchecked((long)r.UInt64())), r.String() ?? ""),
            MessageType.HeldWeapon => new HeldWeapon(r.VarUInt(), r.String() ?? "", ReadVec(r)),
            _ => throw new PacketException($"Unknown message type {(byte)type}"),
        };
        if (!r.AtEnd) throw new PacketException($"Trailing bytes after {type}");
        return msg;
    }

    private static EnemySync ReadEnemySync(PacketReader r)
    {
        ulong id = r.UInt64();
        string kind = r.String() ?? "";
        int level = r.VarInt();
        byte tier = r.Byte();
        float maxHp = r.Float(), hp = r.Float(), armor = r.Float();
        uint count = r.VarUInt();
        if (count > 64) throw new PacketException("Too many affixes");
        var affixes = new string[count];
        for (int i = 0; i < count; i++) affixes[i] = r.String() ?? "";
        long bounty = ReadLong(r);
        float dmg = r.Float();
        string given = r.String() ?? "", epithet = r.String() ?? "";
        float scale = r.Float();
        return new EnemySync(id, kind, level, tier, maxHp, hp, armor, affixes, bounty, dmg, given, epithet, scale);
    }

    private static SpellFxEvent ReadSpellFxEvent(PacketReader r)
    {
        uint player = r.VarUInt(), castId = r.VarUInt();
        uint count = r.VarUInt();
        if (count > MaxShapes) throw new PacketException("Too many stopped shapes");
        var stop = new byte[count];
        for (int i = 0; i < count; i++) stop[i] = r.Byte();
        var at = ReadVec(r);
        return new SpellFxEvent(player, castId, stop, at, ReadShapes(r));
    }

    /// <summary>Most shapes (or stops) one cast or update may carry; a cast draws a handful.</summary>
    public const int MaxShapes = 64;

    private static void WriteShapes(PacketWriter w, IReadOnlyList<FxShape> shapes)
    {
        if (shapes.Count > MaxShapes) throw new ArgumentException($"At most {MaxShapes} shapes per message");
        w.VarUInt((uint)shapes.Count);
        foreach (var s in shapes)
        {
            w.Byte((byte)s.Kind).Byte(s.Look);
            WriteVec(w, s.Position);
            w.Float(s.Radius).Float(s.Height).VarUInt(s.Rgb).Float(s.Seconds).Bool(s.Follow);
            WriteVec(w, s.Velocity);
            WriteVec(w, s.Facing);
        }
    }

    private static FxShape[] ReadShapes(PacketReader r)
    {
        uint count = r.VarUInt();
        if (count > MaxShapes) throw new PacketException("Too many shapes");
        var shapes = new FxShape[count];
        for (int i = 0; i < count; i++)
            shapes[i] = new FxShape((FxKind)r.Byte(), r.Byte(), ReadVec(r), r.Float(), r.Float(), r.VarUInt(), r.Float(), r.Bool(), ReadVec(r), ReadVec(r));
        return shapes;
    }

    private static void WriteLong(PacketWriter w, long v) => w.UInt64(unchecked((ulong)v));

    private static long ReadLong(PacketReader r) => unchecked((long)r.UInt64());

    private static void WriteVec(PacketWriter w, Vec3 v) => w.Float(v.X).Float(v.Y).Float(v.Z);

    private static Vec3 ReadVec(PacketReader r) => new(r.Float(), r.Float(), r.Float());

    private static void WriteItem(PacketWriter w, ItemInstance item)
    {
        w.String(item.BaseId).VarInt(item.Level).Byte((byte)item.Rarity).VarInt(item.Amount).VarInt(item.Upgrades);
        w.VarUInt((uint)item.Stats.Count);
        foreach (var s in item.Stats) w.String(s.StatId).Float(s.Value).VarInt(s.Slot);
    }

    private static ItemInstance ReadItem(PacketReader r)
    {
        string baseId = r.String() ?? "";
        int level = r.VarInt();
        var rarity = (Rarity)r.Byte();
        int amount = r.VarInt();
        int upgrades = r.VarInt();
        uint count = r.VarUInt();
        if (count > 64) throw new PacketException("Too many item stats");
        var stats = new RolledStat[count];
        for (int i = 0; i < count; i++) stats[i] = new RolledStat(r.String() ?? "", r.Float(), r.VarInt());
        return new ItemInstance { BaseId = baseId, Level = level, Rarity = rarity, Amount = amount, Upgrades = upgrades, Stats = stats };
    }
}
