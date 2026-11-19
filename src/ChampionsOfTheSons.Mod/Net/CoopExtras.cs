using Bolt;
using ChampionsOfTheSons.Core.Networking;
using ChampionsOfTheSons.Core.Rules;
using ChampionsOfTheSons.Mod.Bridge;
using ChampionsOfTheSons.Mod.Loot;
using HarmonyLib;
using RedLoader;
using SonsSdk;
using TheForest.Utils;
using UnityEngine;
using Ping = ChampionsOfTheSons.Core.Networking.Ping;

namespace ChampionsOfTheSons.Mod.Net;

/// <summary>
/// Smaller co-op pieces: pings (COS-24), buffs from teammates' auras (COS-23), spell casts seen by others (COS-51)
/// and the position/level report clients send the host.
/// </summary>
[HarmonyPatch]
public sealed class CoopExtras : IModFeature
{
    // ---- pings (COS-24)
    public static PingBoard Pings { get; } = new();
    public static readonly Dictionary<uint, GameObject> PingMarkers = new();

    // ---- auras (COS-23)
    public static AuraBoard Auras { get; } = new();

    private sealed record Source(string BuffId, float Radius, Func<bool> IsActive);

    private static readonly List<Source> Sources = new();
    private static float _auraTimer, _posTimer;
    private static string? _castSpell;

    private static KeybindConfigEntry? _pingKey;

    /// <summary>Self-tests: number of pulses received that applied to the local player.</summary>
    public static int PulsesApplied { get; private set; }

    public void Init()
    {
        CotsNetEvent.Received += OnNet;
        Pings.Added += OnPingAdded;
        Pings.Expired += OnPingExpired;
        _pingKey = Config.Category.CreateKeybindEntry("ping_key", "v", "Ping", "Ping what you are looking at: every player sees the marker.");
        _pingKey.SetScope(needsPlayerControllable: true);
        _pingKey.Notify(PingLookedAt);
    }

    public void OnGameStart()
    {
        Pings.Clear();
        foreach (var m in PingMarkers.Values) if (m != null) UnityEngine.Object.Destroy(m);
        PingMarkers.Clear();
        Auras.Clear();
    }

    public void Update(float dt, bool inWorld)
    {
        double now = Time.realtimeSinceStartup;
        Pings.Tick(now);
        Auras.Prune(now);
        if (!CoopSession.Active || !inWorld) return;

        if (CoopSession.IsClient && CoopFeature.Accepted)
        {
            _posTimer -= dt;
            if (_posTimer <= 0)
            {
                _posTimer = 1f;
                CoopSession.SendToHost(new PlayerPos(CoopSession.ToVec(LocalPlayer.Transform.position), PlayerState.Character.Progress.Level));
            }
        }

        _auraTimer -= dt;
        if (_auraTimer <= 0)
        {
            _auraTimer = 1f;
            foreach (var s in Sources.ToList())
            {
                bool on;
                try { on = s.IsActive(); } catch { on = false; }
                if (on) SendPulse(s.BuffId, s.Radius, 2.5f);
            }
        }
    }

    // ---------------------------------------------------------------- pings

    private static void PingLookedAt()
    {
        if (!LocalPlayer.IsInWorld || !CoopSession.Active) return;
        var cam = LocalPlayer.MainCam != null ? LocalPlayer.MainCam : Camera.main;
        if (cam == null) return;
        var ray = new Ray(cam.transform.position, cam.transform.forward);
        if (!Physics.Raycast(ray, out var hit, 300f, ~0, QueryTriggerInteraction.Ignore)) return;
        var kind = PingKind.Location;
        ulong target = 0;
        var actor = hit.collider.GetComponentInParent<Sons.Ai.Vail.VailActor>();
        if (actor != null)
        {
            kind = PingKind.Enemy;
            target = EnemySyncFeature.NetIdOf(actor);
        }
        else
        {
            var near = LootFeature.Ground.Near(hit.point.x, hit.point.y, hit.point.z, 2f);
            if (near.Count > 0) { kind = PingKind.Item; target = (ulong)near[0]; }
        }
        SendPing(kind, hit.point, target);
    }

    public static void SendPing(PingKind kind, Vector3 at, ulong target)
    {
        var ping = new Ping(CoopSession.HostId, kind, CoopSession.ToVec(at), target);
        if (CoopSession.IsHost) CoopSession.SendToPeers(ping);
        else CoopSession.SendToHost(ping);
        Pings.Add(ping, Time.realtimeSinceStartup); // our own marker too
    }

    private static void OnPingAdded(Ping ping)
    {
        try
        {
            if (PingMarkers.TryGetValue(ping.PlayerId, out var old) && old != null) UnityEngine.Object.Destroy(old);
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = "CotsPing";
            var col = go.GetComponent<Collider>();
            if (col != null) UnityEngine.Object.Destroy(col);
            go.transform.position = CoopSession.ToUnity(ping.Position) + Vector3.up * 3f;
            go.transform.localScale = new Vector3(0.25f, 3f, 0.25f);
            var color = ping.Kind switch { PingKind.Enemy => new Color(1f, 0.25f, 0.2f), PingKind.Item => new Color(1f, 0.85f, 0.2f), _ => new Color(0.3f, 0.8f, 1f) };
            var r = go.GetComponent<Renderer>();
            if (r != null) r.material.color = color;
            PingMarkers[ping.PlayerId] = go;
        }
        catch (Exception e)
        {
            RLog.Warning($"{CoopTestJoin.Tag} ping marker failed: {e.Message}");
        }
    }

    private static void OnPingExpired(Ping ping)
    {
        if (PingMarkers.TryGetValue(ping.PlayerId, out var go))
        {
            if (go != null) UnityEngine.Object.Destroy(go);
            PingMarkers.Remove(ping.PlayerId);
        }
    }

    // ---------------------------------------------------------------- auras

    /// <summary>
    /// Registers an aura the local player can project: while <paramref name="isActive"/> says so, teammates within
    /// <paramref name="radius"/> metres get <paramref name="buffId"/> (pulses go out once a second and last 2.5 s).
    /// The perk code that owns the aura calls this once and applies the received buff with <see cref="IsBuffed"/>.
    /// </summary>
    public static void RegisterAura(string buffId, float radius, Func<bool> isActive) => Sources.Add(new Source(buffId, radius, isActive));

    /// <summary>True while a teammate's aura of this buff reaches the local player.</summary>
    public static bool IsBuffed(string buffId) => Auras.IsActive(buffId, Time.realtimeSinceStartup);

    public static void SendPulse(string buffId, float radius, float seconds)
    {
        if (!LocalPlayer.IsInWorld) return;
        var pulse = new AuraPulse(CoopSession.HostId, buffId, CoopSession.ToVec(LocalPlayer.Transform.position), radius, seconds);
        CoopSession.Broadcast(pulse);
    }

    // ---------------------------------------------------------------- spells (COS-51)

    // Spells are cast by the spells track; we only watch the result: a successful cast of the local player is sent to everyone.
    [HarmonyPatch(typeof(Spells.SpellCaster), nameof(Spells.SpellCaster.Cast))]
    [HarmonyPrefix]
    private static void BeforeCast(int slot)
    {
        var loadout = PlayerState.Character.Spells.Loadout;
        _castSpell = slot is >= 0 && slot < loadout.Count ? loadout[slot] as string : null;
    }

    [HarmonyPatch(typeof(Spells.SpellCaster), nameof(Spells.SpellCaster.Cast))]
    [HarmonyPostfix]
    private static void AfterCast(Spells.SpellCastOutcome __result)
    {
        try
        {
            if (__result != Spells.SpellCastOutcome.Cast || _castSpell == null || !CoopSession.Active || !LocalPlayer.IsInWorld) return;
            var cam = LocalPlayer.MainCam != null ? LocalPlayer.MainCam.transform : LocalPlayer.Transform;
            SendSpell(_castSpell, LocalPlayer.Transform.position, cam.forward);
        }
        catch (Exception e)
        {
            RLog.Warning($"{CoopTestJoin.Tag} spell broadcast failed: {e.Message}");
        }
    }

    public static void SendSpell(string spellId, Vector3 origin, Vector3 direction)
    {
        var cast = new SpellCast(CoopSession.HostId, spellId, CoopSession.ToVec(origin), CoopSession.ToVec(direction), (ulong)Environment.TickCount64);
        CoopSession.Broadcast(cast);
    }

    // ---------------------------------------------------------------- messages

    private static void OnNet(IMessage message, BoltConnection from)
    {
        try
        {
            switch (message)
            {
                case Ping ping when CoopSession.IsHost && CoopFeature.Verified.Contains(from):
                    var relayed = ping with { PlayerId = CoopSession.IdOf(from) };
                    CoopSession.SendToPeers(relayed, except: from);
                    Pings.Add(relayed, Time.realtimeSinceStartup);
                    break;
                case Ping ping when CoopSession.IsClient:
                    Pings.Add(ping, Time.realtimeSinceStartup);
                    break;

                case AuraPulse pulse when CoopSession.IsHost && CoopFeature.Verified.Contains(from):
                    var rp = pulse with { PlayerId = CoopSession.IdOf(from) };
                    CoopSession.SendToPeers(rp, except: from);
                    ApplyPulse(rp);
                    break;
                case AuraPulse pulse when CoopSession.IsClient:
                    ApplyPulse(pulse);
                    break;

                case SpellCast cast when CoopSession.IsHost && CoopFeature.Verified.Contains(from):
                    var rc = cast with { PlayerId = CoopSession.IdOf(from) };
                    CoopSession.SendToPeers(rc, except: from);
                    CoopFx.ShowSpell(rc);
                    break;
                case SpellCast cast when CoopSession.IsClient:
                    CoopFx.ShowSpell(cast);
                    RLog.Msg($"{CoopTestJoin.Tag} client: saw player {cast.PlayerId} cast {cast.SpellId}");
                    break;
            }
        }
        catch (Exception e)
        {
            RLog.Error($"{CoopTestJoin.Tag} extras handler failed: {e}");
        }
    }

    private static void ApplyPulse(AuraPulse pulse)
    {
        if (!LocalPlayer.IsInWorld) return;
        if (Auras.Apply(pulse, CoopSession.ToVec(LocalPlayer.Transform.position), Time.realtimeSinceStartup)) PulsesApplied++;
    }
}
