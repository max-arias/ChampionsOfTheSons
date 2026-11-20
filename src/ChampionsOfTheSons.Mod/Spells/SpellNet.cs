using ChampionsOfTheSons.Core.Networking;
using ChampionsOfTheSons.Mod.Net;
using RedLoader;
using UnityEngine;

namespace ChampionsOfTheSons.Mod.Spells;

/// <summary>
/// COS-80: per-spell cast sync. While a cast (or a later step of it: a bolt's impact, a toggle's upkeep) draws through
/// <see cref="SpellFx"/>, every visual is recorded with its place, size, colour, lifetime, motion and whether it rides on the
/// caster. The cast goes out as one <see cref="SpellCast"/> carrying those shapes; later shapes, and shapes that end early
/// (a bolt that hit, a toggle switched off), follow as <see cref="SpellFxEvent"/>s, flushed once per frame. Receivers draw the
/// same shapes (<see cref="Draw"/>), so a teammate sees each spell's real effect at the right place, size and duration.
/// Damage stays with the caster's machine; Portal pairs opened by a teammate also carry the local player.
/// </summary>
internal static class SpellNet
{
    private sealed class Pending
    {
        public readonly List<FxShape> Shapes = new();
        public readonly List<byte> Stop = new();
        public Vec3 At;
    }

    // ---- caster side
    private static uint _nextCastId = 1;
    private static readonly Dictionary<uint, int> NextIndex = new();
    private static readonly Dictionary<uint, Pending> Out = new();
    private static readonly Dictionary<IntPtr, (uint Cast, byte Index)> Known = new();
    private static readonly Dictionary<string, uint> Toggles = new();

    /// <summary>The cast whose visuals are being drawn now (null: not synced).</summary>
    public static uint? Context { get; set; }

    // ---- receiver side
    private static readonly Dictionary<(uint Player, uint Cast), List<GameObject?>> Remote = new();

    /// <summary>Remote shapes drawn so far (co-op test).</summary>
    public static int RemoteShapesDrawn { get; private set; }

    /// <summary>Remote shapes ended early by their caster (co-op test).</summary>
    public static int RemoteShapesStopped { get; private set; }

    public static void Init()
    {
        CoopFx.DrawCast = DrawCast;
        CoopFx.DrawUpdate = DrawUpdate;
    }

    public static void Reset()
    {
        Context = null;
        NextIndex.Clear();
        Out.Clear();
        Known.Clear();
        Toggles.Clear();
        foreach (var list in Remote.Values)
            foreach (var go in list) SpellFx.Remove(go);
        Remote.Clear();
    }

    // ================================================================= caster side

    /// <summary>Starts recording a new cast; returns its id.</summary>
    public static uint BeginCast()
    {
        uint id = _nextCastId++;
        if (_nextCastId == 0) _nextCastId = 1;
        Context = id;
        NextIndex[id] = 0;
        return id;
    }

    /// <summary>Stops recording and hands back what the cast drew (sent with the <see cref="SpellCast"/>).</summary>
    public static List<FxShape> EndCast(uint id)
    {
        Context = null;
        return Out.Remove(id, out var p) ? p.Shapes : new List<FxShape>();
    }

    /// <summary>A toggle or channeled spell switched on in this cast: its upkeep visuals belong to the cast.</summary>
    public static void ToggleOn(string spellId, uint castId) => Toggles[spellId] = castId;

    public static uint? ToggleCast(string spellId) => Toggles.TryGetValue(spellId, out var id) ? id : null;

    /// <summary>A toggle or channeled spell stopped: tell the others (its visuals stop with it).</summary>
    public static void ToggleOff(string spellId)
    {
        if (!Toggles.Remove(spellId, out var castId) || !CoopSession.Active) return;
        var pos = SpellWorld.PlayerPos;
        CoopSession.Broadcast(new SpellCast(CoopSession.HostId, spellId, CoopSession.ToVec(pos), CoopSession.ToVec(Vector3.forward), 0,
            SpellCastMode.Off, castId, Array.Empty<FxShape>()));
    }

    /// <summary>SpellFx drew <paramref name="go"/>: record it under the current cast.</summary>
    public static void Recorded(GameObject go, FxShape shape)
    {
        if (Context is not uint cast || !CoopSession.Active) return;
        int index = NextIndex.GetValueOrDefault(cast);
        if (!Out.TryGetValue(cast, out var p)) Out[cast] = p = new Pending();
        if (index > byte.MaxValue || p.Shapes.Count >= MessageCodec.MaxShapes) return;
        NextIndex[cast] = index + 1;
        p.Shapes.Add(shape);
        Known[go.Pointer] = (cast, (byte)index);
    }

    /// <summary>The caster changed a recorded visual's lifetime or motion right after drawing it (zones, bolts, portals).</summary>
    public static void NoteMotion(GameObject? go, float seconds, Vector3 velocity)
    {
        if (go == null || !Known.TryGetValue(go.Pointer, out var k) || !Out.TryGetValue(k.Cast, out var p)) return;
        int first = NextIndex.GetValueOrDefault(k.Cast) - p.Shapes.Count;
        int i = k.Index - first;
        if (i < 0 || i >= p.Shapes.Count) return;
        p.Shapes[i] = p.Shapes[i] with { Seconds = seconds, Velocity = CoopSession.ToVec(velocity) };
    }

    /// <summary>A recorded visual was removed before its time ran out: the others remove theirs too.</summary>
    public static void Removed(GameObject go)
    {
        if (!Known.Remove(go.Pointer, out var k) || !CoopSession.Active) return;
        if (!Out.TryGetValue(k.Cast, out var p)) Out[k.Cast] = p = new Pending();
        if (p.Stop.Count == 0) p.At = CoopSession.ToVec(go.transform.position);
        if (p.Stop.Count < MessageCodec.MaxShapes) p.Stop.Add(k.Index);
    }

    /// <summary>A recorded visual ran its time out: nothing to send.</summary>
    public static void Expired(GameObject? go)
    {
        if (go != null) Known.Remove(go.Pointer);
    }

    /// <summary>Once per frame: sends what running casts drew or stopped since the last frame.</summary>
    public static void Flush()
    {
        if (Out.Count == 0) return;
        foreach (var (cast, p) in Out.ToList())
        {
            if (cast == Context) continue; // still being cast: goes out with the SpellCast
            Out.Remove(cast);
            if (p.Shapes.Count == 0 && p.Stop.Count == 0) continue;
            CoopSession.Broadcast(new SpellFxEvent(CoopSession.HostId, cast, p.Stop.ToArray(), p.At, p.Shapes.ToArray()));
        }
    }

    // ================================================================= receiver side

    /// <summary>Longest a teammate's visual may stay without news from them (a toggle whose Off was lost).</summary>
    private const float MaxRemoteSeconds = 600f;

    private static void DrawCast(SpellCast cast)
    {
        var key = (cast.PlayerId, cast.CastId);
        if (cast.Mode == SpellCastMode.Off)
        {
            if (Remote.Remove(key, out var gone))
                foreach (var go in gone) { SpellFx.Remove(go); RemoteShapesStopped++; }
            return;
        }
        var origin = CoopSession.ToUnity(cast.Origin);
        var body = CasterBody(origin);
        var list = new List<GameObject?>();
        foreach (var s in cast.AllShapes) list.Add(Draw(s, body));
        Remote[key] = list;
        if (cast.SpellId == "spell.portal") OpenSharedPortal(cast.AllShapes, list);
        PruneRemote();
    }

    private static void DrawUpdate(SpellFxEvent ev)
    {
        var key = (ev.PlayerId, ev.CastId);
        if (!Remote.TryGetValue(key, out var list)) Remote[key] = list = new List<GameObject?>();
        foreach (var i in ev.Stop)
        {
            if (i >= list.Count) continue;
            SpellFx.Remove(list[i]);
            list[i] = null;
            RemoteShapesStopped++;
        }
        if (ev.Shapes.Count == 0) return;
        var body = CasterBody(CoopSession.ToUnity(ev.At));
        foreach (var s in ev.Shapes) list.Add(Draw(s, body));
    }

    /// <summary>Draws one teammate's shape the way the caster drew it.</summary>
    public static GameObject? Draw(FxShape s, Transform? body)
    {
        var pos = CoopSession.ToUnity(s.Position);
        var color = new Color(((s.Rgb >> 16) & 0xFF) / 255f, ((s.Rgb >> 8) & 0xFF) / 255f, (s.Rgb & 0xFF) / 255f);
        float seconds = Mathf.Min(s.Seconds, MaxRemoteSeconds);
        var follow = s.Follow ? body : null;
        var look = (SpellFx.Look)s.Look;
        GameObject go = s.Kind switch
        {
            FxKind.Disc => SpellFx.Disc(pos, s.Radius, color, seconds, follow, look),
            FxKind.Column => SpellFx.Column(pos, s.Radius, s.Height, color, seconds),
            FxKind.Portal => SpellFx.Portal(pos, CoopSession.ToUnity(s.Facing), color, seconds),
            _ => SpellFx.Sphere(pos, s.Radius, color, seconds, follow, look),
        };
        var v = CoopSession.ToUnity(s.Velocity);
        if (v != Vector3.zero) SpellFx.SetVelocity(go, v);
        RemoteShapesDrawn++;
        return go;
    }

    private static void OpenSharedPortal(IReadOnlyList<FxShape> shapes, List<GameObject?> drawn)
    {
        int a = -1, b = -1;
        for (int i = 0; i < shapes.Count; i++)
        {
            if (shapes[i].Kind != FxKind.Portal) continue;
            if (a < 0) a = i;
            else if (b < 0) b = i;
        }
        if (a < 0 || b < 0) return;
        SpellPortals.OpenShared(CoopSession.ToUnity(shapes[a].Position), CoopSession.ToUnity(shapes[b].Position),
            Mathf.Min(shapes[a].Seconds, MaxRemoteSeconds), drawn[a], drawn[b]);
    }

    /// <summary>Forgets casts whose visuals are all gone.</summary>
    private static void PruneRemote()
    {
        foreach (var (key, list) in Remote.ToList())
            if (list.All(go => go == null)) Remote.Remove(key);
    }

    /// <summary>The body of the teammate who cast at <paramref name="origin"/>: the nearest other player's root (for visuals that ride on them).</summary>
    private static Transform? CasterBody(Vector3 origin)
    {
        try
        {
            Transform? best = null;
            float bestDist = 6f;
            foreach (var t in CoopBodies.Others())
            {
                float d = Vector3.Distance(t.position, origin);
                if (d < bestDist) { bestDist = d; best = t; }
            }
            return best;
        }
        catch (Exception e)
        {
            RLog.Warning($"{CoopTestJoin.Tag} finding the caster's body failed: {e.Message}");
            return null;
        }
    }
}

/// <summary>Co-op test helpers for <see cref="SpellNet"/> (real casts from the test client).</summary>
internal static class SpellNetTest
{
    public static int RemoteShapesDrawn => SpellNet.RemoteShapesDrawn;
    public static int RemoteShapesStopped => SpellNet.RemoteShapesStopped;
    public static int SharedPortals => SpellPortals.SharedOpened;

    /// <summary>Equips <paramref name="id"/> in <paramref name="slot"/>, readies it, fills energy and casts it.</summary>
    public static SpellCastOutcome Cast(int slot, string id)
    {
        var pc = PlayerState.Character;
        pc.Spells.Equip(slot, id);
        pc.Spells.Tick(1e6, pc.Stats);
        return CastSlot(slot);
    }

    public static SpellCastOutcome CastSlot(int slot)
    {
        var st = TheForest.Utils.LocalPlayer.Vitals.Stamina;
        st.SetCurrentValue(st.GetMax());
        return SpellCaster.Cast(slot);
    }

    public static int LastShapes(string id) => SpellCaster.LastSync is { } s && s.SpellId == id ? s.Shapes.Count : -1;

    public static int LastPortals() => SpellCaster.LastSync?.Shapes.Count(x => x.Kind == FxKind.Portal) ?? 0;
}
