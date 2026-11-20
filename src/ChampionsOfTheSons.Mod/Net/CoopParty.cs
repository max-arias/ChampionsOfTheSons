using Bolt;
using ChampionsOfTheSons.Core.Networking;
using ChampionsOfTheSons.Core.Rules;
using RedLoader;
using TheForest.Utils;
using UnityEngine;

namespace ChampionsOfTheSons.Mod.Net;

/// <summary>
/// COS-81: who is in the party. Every player sends <see cref="PlayerInfo"/> (name, level, magic find, position) every few
/// seconds and on a level change; the host relays it with the sender's id, so everyone knows every player's level (name tags,
/// the Character page's party list). System lines (<see cref="Announce"/>) tell everyone who joined, left, died or levelled up.
/// The host sums every player's magic find for drops (<see cref="KillCredit.TeamMagicFind"/>). In the Net namespace so a
/// dedicated server relays and sums too.
/// </summary>
public sealed class CoopParty : IModFeature
{
    private const float SendInterval = 3f;
    private const float StaleSeconds = 12f;

    public sealed class Member
    {
        public uint Id;
        public string Name = "";
        public int Level = 1;
        public float MagicFind = 1;
        public Vector3 Position;
        public float Seen;
    }

    private static readonly Dictionary<uint, Member> Members = new();
    private static readonly HashSet<uint> Announced = new();
    private static float _sendTimer, _pruneTimer;
    private static int _sentLevel;

    /// <summary>A system line arrived (or the host made one): the HUD shows it.</summary>
    public static event Action<Announce>? Line;

    /// <summary>Lines received or made here (co-op test).</summary>
    public static readonly List<Announce> Lines = new();

    /// <summary>The other players this machine knows, freshest first (not the local player).</summary>
    public static IEnumerable<Member> Others => Members.Values.Where(m => Time.realtimeSinceStartup - m.Seen < StaleSeconds).OrderBy(m => m.Id);

    public void Init()
    {
        CotsNetEvent.Received += OnNet;
        CoopSession.Left += OnLeft;
        PlayerState.Character.LeveledUp += r => Send(new Announce(AnnounceKind.LevelUp, CoopSession.HostId, LocalName, r.NewLevel), sendInfo: true);
        PlayerState.Character.Died += _ => Send(new Announce(AnnounceKind.Died, CoopSession.HostId, LocalName, PlayerState.Character.Progress.Level), sendInfo: false);
    }

    public void OnGameStart()
    {
        Members.Clear();
        Announced.Clear();
        Lines.Clear();
        _sentLevel = 0;
    }

    /// <summary>The local player's name: the game's player name, or a plain fallback.</summary>
    public static string LocalName
    {
        get
        {
            string n = CoopBodies.LocalName;
            return string.IsNullOrWhiteSpace(n) ? UI.UiText.T("ui.coop.player", "Player") : n;
        }
    }

    public static double LocalMagicFind
    {
        get
        {
            double mf = PlayerState.Character.Stats.Get(StatIds.MagicFind);
            return mf > 0 ? mf : 1;
        }
    }

    /// <summary>Magic find for a kill's drops: the local player's alone outside co-op; on a co-op host the sum over every player
    /// (the host only counts itself when it has a character in the world).</summary>
    public static double TeamMagicFind()
    {
        bool hostBody = LocalPlayer.IsInWorld && !ServerConfig.OnDedicatedServer;
        if (!CoopSession.IsHost || CoopFeature.Verified.Count == 0) return LocalMagicFind;
        var all = new List<double>();
        if (hostBody) all.Add(LocalMagicFind);
        foreach (var c in CoopSession.Peers)
            all.Add(Members.TryGetValue(CoopSession.IdOf(c), out var m) ? m.MagicFind : 1);
        return KillCredit.TeamMagicFind(all);
    }

    public void Update(float dt, bool inWorld)
    {
        if (!CoopSession.Active) return;
        if (CoopSession.IsHost)
        {
            _pruneTimer -= dt;
            if (_pruneTimer <= 0)
            {
                _pruneTimer = 2f;
                CoopFeature.PruneGone();
            }
        }
        if (!inWorld || LocalPlayer.Transform == null || ServerConfig.OnDedicatedServer) return;
        if (CoopSession.IsClient && !CoopFeature.Accepted) return;
        _sendTimer -= dt;
        int level = PlayerState.Character.Progress.Level;
        if (_sendTimer > 0 && level == _sentLevel) return;
        _sendTimer = SendInterval;
        _sentLevel = level;
        CoopSession.Broadcast(MyInfo());
    }

    private static PlayerInfo MyInfo() =>
        new(CoopSession.HostId, LocalName, PlayerState.Character.Progress.Level, (float)LocalMagicFind, CoopSession.ToVec(LocalPlayer.Transform.position));

    /// <summary>The local player's own line: shown here, sent to everyone (the host relays).</summary>
    private static void Send(Announce line, bool sendInfo)
    {
        try
        {
            if (!CoopSession.Active || ServerConfig.OnDedicatedServer) return;
            if (CoopSession.IsClient && !CoopFeature.Accepted) return;
            if (sendInfo && LocalPlayer.Transform != null) CoopSession.Broadcast(MyInfo());
            CoopSession.Broadcast(line);
            Show(line);
        }
        catch (Exception e)
        {
            RLog.Warning($"{CoopTestJoin.Tag} announce failed: {e.Message}");
        }
    }

    private static void Show(Announce line)
    {
        Lines.Add(line);
        if (Lines.Count > 50) Lines.RemoveAt(0);
        RLog.Msg($"{CoopTestJoin.Tag} line: {line.Kind} {line.Name} (player {line.PlayerId}, level {line.Level})");
        try { Line?.Invoke(line); }
        catch (Exception e) { RLog.Warning($"{CoopTestJoin.Tag} line display failed: {e.Message}"); }
    }

    private static void Store(PlayerInfo info)
    {
        if (!Members.TryGetValue(info.PlayerId, out var m)) Members[info.PlayerId] = m = new Member { Id = info.PlayerId };
        m.Name = info.Name.Length > 32 ? info.Name[..32] : info.Name;
        m.Level = System.Math.Clamp(info.Level, 1, 100000);
        m.MagicFind = float.IsFinite(info.MagicFind) ? Mathf.Clamp(info.MagicFind, 0, 1000) : 1;
        m.Position = CoopSession.ToUnity(info.Position);
        m.Seen = Time.realtimeSinceStartup;
    }

    private static void OnLeft(BoltConnection connection)
    {
        uint id = CoopSession.IdOf(connection);
        Members.TryGetValue(id, out var m);
        Members.Remove(id);
        if (!Announced.Remove(id)) return; // never fully joined: no line
        var line = new Announce(AnnounceKind.Left, id, m?.Name ?? "", m?.Level ?? 0);
        CoopSession.SendToPeers(line);
        if (!ServerConfig.OnDedicatedServer) Show(line);
    }

    private static void OnNet(IMessage message, BoltConnection from)
    {
        try
        {
            switch (message)
            {
                case PlayerInfo info when CoopSession.IsHost && CoopFeature.Verified.Contains(from):
                {
                    uint id = CoopSession.IdOf(from);
                    var relayed = info with { PlayerId = id };
                    Store(relayed);
                    CoopSession.SetLevel(from, relayed.Level);
                    CoopSession.SendToPeers(relayed, except: from);
                    if (Announced.Add(id))
                    {
                        // First word from this player: tell everyone, and tell them who is already here.
                        var joined = new Announce(AnnounceKind.Joined, id, Members[id].Name, Members[id].Level);
                        CoopSession.SendToPeers(joined, except: from);
                        if (!ServerConfig.OnDedicatedServer)
                        {
                            Show(joined);
                            if (LocalPlayer.Transform != null) CotsNetEvent.Instance.SendTo(MyInfo(), from);
                        }
                        foreach (var other in Members.Values.Where(o => o.Id != id).ToList())
                            CotsNetEvent.Instance.SendTo(new PlayerInfo(other.Id, other.Name, other.Level, other.MagicFind, CoopSession.ToVec(other.Position)), from);
                    }
                    break;
                }
                case PlayerInfo info when CoopSession.IsClient:
                    Store(info);
                    break;
                case Announce line when CoopSession.IsHost && CoopFeature.Verified.Contains(from):
                {
                    // Clients only speak for themselves, and only about dying or levelling up.
                    if (line.Kind is not (AnnounceKind.Died or AnnounceKind.LevelUp)) break;
                    uint id = CoopSession.IdOf(from);
                    var relayed = line with { PlayerId = id, Name = Members.TryGetValue(id, out var m) ? m.Name : line.Name };
                    CoopSession.SendToPeers(relayed, except: from);
                    if (!ServerConfig.OnDedicatedServer) Show(relayed);
                    break;
                }
                case Announce line when CoopSession.IsClient:
                    Show(line);
                    break;
            }
        }
        catch (Exception e)
        {
            RLog.Error($"{CoopTestJoin.Tag} party handler failed: {e}");
        }
    }
}
