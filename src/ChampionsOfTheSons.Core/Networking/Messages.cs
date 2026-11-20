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
    DropSpawned = 20,
    PickupRequest = 21,
    PickupGranted = 22,
    DropRemoved = 23,
    SpellCast = 30,
    AuraPulse = 31,
    Ping = 40,
    AdminGrant = 50,
    HeldWeapon = 51,
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

/// <summary>Host → clients: world settings that the host owns.</summary>
public sealed record WorldSettings(string DifficultyId, bool FriendlyFire) : IMessage
{
    public MessageType Type => MessageType.WorldSettings;
}

/// <summary>Client → host: send me this enemy's profile. <paramref name="EnemyNetId"/> is the packed Bolt network ID.</summary>
public sealed record EnemyProfileRequest(ulong EnemyNetId) : IMessage
{
    public MessageType Type => MessageType.EnemyProfileRequest;
}

/// <summary>Host → clients: the rolled profile of one enemy. Protocol 2 added the damage multiplier and the elite name ids
/// (empty strings: no name) for the scanner and nameplates (COS-68).</summary>
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
    string NameEpithetId = "") : IMessage
{
    public MessageType Type => MessageType.EnemySync;

    public bool Equals(EnemySync? other) =>
        other is not null && EnemyNetId == other.EnemyNetId && KindId == other.KindId && Level == other.Level &&
        Tier == other.Tier && MaxHealth.Equals(other.MaxHealth) && Health.Equals(other.Health) &&
        Armor.Equals(other.Armor) && Bounty == other.Bounty && AffixIds.SequenceEqual(other.AffixIds) &&
        DamageMult.Equals(other.DamageMult) && NameGivenId == other.NameGivenId && NameEpithetId == other.NameEpithetId;

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

/// <summary>Host → clients: an item dropped in the world.</summary>
public sealed record DropSpawned(uint DropId, ItemInstance Item, Vec3 Position) : IMessage
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

/// <summary>Caster → everyone: play this spell's visuals. <paramref name="Seed"/> keeps random visuals identical.</summary>
public sealed record SpellCast(uint PlayerId, string SpellId, Vec3 Origin, Vec3 Direction, ulong Seed) : IMessage
{
    public MessageType Type => MessageType.SpellCast;
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
public sealed record AuraPulse(uint PlayerId, string BuffId, Vec3 Origin, float Radius, float Seconds) : IMessage
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
/// I am, so receivers can find my body and show the model on my held game weapon (COS-93).</summary>
public sealed record HeldWeapon(uint PlayerId, string TypeId, Vec3 Position) : IMessage
{
    public MessageType Type => MessageType.HeldWeapon;
}
