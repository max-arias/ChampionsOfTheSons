using ChampionsOfTheSons.Core.Networking;
using ChampionsOfTheSons.Mod.Bridge;
using ChampionsOfTheSons.Mod.Net;
using ChampionsOfTheSons.Mod.Spells;
using RedLoader;
using SonsSdk;
using TheForest.Utils;
using UnityEngine;

namespace ChampionsOfTheSons.Mod.Visuals;

/// <summary>
/// COS-106. Enemy state tints: a Shielding enemy turns dark while its shield is up, an Undead enemy green after it came back.
/// The host decides (it runs the enemies) and sends each change to clients (<see cref="EnemyState"/>). Player states others
/// see: Black Flame and Multishot at the hand, Berserk and Golden Skin at the feet, sent by each player (<see cref="PlayerStateNet"/>)
/// and drawn at that player's position (the hand: the nearest held item of theirs). Golden Skin also turns the local player's
/// body (the first-person arms) gold.
/// </summary>
public sealed class StateTintsFeature : IModFeature
{
    private const float Scan = 0.15f, Resend = 3f, Stale = 10f;

    public static readonly Color ShieldTint = new(0.22f, 0.22f, 0.3f);
    public static readonly Color UndeadTint = new(0.5f, 1f, 0.5f);
    public static readonly Color GoldTint = new(1f, 0.78f, 0.3f);

    private static readonly Dictionary<IntPtr, EnemyStateFlags> Shown = new();
    private static readonly Dictionary<ulong, EnemyStateFlags> FromHost = new();
    private static readonly Dictionary<IntPtr, EnemyStateFlags> Sent = new();
    private float _scan;

    private sealed class RemotePlayer
    {
        public PlayerStateFlags Flags;
        public Vector3 Position;
        public float Seen;
        public readonly Dictionary<PlayerStateFlags, GameObject> Fx = new();
    }

    private static readonly Dictionary<uint, RemotePlayer> Remotes = new();
    private static PlayerStateFlags _sentFlags;
    private static float _sendTimer;
    private static bool _goldOn;

    /// <summary>The tint an enemy shows now (self-test).</summary>
    public static EnemyStateFlags TintOf(Sons.Ai.Vail.VailActor actor) => Shown.GetValueOrDefault(actor.Pointer);

    /// <summary>Remote players with a state shown (self-test).</summary>
    public static int RemoteEffects => Remotes.Values.Sum(r => r.Fx.Count);

    public void Init() => CotsNetEvent.Received += OnNet;

    public void OnGameStart()
    {
        Shown.Clear();
        FromHost.Clear();
        Sent.Clear();
        foreach (var r in Remotes.Values) ClearFx(r);
        Remotes.Clear();
        _goldOn = false;
    }

    public void Update(float dt, bool inWorld)
    {
        if (!inWorld) return;
        _scan -= dt;
        if (_scan <= 0)
        {
            _scan = Scan;
            try { ScanEnemies(); } catch (Exception e) { RLog.Warning($"[COTS] enemy tints: {e.Message}"); }
        }
        try
        {
            LocalPlayerState(dt);
            DrawRemotes();
        }
        catch (Exception e)
        {
            RLog.Warning($"[COTS] player states: {e.Message}");
        }
    }

    // ---------------------------------------------------------------- enemies

    /// <summary>What an enemy's state is on this machine: computed on the host (or alone), received on a client.</summary>
    public static EnemyStateFlags StateOf(EnemyRuntime e)
    {
        if (CoopSession.IsClient)
            return e.Actor != null && FromHost.TryGetValue(EnemySyncFeature.NetIdOf(e.Actor), out var f) ? f : EnemyStateFlags.None;
        var flags = EnemyStateFlags.None;
        if (e.Combat.ShieldUp) flags |= EnemyStateFlags.ShieldUp;
        if (e.Revived) flags |= EnemyStateFlags.Revived;
        return flags;
    }

    private static void ScanEnemies()
    {
        foreach (var e in EnemyRegistry.Live)
        {
            if (e.Actor == null) continue;
            var key = e.Actor.Pointer;
            var flags = StateOf(e);
            if (CoopSession.IsHost && Sent.GetValueOrDefault(key) != flags)
            {
                Sent[key] = flags;
                ulong id = EnemySyncFeature.NetIdOf(e.Actor);
                if (id != 0) CoopSession.SendToPeers(new EnemyState(id, flags));
            }
            if (Shown.GetValueOrDefault(key) == flags) continue;
            Shown[key] = flags;
            Tint(e.Actor.gameObject, flags.HasFlag(EnemyStateFlags.ShieldUp) ? ShieldTint : flags.HasFlag(EnemyStateFlags.Revived) ? UndeadTint : null);
        }
    }

    private static MaterialPropertyBlock? _block;
    private static readonly int BaseColor = Shader.PropertyToID("_BaseColor"), PlainColor = Shader.PropertyToID("_Color");

    /// <summary>Multiplies every renderer's base colour (a property block, so the shared materials stay untouched); null clears.</summary>
    public static void Tint(GameObject root, Color? tint)
    {
        _block ??= new MaterialPropertyBlock();
        foreach (var r in root.GetComponentsInChildren<Renderer>(true)) // inactive LODs too
        {
            if (r == null || r is ParticleSystemRenderer || r.sharedMaterial == null) continue;
            if (r.gameObject.name.StartsWith("cots-", StringComparison.Ordinal) || r.gameObject.name.StartsWith("Fx_", StringComparison.Ordinal)) continue;
            if (tint == null)
            {
                r.SetPropertyBlock(null);
                continue;
            }
            _block.Clear();
            var m = r.sharedMaterial;
            if (m.HasProperty(BaseColor)) _block.SetColor(BaseColor, m.GetColor(BaseColor) * tint.Value);
            if (m.HasProperty(PlainColor)) _block.SetColor(PlainColor, m.GetColor(PlainColor) * tint.Value);
            r.SetPropertyBlock(_block);
        }
    }

    // ---------------------------------------------------------------- players

    public static PlayerStateFlags LocalFlags()
    {
        var flags = PlayerStateFlags.None;
        if (PlayerStateFx.ToggleOn("spell.black_flame")) flags |= PlayerStateFlags.BlackFlame;
        if (PlayerStateFx.ToggleOn("spell.multishot")) flags |= PlayerStateFlags.Multishot;
        if (SpellWorld.Buffs.Has("berserk")) flags |= PlayerStateFlags.Berserk;
        if (SpellWorld.Buffs.Has("golden_skin")) flags |= PlayerStateFlags.GoldenSkin;
        return flags;
    }

    private static void LocalPlayerState(float dt)
    {
        var flags = LocalFlags();
        bool gold = flags.HasFlag(PlayerStateFlags.GoldenSkin);
        if (gold != _goldOn && LocalPlayer.Transform != null)
        {
            _goldOn = gold;
            Tint(LocalPlayer.Transform.gameObject, gold ? GoldTint : null); // first-person arms and body
        }
        if (!CoopSession.Active || LocalPlayer.Transform == null) return;
        _sendTimer -= dt;
        if (flags == _sentFlags && (flags == PlayerStateFlags.None || _sendTimer > 0)) return;
        _sendTimer = Resend;
        _sentFlags = flags;
        CoopSession.Broadcast(new PlayerStateNet(0, flags, CoopSession.ToVec(LocalPlayer.Transform.position)));
    }

    private static void OnNet(IMessage message, BoltConnection from)
    {
        try
        {
            switch (message)
            {
                case EnemyState s when CoopSession.IsClient:
                    FromHost[s.EnemyNetId] = s.Flags;
                    break;
                case PlayerStateNet p:
                    if (CoopSession.IsHost)
                    {
                        if (!CoopFeature.Verified.Contains(from)) return;
                        p = p with { PlayerId = CoopSession.IdOf(from) };
                        CoopSession.SendToPeers(p, except: from);
                    }
                    if (!Remotes.TryGetValue(p.PlayerId, out var r)) Remotes[p.PlayerId] = r = new RemotePlayer();
                    r.Flags = p.Flags;
                    r.Position = CoopSession.ToUnity(p.Position);
                    r.Seen = Time.realtimeSinceStartup;
                    break;
            }
        }
        catch (Exception e)
        {
            RLog.Error($"[COTS] state message failed: {e}");
        }
    }

    private static void DrawRemotes()
    {
        if (Remotes.Count == 0 || !FxKit.Available) return;
        var hands = Weapons.HeldWeapons.Remote().ToList();
        foreach (var r in Remotes.Values)
        {
            var flags = Time.realtimeSinceStartup - r.Seen > Stale ? PlayerStateFlags.None : r.Flags;
            // The remote body moves between messages: follow the nearest held item of theirs when there is one.
            Transform? hand = null;
            float best = 4f;
            foreach (var h in hands)
            {
                float d = Vector3.Distance(h.Holder.position, r.Position);
                if (d < best) { best = d; hand = h.Holder; }
            }
            if (hand != null) r.Position = new Vector3(hand.position.x, r.Position.y, hand.position.z);
            foreach (PlayerStateFlags f in new[] { PlayerStateFlags.BlackFlame, PlayerStateFlags.Multishot, PlayerStateFlags.Berserk, PlayerStateFlags.GoldenSkin })
            {
                bool on = flags.HasFlag(f) && !(FxKit.Reduced && f is PlayerStateFlags.BlackFlame or PlayerStateFlags.Multishot);
                r.Fx.TryGetValue(f, out var go);
                if (on && go == null)
                {
                    go = f switch
                    {
                        PlayerStateFlags.BlackFlame => FxKit.Spawn(FxKit.Flame, r.Position, 0.14f, new Color(0.55f, 0.2f, 1f)),
                        PlayerStateFlags.Multishot => FxKit.Spawn(FxKit.Glyph, r.Position, 0.16f, SpellFx.Arcane),
                        PlayerStateFlags.Berserk => FxKit.Spawn(FxKit.Ring, r.Position, 0.9f, SpellFx.Blood),
                        _ => FxKit.Spawn(FxKit.Ring, r.Position, 0.75f, GoldTint),
                    };
                    if (go != null) r.Fx[f] = go;
                }
                else if (!on && go != null)
                {
                    UnityEngine.Object.Destroy(go);
                    r.Fx.Remove(f);
                }
                if (on && go != null)
                    go.transform.position = f is PlayerStateFlags.BlackFlame or PlayerStateFlags.Multishot
                        ? (hand != null ? hand.position : r.Position + Vector3.up * 1.3f)
                        : r.Position;
            }
        }
    }

    private static void ClearFx(RemotePlayer r)
    {
        foreach (var go in r.Fx.Values) if (go != null) UnityEngine.Object.Destroy(go);
        r.Fx.Clear();
    }

    // ---------------------------------------------------------------- self-test

    public void RegisterSelfTests()
    {
        Sons.Ai.Vail.VailActor? shielded = null, undead = null;
        SelfTest.Add(0.5f, "tints: unpause", TestKit.Unpause);
        SelfTest.Add(1f, "tints: spawn", () =>
        {
            var rules = WorldState.Rules with { AllowElites = false };
            shielded = TestKit.Spawn(distance: 6, sideways: -1.5f, rules: rules);
            undead = TestKit.Spawn(distance: 6, sideways: 1.5f, rules: rules);
            SelfTest.Check("tints: two enemies", shielded != null && undead != null);
        });
        SelfTest.Add(2f, "tints: raise shield, revive", () =>
        {
            if (shielded == null || undead == null) return;
            var s = EnemyRegistry.Get(shielded);
            var u = EnemyRegistry.Get(undead);
            if (s == null || u == null) return;
            s.Combat.ForceShieldForTest();
            u.Revived = true;
        });
        SelfTest.Add(2.6f, "tints: shown", () =>
        {
            if (shielded == null || undead == null) return;
            SelfTest.Check("tints: a raised shield darkens the enemy", TintOf(shielded).HasFlag(EnemyStateFlags.ShieldUp), TintOf(shielded).ToString());
            SelfTest.Check("tints: a revived Undead turns green", TintOf(undead) == EnemyStateFlags.Revived, TintOf(undead).ToString());
            TestKit.Screenshot("tints-01-shield-undead");
        });
        SelfTest.Add(3f, "tints: golden skin", () =>
        {
            SpellWorld.Buffs.Apply("golden_skin", 5, Array.Empty<Core.Rules.StatModifier>());
        });
        SelfTest.Add(3.4f, "tints: golden skin shown", () =>
        {
            SelfTest.Check("tints: Golden Skin sends and shows the gold state", LocalFlags().HasFlag(PlayerStateFlags.GoldenSkin) && _goldOn);
            SpellWorld.Buffs.Clear();
        });
        SelfTest.Add(4f, "tints: cleanup", () =>
        {
            foreach (var a in new[] { shielded, undead }) if (a != null) TestKit.Kill(a);
        });
    }
}
