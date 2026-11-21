using ChampionsOfTheSons.Core.Rules;

namespace ChampionsOfTheSons.Core.Networking;

/// <summary>
/// Every message the mod sends. Authority: the host owns enemies, drops, pickups and difficulty;
/// a client owns its own stat sheet, hits and spell casts. <see cref="MessageCodec"/> turns these into
/// bytes; the mod sends the bytes through one SonsSdk NetEvent.
/// </summary>
public enum MessageType : byte
{
    Hello = 1,
    HelloReject = 2,
    WorldSettings = 3,
    EnemyProfileRequest = 10,
    EnemySync = 11,
    EnemyHealth = 12,
    Hit = 13,
    XpGranted = 14,
    DamageShown = 15,
    PlayerPos = 16,
    EnemyEffectNet = 17,
    EnemyPush = 18,
    EnemyAware = 19,
    DropSpawned = 20,
    PickupRequest = 21,
    PickupGranted = 22,
    DropRemoved = 23,
    DropRequest = 24,
    SpellCast = 30,
    AuraPulse = 31,
    Ping = 40,
    AdminGrant = 50,
    HeldWeapon = 51,
    SpellFxEvent = 52,
    AbilityEvent = 53,
    PlayerInfo = 54,
    Announce = 55,
}

public interface IMessage
{
    MessageType Type { get; }
}

public readonly record struct Vec3(float X, float Y, float Z);

/// <summary>Client → host on join. The host answers with <see cref="HelloReject"/> on a mismatch.</summary>
public sealed record Hello(int ProtocolVersion, string ModVersion) : IMessage
{
    public MessageType Type => MessageType.Hello;
}

public sealed record HelloReject(string Reason) : IMessage
{
    public MessageType Type => MessageType.HelloReject;
}

/// <summary>Host → clients: world settings that the host owns (COS-130: the game's difficulty as the host reads it, and its extra levels).</summary>
public sealed record WorldSettings(GameMode Mode, int LevelOffset, int ExtraEnemyLevels, bool FriendlyFire) : IMessage
{
    public MessageType Type => MessageType.WorldSettings;
}

/// <summary>Client → host: send me this enemy's profile. <paramref name="EnemyNetId"/> is the packed Bolt network ID.</summary>
public sealed record EnemyProfileRequest(ulong EnemyNetId) : IMessage
{
    public MessageType Type => MessageType.EnemyProfileRequest;
}

/// <summary>Host → clients: the rolled profile of one enemy. Protocol 2 added the damage multiplier and the elite name ids
/// (empty strings: no name) for the scanner and nameplates (COS-68); protocol 3 the model scale (COS-79).</summary>
public sealed record EnemySync(
    ulong EnemyNetId,
    string KindId,
    int Level,
    byte Tier,
    float MaxHealth,
    float Health,
    float Armor,
    IReadOnlyList<string> AffixIds,
    long Bounty,
    float DamageMult = 0,
    string NameGivenId = "",
    string NameEpithetId = "",
    float Scale = 1) : IMessage
{
    public MessageType Type => MessageType.EnemySync;

    public bool Equals(EnemySync? other) =>
        other is not null && EnemyNetId == other.EnemyNetId && KindId == other.KindId && Level == other.Level &&
        Tier == other.Tier && MaxHealth.Equals(other.MaxHealth) && Health.Equals(other.Health) &&
        Armor.Equals(other.Armor) && Bounty == other.Bounty && AffixIds.SequenceEqual(other.AffixIds) &&
        DamageMult.Equals(other.DamageMult) && NameGivenId == other.NameGivenId && NameEpithetId == other.NameEpithetId &&
        Scale.Equals(other.Scale);

    public override int GetHashCode() => HashCode.Combine(EnemyNetId, KindId, Level, Tier);
}

/// <summary>Host → clients: health/armor changed.</summary>
public sealed record EnemyHealth(ulong EnemyNetId, float Health, float Armor) : IMessage
{
    public MessageType Type => MessageType.EnemyHealth;
}

[Flags]
public enum HitFlags : byte
{
    None = 0,
    Crit = 1,
    Magic = 2,
    Pure = 4,
    Melee = 8,
    Ranged = 16,
    Thrown = 32,
}

/// <summary>Client → host: the client threw an item out of its bag (or its bag overflowed); the host makes the drop for everyone (COS-14).</summary>
public sealed record DropRequest(ItemInstance Item, Vec3 Position, DropSource Source) : IMessage
{
    public MessageType Type => MessageType.DropRequest;
}

/// <summary>Host → clients: a boss noticed a player or lost track (only the host runs the AI), for the boss bar (COS-116).</summary>
public sealed record EnemyAware(ulong EnemyNetId, bool Aware) : IMessage
{
    public MessageType Type => MessageType.EnemyAware;
}

/// <summary>Client → host: a debuff the client's hit or spell put on a host-owned enemy (COS-13).</summary>
public sealed record EnemyEffectNet(ulong EnemyNetId, EnemyEffectKind Kind, string Source, float Amount, float Seconds, DotKind Dot) : IMessage
{
    public MessageType Type => MessageType.EnemyEffectNet;
}

/// <summary>Client → host: a spell moved a host-owned enemy (pull, knockback) by <paramref name="Delta"/> (COS-13).</summary>
public sealed record EnemyPush(ulong EnemyNetId, Vec3 Delta) : IMessage
{
    public MessageType Type => MessageType.EnemyPush;
}

/// <summary>Client → host: an outgoing hit computed from the attacker's own stat sheet. The host applies defense.</summary>
public sealed record Hit(ulong EnemyNetId, float Amount, HitFlags Flags, float ArmorPierce) : IMessage
{
    public MessageType Type => MessageType.Hit;
}

/// <summary>Host → one client: XP earned (kill share, bounty, massacre payout).</summary>
public sealed record XpGranted(long Amount) : IMessage
{
    public MessageType Type => MessageType.XpGranted;
}

/// <summary>Host → clients: an item dropped in the world, with the seconds it has left (COS-96).</summary>
public sealed record DropSpawned(uint DropId, ItemInstance Item, Vec3 Position, float Remaining = 120f) : IMessage
{
    public MessageType Type => MessageType.DropSpawned;
}

/// <summary>Client → host: I want to pick this drop up.</summary>
public sealed record PickupRequest(uint DropId) : IMessage
{
    public MessageType Type => MessageType.PickupRequest;
}

/// <summary>Host → the requesting client: the drop is yours.</summary>
public sealed record PickupGranted(uint DropId, ItemInstance Item) : IMessage
{
    public MessageType Type => MessageType.PickupGranted;
}

/// <summary>Host → clients: remove this drop from the world.</summary>
public sealed record DropRemoved(uint DropId) : IMessage
{
    public MessageType Type => MessageType.DropRemoved;
}

/// <summary>What a <see cref="SpellCast"/> is: a one-off cast, or a toggle spell switching on or off.</summary>
public enum SpellCastMode : byte
{
    Cast = 0,
    On = 1,
    Off = 2,
}

/// <summary>The shape a <see cref="FxShape"/> draws (the caster's spell visual layer has these four).</summary>
public enum FxKind : byte
{
    Sphere = 0,
    Disc = 1,
    Column = 2,
    Portal = 3,
}

/// <summary>
/// One spell visual exactly as the caster drew it (COS-80): kind and look, where, how big, which colour (0xRRGGBB), for how
/// long. <paramref name="Follow"/>: it rides on the caster's body. <paramref name="Velocity"/>: it flies (bolts, Ball Lightning).
/// <paramref name="Facing"/>: which way a portal looks. <paramref name="Look"/> is the caster's look enum as a byte.
/// <paramref name="HomingTarget"/>: the enemy net id a flying visual curves toward (Seeking Arrow, COS-114); 0 flies straight.
/// </summary>
public readonly record struct FxShape(
    FxKind Kind,
    byte Look,
    Vec3 Position,
    float Radius,
    float Height,
    uint Rgb,
    float Seconds,
    bool Follow,
    Vec3 Velocity,
    Vec3 Facing,
    ulong HomingTarget = 0);

/// <summary>Caster → everyone: a spell cast, with every visual it drew (COS-80) so receivers show the same thing at the same
/// place, size and duration. <paramref name="CastId"/> numbers the caster's casts, so a later <see cref="SpellFxEvent"/> can stop
/// or add to this cast's visuals. A toggle spell sends <see cref="SpellCastMode.On"/> and later <see cref="SpellCastMode.Off"/>.
/// Protocol 3 added the mode, cast id and shapes.</summary>
public sealed record SpellCast(
    uint PlayerId,
    string SpellId,
    Vec3 Origin,
    Vec3 Direction,
    ulong Seed,
    SpellCastMode Mode = SpellCastMode.Cast,
    uint CastId = 0,
    IReadOnlyList<FxShape>? Shapes = null) : IMessage
{
    public MessageType Type => MessageType.SpellCast;

    public IReadOnlyList<FxShape> AllShapes => Shapes ?? Array.Empty<FxShape>();

    public bool Equals(SpellCast? other) =>
        other is not null && PlayerId == other.PlayerId && SpellId == other.SpellId && Origin == other.Origin &&
        Direction == other.Direction && Seed == other.Seed && Mode == other.Mode && CastId == other.CastId &&
        AllShapes.SequenceEqual(other.AllShapes);

    public override int GetHashCode() => HashCode.Combine(PlayerId, SpellId, CastId);
}

/// <summary>Caster → everyone: later visuals of a running cast (COS-80). <paramref name="Stop"/> lists indices of the cast's
/// shapes that end now (a bolt hit something), <paramref name="At"/> is where the first of them ended; <paramref name="Shapes"/>
/// are new visuals (an impact), numbered after the ones the cast already has.</summary>
public sealed record SpellFxEvent(uint PlayerId, uint CastId, IReadOnlyList<byte> Stop, Vec3 At, IReadOnlyList<FxShape> Shapes) : IMessage
{
    public MessageType Type => MessageType.SpellFxEvent;

    public bool Equals(SpellFxEvent? other) =>
        other is not null && PlayerId == other.PlayerId && CastId == other.CastId && At == other.At &&
        Stop.SequenceEqual(other.Stop) && Shapes.SequenceEqual(other.Shapes);

    public override int GetHashCode() => HashCode.Combine(PlayerId, CastId);
}

public enum PingKind : byte
{
    Location = 0,
    Enemy = 1,
    Item = 2,
}

/// <summary>Player → everyone: a ping marker. <paramref name="TargetId"/> is the enemy net ID or drop ID, 0 for locations.</summary>
public sealed record Ping(uint PlayerId, PingKind Kind, Vec3 Position, ulong TargetId) : IMessage
{
    public MessageType Type => MessageType.Ping;
}

/// <summary>Host → clients: a hit landed on an enemy, so every client can show a damage number (COS-44).
/// <paramref name="PlayerId"/> is the attacker; that player's own client already showed it.</summary>
public sealed record DamageShown(uint PlayerId, ulong EnemyNetId, Vec3 Position, float Amount, HitFlags Flags) : IMessage
{
    public MessageType Type => MessageType.DamageShown;
}

/// <summary>Caster → everyone: a Support perk or aura is active around <paramref name="Origin"/> for a moment.
/// Receivers inside <paramref name="Radius"/> get the buff for <paramref name="Seconds"/>; it is repeated while the aura lasts,
/// so a teammate who leaves the range loses it when the last pulse runs out (COS-23).</summary>
/// <remarks><paramref name="Amount"/> and <paramref name="Amount2"/> carry the caster's numbers for team spells (COS-23): heal, buff size.</remarks>
public sealed record AuraPulse(uint PlayerId, string BuffId, Vec3 Origin, float Radius, float Seconds, float Amount = 0, float Amount2 = 0) : IMessage
{
    public MessageType Type => MessageType.AuraPulse;
}

/// <summary>Client → host, about once a second: where I am and my level (the host decides who is in range of a kill and a pickup).</summary>
public sealed record PlayerPos(Vec3 Position, int Level) : IMessage
{
    public MessageType Type => MessageType.PlayerPos;
}

/// <summary>What an <see cref="AdminGrant"/> does to the receiving player.</summary>
public enum AdminAction : byte
{
    AddXp = 1,
    SetLevel = 2,
    AddLevels = 3,
    AddPerkPoints = 4,
    Respec = 5,
    /// <summary><c>Arg</c> = item base id, <c>Amount</c> = item level.</summary>
    GiveItem = 6,
    Heal = 7,
    /// <summary><c>Amount</c> 1 = on, 0 = off.</summary>
    God = 8,
    /// <summary><c>Amount</c> 1 = on, 0 = off.</summary>
    NoCooldowns = 9,
}

/// <summary>Host → one client: an admin command applied to that player (COS-99). Clients accept it only from the host.</summary>
public sealed record AdminGrant(AdminAction Action, double Amount, string Arg) : IMessage
{
    public MessageType Type => MessageType.AdminGrant;
}

/// <summary>Player → everyone, on change and every few seconds while set: the weapon type in my hand ("" = none), and where
/// I am, so receivers can find my body and show the model on my held game weapon (COS-93). Also the item's rarity and base
/// item id, so receivers show the same glow (COS-108).</summary>
public sealed record HeldWeapon(uint PlayerId, string TypeId, Vec3 Position, int Rarity = 0, string ItemId = "") : IMessage
{
    public MessageType Type => MessageType.HeldWeapon;
}

/// <summary>Stage of an enemy ability cast in an <see cref="AbilityEvent"/>.</summary>
public enum AbilityPhase : byte
{
    /// <summary>The cast starts at <c>Point</c> (the target's position).</summary>
    Begin = 0,
    /// <summary>A Laser locked its aim: the beam ends at <c>Point</c>.</summary>
    Lock = 1,
}

/// <summary>Host → each client: an enemy's active ability started (COS-79). Each machine plays the cast and resolves its effect
/// (damage, slow, root, pull) on its own player. <paramref name="Ability"/> is the ability's enum name, <paramref name="Seed"/>
/// places random parts (traps) the same everywhere, <paramref name="TargetIsYou"/> says the receiver is the one aimed at.</summary>
public sealed record AbilityEvent(ulong EnemyNetId, string Ability, AbilityPhase Phase, Vec3 Point, ulong Seed, bool TargetIsYou) : IMessage
{
    public MessageType Type => MessageType.AbilityEvent;
}

/// <summary>Player → everyone (the host relays with <paramref name="PlayerId"/> set), every few seconds: name, level, magic
/// find and position, for the party list, name tags and team magic find (COS-81).</summary>
public sealed record PlayerInfo(uint PlayerId, string Name, int Level, float MagicFind, Vec3 Position, float ExplosionDamage = 0) : IMessage
{
    public MessageType Type => MessageType.PlayerInfo;
}

public enum AnnounceKind : byte
{
    Joined = 1,
    Left = 2,
    Died = 3,
    LevelUp = 4,
}

/// <summary>A system line for every player (COS-81): someone joined, left, died or reached a level. Players send their own
/// deaths and level-ups to the host, which relays; joins and leaves come from the host.</summary>
public sealed record Announce(AnnounceKind Kind, uint PlayerId, string Name, int Level) : IMessage
{
    public MessageType Type => MessageType.Announce;
}
