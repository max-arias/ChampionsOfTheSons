using ChampionsOfTheSons.Core.Networking;
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

    private static float _posTimer;
    private static bool _posLogged;
    private static string? _castSpell;

    private static KeybindConfigEntry? _pingKey;

    /// <summary>Self-tests: number of pulses received that applied to the local player.</summary>
    public static int PulsesApplied { get; private set; }

    public void Init()
    {
        CotsNetEvent.Received += OnNet;
        Pings.Added += OnPingAdded;
        Pings.Expired += OnPingExpired;
        _pingKey = Config.Category.CreateKeybindEntry("ping_key", "h", "Ping", "Ping what you are looking at: every player sees the marker. Ping your own marker (or the sky) to clear it.");
        _pingKey.SetScope(needsPlayerControllable: true);
        _pingKey.Notify(PingLookedAt);
    }

    public void OnGameStart()
    {
        PingLabels.Build();
        Pings.Clear();
        foreach (var m in PingMarkers.Values) if (m != null) UnityEngine.Object.Destroy(m);
        PingMarkers.Clear();
        Auras.Clear();
    }

    public void Update(float dt, bool inWorld)
    {
        double now = Time.realtimeSinceStartup;
        Pings.Tick(now);
        PingLabels.Update(Pings, inWorld && Config.ShowHud.Value);
        foreach (var ping in Pings.Active)
            if (ping.Kind == PingKind.Enemy && PingMarkers.TryGetValue(ping.PlayerId, out var pillar) && pillar != null
                && PingLabels.Shown.ContainsKey(ping.PlayerId))
                FollowPillar(ping, pillar);
        Auras.Prune(now);
        if (!CoopSession.Active || !inWorld) return;

        if (CoopSession.IsClient && CoopFeature.Accepted)
        {
            _posTimer -= dt;
            if (_posTimer <= 0)
            {
                _posTimer = 1f;
                if (!_posLogged) { _posLogged = true; RLog.Msg($"{CoopTestJoin.Tag} client: reporting position to the host"); }
                CoopSession.SendToHost(new PlayerPos(CoopSession.ToVec(LocalPlayer.Transform.position), PlayerState.Character.Progress.Level));
            }
        }

    }

    // ---------------------------------------------------------------- pings

    /// <summary>The local player's id on the ping board: 0 for the host and alone (COS-98: pings work in single player).
    /// A client does not know the id the host gives it, so its own marker sits under a local-only id (the host rewrites the
    /// id when it relays, and never sends a client its own ping back).
    public static uint LocalPingId => CoopSession.IsClient ? uint.MaxValue : CoopSession.HostId;

    private static void PingLookedAt()
    {
        if (!LocalPlayer.IsInWorld) return;
        var cam = LocalPlayer.MainCam != null ? LocalPlayer.MainCam : Camera.main;
        if (cam == null) return;
        var ray = new Ray(cam.transform.position, cam.transform.forward);
        var kind = PingKind.Location;
        ulong target = 0;
        Vector3? at = null;
        Sons.Ai.Vail.VailActor? actor = null;
        if (Physics.Raycast(ray, out var hit, 300f, ~0, QueryTriggerInteraction.Ignore))
        {
            at = hit.point;
            actor = hit.collider.GetComponentInParent<Sons.Ai.Vail.VailActor>();
            if (actor != null)
            {
                kind = PingKind.Enemy;
                target = EnemySyncFeature.NetIdOf(actor);
                if (target == 0) target = (ulong)actor.Pointer.ToInt64(); // alone: no net id, the actor is the target
            }
            else
            {
                var near = LootFeature.Ground.Near(hit.point.x, hit.point.y, hit.point.z, 2f);
                if (near.Count > 0) { kind = PingKind.Item; target = (ulong)near[0]; }
            }
        }
        var press = Pings.PressKind(LocalPingId, kind, at is { } p ? CoopSession.ToVec(p) : null, target);
        if (press == null) return;
        if (press == PingKind.Enemy) PingLabels.LocalTarget = actor;
        SendPing(press.Value, at ?? LocalPlayer.Transform.position, press == PingKind.Clear ? 0 : target);
    }

    public static void SendPing(PingKind kind, Vector3 at, ulong target)
    {
        var ping = new Ping(LocalPingId, kind, CoopSession.ToVec(at), target);
        if (CoopSession.Active)
        {
            if (CoopSession.IsHost) CoopSession.SendToPeers(ping);
            else CoopSession.SendToHost(ping);
        }
        Pings.Add(ping, Time.realtimeSinceStartup); // our own marker too
    }

    /// <summary>An enemy ping's pillar stands over the enemy as it moves.</summary>
    private static void FollowPillar(Ping ping, GameObject pillar)
    {
        var a = EnemySyncFeature.ActorOf(ping.TargetId) ?? (ping.PlayerId == LocalPingId ? PingLabels.LocalTarget : null);
        if (a != null) pillar.transform.position = a.transform.position + Vector3.up * 3f;
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

    // ---------------------------------------------------------------- ping self-test (single player, COS-98)

    private static Sons.Ai.Vail.VailActor? _pinged;

    public void RegisterSelfTests()
    {
        SelfTest.Add(0.5f, "pings: unpause", TestKit.Unpause);
        SelfTest.Add(1f, "pings: enemy ping alone", () =>
        {
            Pings.Clear();
            _pinged = TestKit.Spawn(distance: 10, rules: WorldState.Rules with { AllowElites = false });
            SelfTest.Check("pings: enemy spawned", _pinged != null);
            if (_pinged == null) return;
            PingLabels.LocalTarget = _pinged;
            SendPing(PingKind.Enemy, _pinged.transform.position, (ulong)_pinged.Pointer.ToInt64());
            SelfTest.Check("pings: a ping works without a co-op session", Pings.Of(LocalPingId) is { Kind: PingKind.Enemy });
        });
        SelfTest.Add(1.5f, "pings: enemy label", () =>
        {
            PingLabels.Shown.TryGetValue(LocalPingId, out var text);
            var name = _pinged != null && Bridge.EnemyRegistry.Get(_pinged) is { } r ? Hud.HudText.KindTitle(r.Profile) : "?";
            SelfTest.Check("pings: the enemy ping shows the enemy's name", text != null && (text.Contains(name) || text.Contains("Lv")), text ?? "no label");
            TestKit.Screenshot("pings-01-enemy");
            if (_pinged != null) TestKit.Kill(_pinged);
        });
        SelfTest.Add(3f, "pings: ends with the enemy", () =>
        {
            SelfTest.Check("pings: an enemy ping ends when the enemy dies", Pings.Of(LocalPingId) == null);
            var at = LocalPlayer.Transform.position + LocalPlayer.Transform.forward * 8;
            SendPing(PingKind.Location, at, 0);
        });
        SelfTest.Add(3.5f, "pings: location label and clear", () =>
        {
            PingLabels.Shown.TryGetValue(LocalPingId, out var text);
            SelfTest.Check("pings: a location ping shows the distance", text != null && text.Contains(" m"), text ?? "no label");
            var own = Pings.Of(LocalPingId);
            var press = own == null ? null : Pings.PressKind(LocalPingId, PingKind.Location, own.Position, 0);
            if (press is { } k) SendPing(k, CoopSession.ToUnity(own!.Position), 0);
            SelfTest.Check("pings: pinging your own marker clears it", press == PingKind.Clear && Pings.Of(LocalPingId) == null && !PingMarkers.ContainsKey(LocalPingId));
        });
    }

    // ---------------------------------------------------------------- auras

    /// <summary>True while a teammate's aura of this buff reaches the local player.</summary>
    public static bool IsBuffed(string buffId) => Auras.IsActive(buffId, Time.realtimeSinceStartup);

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
            var sync = Spells.SpellCaster.LastSync;
            if (sync != null && sync.SpellId == _castSpell)
                SendSpell(_castSpell, LocalPlayer.Transform.position, cam.forward, sync.ToggledOn ? SpellCastMode.On : SpellCastMode.Cast, sync.CastId, sync.Shapes);
            else
                SendSpell(_castSpell, LocalPlayer.Transform.position, cam.forward);
        }
        catch (Exception e)
        {
            RLog.Warning($"{CoopTestJoin.Tag} spell broadcast failed: {e.Message}");
        }
    }

    public static void SendSpell(string spellId, Vector3 origin, Vector3 direction, SpellCastMode mode = SpellCastMode.Cast, uint castId = 0,
        IReadOnlyList<FxShape>? shapes = null)
    {
        var cast = new SpellCast(CoopSession.HostId, spellId, CoopSession.ToVec(origin), CoopSession.ToVec(direction), (ulong)Environment.TickCount64,
            mode, castId, shapes ?? Array.Empty<FxShape>());
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
                    RLog.Msg($"{CoopTestJoin.Tag} client: saw player {cast.PlayerId} cast {cast.SpellId} ({cast.Mode}, {cast.AllShapes.Count} visual(s))");
                    break;

                case SpellFxEvent ev when CoopSession.IsHost && CoopFeature.Verified.Contains(from):
                    var re = ev with { PlayerId = CoopSession.IdOf(from) };
                    CoopSession.SendToPeers(re, except: from);
                    CoopFx.ShowUpdate(re);
                    break;
                case SpellFxEvent ev when CoopSession.IsClient:
                    CoopFx.ShowUpdate(ev);
                    break;
            }
        }
        catch (Exception e)
        {
            RLog.Error($"{CoopTestJoin.Tag} extras handler failed: {e}");
        }
    }

    /// <summary>Team spells (COS-23): the caster's War Cry, Purge, Healing Dome and Sun Flare reach teammates within the radius.</summary>
    public static void SendTeamSpell(string spellId, Vector3 origin, float radius, float seconds, double amount, double amount2 = 0)
    {
        if (!CoopSession.Active || (CoopSession.IsClient && !CoopFeature.Accepted)) return;
        CoopSession.Broadcast(new AuraPulse(CoopSession.HostId, spellId, CoopSession.ToVec(origin), radius, seconds, (float)amount, (float)amount2));
    }

    public static int TeamSpellsApplied { get; private set; }

    private static void ApplyPulse(AuraPulse pulse)
    {
        if (!LocalPlayer.IsInWorld) return;
        if (pulse.BuffId.StartsWith("spell.", StringComparison.Ordinal))
        {
            if (Vector3.Distance(LocalPlayer.Transform.position, CoopSession.ToUnity(pulse.Origin)) <= pulse.Radius && Spells.TeamSpells.Apply(pulse))
                TeamSpellsApplied++;
            return;
        }
        if (Auras.Apply(pulse, CoopSession.ToVec(LocalPlayer.Transform.position), Time.realtimeSinceStartup)) PulsesApplied++;
    }
}
