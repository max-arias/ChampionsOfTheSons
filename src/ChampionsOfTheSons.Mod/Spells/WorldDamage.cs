using ChampionsOfTheSons.Core.Networking;
using ChampionsOfTheSons.Core.Rules;
using ChampionsOfTheSons.Mod.Bridge;
using RedLoader;
using Sons.Ai.Vail;
using Sons.Crafting.Structures;
using Sons.Gameplay;
using Sons.Gameplay.TreeCutting;
using TheForest.Utils;
using UnityEngine;

namespace ChampionsOfTheSons.Mod.Spells;

/// <summary>
/// Spell damage on everything that is not a tracked enemy (COS-213; Max, 2026-11-24: "every spell that does damage should
/// damage everything"): animals and other untracked actors (the companions too), breakables (bushes, plants, crates), trees
/// (a health pool per tree, <see cref="WorldDamageRules"/>) and player-built structures. Host and single player only: a
/// co-op client's spells do not touch the host's world.
/// </summary>
internal static class WorldDamage
{
    /// <summary>What the last <see cref="Area"/> call hit (self-tests, logs).</summary>
    public static (int Actors, int Breakables, int Trees, int Felled, int Structures) Last { get; private set; }

    private static readonly Dictionary<int, (double Left, float Time)> TreePools = new();
    private static readonly HashSet<int> Seen = new();
    private static readonly HashSet<string> Warned = new();

    /// <summary>The world is this machine's to change (single player or host; a dedicated server has no local player).</summary>
    private static bool Allowed => EnemyRegistry.IsAuthority;

    /// <summary>A co-op client: its spells' world damage goes to the host as <see cref="WorldHit"/> (COS-213).</summary>
    private static bool Forwards => Net.CoopHits.ForwardsToHost;

    /// <summary>World hits this client sent to the host (co-op test).</summary>
    public static int Forwarded { get; private set; }

    private static void Send(WorldHitKind kind, Vector3 center, float radius, double amount, Vector3 target = default, float seconds = 0)
    {
        if (!(amount > 0) || double.IsInfinity(amount)) return;
        Net.CoopSession.SendToHost(new WorldHit(kind, Net.CoopSession.ToVec(center), radius, (float)Math.Min(amount, float.MaxValue / 2),
            Net.CoopSession.ToVec(target), seconds));
        Forwarded++;
    }

    // ---------------------------------------------------------------- actors

    /// <summary>Live actors near a point that are not tracked enemies: animals, the companions.</summary>
    public static List<VailActor> ActorsWithin(Vector3 center, float radius)
    {
        var found = new List<VailActor>();
        try
        {
            var active = VailActorManager.GetActiveActors();
            if (active == null) return found;
            var self = LocalPlayer.Transform != null ? LocalPlayer.Transform.root : null;
            for (int i = 0; i < active.Count; i++)
            {
                var a = active[i];
                if (a == null || !a.gameObject.activeInHierarchy || a.IsDead() || EnemyRegistry.Get(a) != null) continue;
                if (a.TypeId is VailActorTypeId.PlayerRobby or VailActorTypeId.None) continue;   // players' own stand-ins
                if (self != null && a.transform.IsChildOf(self)) continue;
                if (Vector3.Distance(a.transform.position + Vector3.up * 0.5f, center) <= radius + 0.5f) found.Add(a);
            }
        }
        catch (Exception e)
        {
            Warn("actors", e);
        }
        return found;
    }

    /// <summary>A spell hit on an untracked actor, through the game's own damage call; a kill counts for the local player.</summary>
    public static void Strike(VailActor actor, double amount, Vector3 from)
    {
        if (actor == null || amount <= 0) return;
        if (Forwards)
        {
            // The host finds the same animal by place: a small sphere around it.
            try { if (!actor.IsDead()) Send(WorldHitKind.Area, actor.transform.position + Vector3.up * 0.5f, 0.8f, amount); }
            catch (Exception e) { Warn("forward", e); }
            return;
        }
        if (!Allowed) return;
        try
        {
            if (actor.IsDead()) return;
            World.WorldXpFeature.MarkPlayerHit(actor);
            var dir = actor.transform.position - from;
            dir.y = 0;
            actor.ReceivedDamage(null, ImpactType.Melee, 0, null, (float)amount, true, false, false, dir.sqrMagnitude > 1e-4f ? dir.normalized : Vector3.forward);
            // The damage call alone does not start the death sequence (its usual caller does).
            if (!actor.IsDead() && actor.GetHealth() <= 0) actor.OnDeath();
        }
        catch (Exception e)
        {
            Warn("strike", e);
        }
    }

    // ---------------------------------------------------------------- area

    /// <summary>
    /// A spell's damage on the world inside a sphere: untracked actors take <paramref name="hit"/>, breakables break, trees lose
    /// health and fall (away from the centre, or toward <paramref name="pullTo"/>), structures take their share.
    /// </summary>
    public static void Area(Vector3 center, float radius, OutgoingHit hit, Vector3? pullTo = null)
    {
        if (radius <= 0 || hit.Amount <= 0) return;
        if (Forwards)
        {
            Send(pullTo == null ? WorldHitKind.Area : WorldHitKind.AreaToward, center, radius, hit.Amount, pullTo ?? default);
            return;
        }
        if (Allowed) Apply(center, radius, hit.Amount, pullTo, null);
    }

    /// <summary>Host: a client's spell on the world, applied as the host's own (COS-213). Refuses claims no spell makes.</summary>
    public static void ApplyRemote(WorldHit m, BoltConnection from)
    {
        if (!Allowed || !(m.Radius > 0) || m.Radius > MaxRemoteRadius || !(m.Amount > 0) || float.IsInfinity(m.Amount)) return;
        var center = Net.CoopSession.ToUnity(m.Center);
        switch (m.Kind)
        {
            case WorldHitKind.Area: Apply(center, m.Radius, m.Amount, null, from); break;
            case WorldHitKind.AreaToward: Apply(center, m.Radius, m.Amount, Net.CoopSession.ToUnity(m.Target), from); break;
            case WorldHitKind.Pull:
                if (m.Seconds > 0 && m.Seconds <= 1 && m.Amount <= 100) RemotePulls.Add((center, m.Radius, m.Amount, m.Seconds));
                break;
        }
        RemoteApplied++;
    }

    /// <summary>World hits from clients the host applied (co-op test).</summary>
    public static int RemoteApplied { get; private set; }

    private const float MaxRemoteRadius = 60;
    private static readonly List<(Vector3 Center, float Radius, float Speed, float Left)> RemotePulls = new();

    /// <summary>Host: clients' Black Hole pulls, each for the short time its message covers.</summary>
    public static void StepRemote(float dt)
    {
        for (int i = RemotePulls.Count - 1; i >= 0; i--)
        {
            var p = RemotePulls[i];
            PullHere(p.Center, p.Radius, p.Speed, dt);
            p.Left -= dt;
            if (p.Left <= 0) RemotePulls.RemoveAt(i);
            else RemotePulls[i] = p;
        }
    }

    private static void Apply(Vector3 center, float radius, double amount, Vector3? pullTo, BoltConnection? caster)
    {
        var hit = new OutgoingHit(amount, DamageKind.Magic, false, 0);
        HitPlayers(center, radius, amount, caster);
        int actors = 0, breakables = 0, trees = 0, felled = 0, structures = 0;
        foreach (var a in ActorsWithin(center, radius))
        {
            Strike(a, hit.Amount, center);
            actors++;
        }

        Seen.Clear();
        Collider[] found;
        try
        {
            found = Physics.OverlapSphere(center, radius, ~0, QueryTriggerInteraction.Collide);
        }
        catch (Exception e)
        {
            Warn("overlap", e);
            Last = (actors, 0, 0, 0, 0);
            return;
        }
        float now = Time.realtimeSinceStartup;
        foreach (var c in found)
        {
            if (c == null) continue;
            try
            {
                var tree = c.GetComponentInParent<TreeCutManager>();
                if (tree != null)
                {
                    if (!Seen.Add(tree.GetInstanceID())) continue;
                    trees++;
                    if (HitTree(tree, hit.Amount, pullTo ?? center, pullTo != null, now)) felled++;
                    continue;
                }
                var breakable = c.GetComponentInParent<BreakableObject>();
                if (breakable != null)
                {
                    if (!Seen.Add(breakable.GetInstanceID()) || breakable.IsBroken) continue;
                    breakable.BreakImmediate();
                    breakables++;
                    continue;
                }
                var screw = c.GetComponentInParent<ScrewStructureDestruction>();
                if (screw != null)
                {
                    if (!Seen.Add(screw.GetInstanceID())) continue;
                    screw.ProcessImpact((float)WorldDamageRules.Structure(hit.Amount), ImpactType.Explosion, center, radius);
                    structures++;
                    continue;
                }
                var built = c.GetComponentInParent<Construction.StructureDestruction>();
                if (built != null)
                {
                    if (!Seen.Add(built.GetInstanceID())) continue;
                    if (HitStructure(built, center, radius, hit.Amount)) structures++;
                }
            }
            catch (Exception e)
            {
                Warn("object", e);
            }
        }
        Last = (actors, breakables, trees, felled, structures);
        if (TreePools.Count > 64) Forget(now);
    }

    // ---------------------------------------------------------------- players (PvP)

    /// <summary>Player hits the host sent or took (self-tests, co-op test).</summary>
    public static int PlayerHitsSent { get; private set; }

    /// <summary>
    /// Host: other players inside the sphere take the spell's hit when the game's PvP damage is on (COS-213, Max 2026-11-24:
    /// co-op honours the base game's PvP setting): full on Normal, half on Reduced. The caster is never hit by their own spell.
    /// The host's own player is hit here; a client gets <see cref="PlayerSpellHit"/> and applies it through its own defenses.
    /// </summary>
    private static void HitPlayers(Vector3 center, float radius, double amount, BoltConnection? caster)
    {
        if (!Net.CoopSession.Active) return;
        double factor = PvpRules.Factor(WorldState.Pvp);
        if (factor <= 0) return;
        float dealt = (float)Math.Min(amount * factor, float.MaxValue / 2);
        uint casterId = caster != null ? Net.CoopSession.IdOf(caster) : Net.CoopSession.HostId;
        try
        {
            // The host's own player (not on a dedicated server), when a client cast it.
            if (caster != null && LocalPlayer.IsInWorld && LocalPlayer.Transform != null
                && Vector3.Distance(LocalPlayer.Transform.position + Vector3.up, center) <= radius + 0.5f)
            {
                TakePlayerHit(dealt);
                PlayerHitsSent++;
            }
            foreach (var conn in Net.CoopSession.Peers)
            {
                if (caster != null && conn.ConnectionId == caster.ConnectionId) continue;
                if (!Net.CoopSession.TryPosition(conn, out var pos)) continue;
                if (Vector3.Distance(Net.CoopSession.ToUnity(pos) + Vector3.up, center) > radius + 0.5f) continue;
                Net.CotsNetEvent.Instance.SendTo(new PlayerSpellHit(casterId, dealt), conn);
                PlayerHitsSent++;
            }
        }
        catch (Exception e)
        {
            Warn("players", e);
        }
    }

    /// <summary>A teammate's spell hit the local player: magic damage through the player's own defenses (the enemy-magic path).</summary>
    public static void TakePlayerHit(double amount)
    {
        if (amount <= 0 || !LocalPlayer.IsInWorld || LocalPlayer.Stats == null) return;
        try
        {
            LocalPlayer.Stats.Hit((float)amount, isDemonic: true);
        }
        catch (Exception e)
        {
            Warn("player hit", e);
        }
    }

    /// <summary>Spell damage on one tree: its pool goes down, and at zero the tree falls. True when it fell.</summary>
    private static bool HitTree(TreeCutManager tree, double amount, Vector3 point, bool toward, float now)
    {
        int id = tree.GetInstanceID();
        double left = TreePools.TryGetValue(id, out var pool) && now - pool.Time <= WorldDamageRules.TreeForgetSeconds ? pool.Left : WorldDamageRules.TreeHealth;
        if (left <= 0) return false; // already felled by a spell
        var (after, fell) = WorldDamageRules.TreeHit(left, amount);
        TreePools[id] = (after, fell ? float.MaxValue : now);
        if (!fell) return false;
        var at = tree.transform.position;
        var dir = toward ? point - at : at - point;
        dir.y = 0;
        tree.InstantCutForceFall(dir.sqrMagnitude > 1e-4f ? dir.normalized : Vector3.forward);
        World.WorldXpFeature.PaySpellFelledTree(at);
        return true;
    }

    /// <summary>A built structure takes the hit as an explosion, through the game's own impact call.</summary>
    private static bool HitStructure(Construction.StructureDestruction built, Vector3 center, float radius, double amount)
    {
        var data = new ExplosionImpactData(null, ImpactExplosionType.Bomb, (float)WorldDamageRules.Structure(amount), Vector3.zero, 1f, 0f, center, radius, null);
        built.Cast<IImpactReceiver>().OnImpact(null, data.Cast<IImpactData>());
        return true;
    }

    private static void Forget(float now)
    {
        foreach (var id in TreePools.Where(kv => kv.Value.Time != float.MaxValue && now - kv.Value.Time > WorldDamageRules.TreeForgetSeconds).Select(kv => kv.Key).ToList())
            TreePools.Remove(id);
    }

    // ---------------------------------------------------------------- pull (Black Hole)

    private static readonly List<Rigidbody> Loose = new();
    private static float _looseAt = -1;
    private static Vector3 _looseCenter;

    /// <summary>
    /// Black Hole's pull on the world: untracked actors slide toward the centre, and loose bodies (break debris, logs,
    /// felled trunks, sticks) lose their weight and drift in. Call every frame while the hole lives.
    /// </summary>
    public static void Pull(Vector3 center, float radius, float speed, float dt)
    {
        if (radius <= 0.1f || dt <= 0) return;
        if (Forwards)
        {
            // A message covers a quarter second of pull (a little more, so there is no gap between them).
            _pullSendTimer -= dt;
            if (_pullSendTimer <= 0)
            {
                _pullSendTimer = 0.25f;
                Send(WorldHitKind.Pull, center, radius, speed, seconds: 0.3f);
            }
            return;
        }
        if (Allowed) PullHere(center, radius, speed, dt);
    }

    private static float _pullSendTimer;

    private static void PullHere(Vector3 center, float radius, float speed, float dt)
    {
        foreach (var a in ActorsWithin(center, radius))
        {
            var to = center - a.transform.position;
            float d = to.magnitude;
            if (d > 2f) a.transform.position += to / d * Mathf.Min(speed * dt, d - 2f);
        }
        float now = Time.realtimeSinceStartup;
        if (now - _looseAt > 0.25f || Vector3.Distance(center, _looseCenter) > 1f)
        {
            _looseAt = now;
            _looseCenter = center;
            Loose.Clear();
            try
            {
                var self = LocalPlayer.Transform != null ? LocalPlayer.Transform.root : null;
                Seen.Clear();
                foreach (var c in Physics.OverlapSphere(center, radius, ~0, QueryTriggerInteraction.Ignore))
                {
                    var rb = c != null ? c.attachedRigidbody : null;
                    if (rb == null || rb.isKinematic || !Seen.Add(rb.GetInstanceID())) continue;
                    if (self != null && rb.transform.IsChildOf(self)) continue;
                    if (rb.GetComponentInParent<VailActor>() != null) continue;
                    Loose.Add(rb);
                }
            }
            catch (Exception e)
            {
                Warn("pull", e);
            }
        }
        foreach (var rb in Loose)
        {
            if (rb == null) continue;
            var to = center - rb.worldCenterOfMass;
            float d = to.magnitude;
            if (d < 0.5f) { rb.velocity *= 0.5f; continue; }
            // Weightless, and steered toward the centre at the pull speed (faster close in).
            var want = to / d * Mathf.Max(speed, 3f) * Mathf.Lerp(1.6f, 0.7f, Mathf.Clamp01(d / radius));
            rb.velocity = Vector3.Lerp(rb.velocity, want, Mathf.Clamp01(4f * dt));
            rb.AddForce(-Physics.gravity, ForceMode.Acceleration);
            if (rb.IsSleeping()) rb.WakeUp();
        }
    }

    /// <summary>Loose bodies the last <see cref="Pull"/> held (self-tests).</summary>
    public static int Pulled => Loose.Count(r => r != null);

    public static void Reset()
    {
        TreePools.Clear();
        Loose.Clear();
        RemotePulls.Clear();
        _looseAt = -1;
        Last = default;
    }

    private static void Warn(string where, Exception e)
    {
        if (Warned.Add(where + e.GetType().Name)) RLog.Warning($"[COTS] world damage ({where}): {e.Message}");
    }
}
