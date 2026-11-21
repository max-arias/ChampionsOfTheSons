using ChampionsOfTheSons.Core.Networking;
using ChampionsOfTheSons.Mod.Visuals;
using UnityEngine;

namespace ChampionsOfTheSons.Mod.Spells;

/// <summary>
/// Spell visuals that live for a time and then go. With the asset bundle (COS-5) they are the bundled particle effects
/// (COS-102, <see cref="FxKit"/>); without it, coloured Unity primitives (spheres, flat discs, columns). The size passed
/// in is the spell's real radius either way, so what you see matches what is hit. Timers run on the dt the spell world
/// is stepped with, so they work while the game itself is paused (headless self-tests).
/// </summary>
internal static class SpellFx
{
    /// <summary>
    /// Which effect a call draws. <see cref="Look.Auto"/> picks from the shape and lifetime: a short disc or sphere is a
    /// burst, a lasting disc an area ring, a lasting small sphere an orb, a lasting sphere that follows the player a bubble.
    /// </summary>
    public enum Look { Auto, Burst, Ring, Dome, Orb, Bubble, BlackHole, Snow, Gas, Glyph, Flame, SunFlare }

    private sealed class Item
    {
        public GameObject Go = null!;
        public float Left;
        public Transform? Follow;
        public Vector3 Offset;
        public Vector3 Velocity;
        public Transform? Homing;
    }

    /// <summary>A flying visual turns toward <paramref name="target"/> at the caster's bolt turn rate (a teammate's Seeking Arrow).</summary>
    public static void SetHoming(GameObject? go, Transform target)
    {
        if (go == null) return;
        var it = Items.FirstOrDefault(x => x.Go == go);
        if (it != null) it.Homing = target;
    }

    private static readonly List<Item> Items = new();
    private static Shader? _unlit;
    private static bool _looked;

    public static readonly Color Fire = new(1f, 0.45f, 0.1f), Ice = new(0.55f, 0.85f, 1f), Arcane = new(0.7f, 0.35f, 1f),
        Holy = new(1f, 0.9f, 0.4f), Heal = new(0.4f, 1f, 0.5f), Blood = new(0.75f, 0.05f, 0.1f), Dark = new(0.12f, 0.05f, 0.2f),
        Storm = new(0.6f, 0.8f, 1f), Steel = new(0.8f, 0.85f, 0.95f);

    /// <summary>Lifetime below which a disc or sphere counts as a one-shot burst.</summary>
    private const float BurstSeconds = 2f;

    public static GameObject Sphere(Vector3 pos, float radius, Color color, float seconds, Transform? follow = null, Look look = Look.Auto)
    {
        if (look == Look.Auto)
            look = seconds <= BurstSeconds && follow == null ? Look.Burst : follow != null ? Look.Bubble : radius <= 1f ? Look.Orb : Look.Bubble;
        var go = Fx(look, pos, radius, color, seconds, follow) ?? Make(PrimitiveType.Sphere, pos, Vector3.one * radius * 2, color, seconds, follow);
        return Record(go, FxKind.Sphere, look, pos, radius, 0, color, seconds, follow != null, Vector3.zero);
    }

    /// <summary>A flat disc lying on the ground, <paramref name="radius"/> across to its edge.</summary>
    public static GameObject Disc(Vector3 pos, float radius, Color color, float seconds, Transform? follow = null, Look look = Look.Auto)
    {
        if (look == Look.Auto) look = seconds <= BurstSeconds ? Look.Burst : Look.Ring;
        var go = Fx(look, pos, radius, color, seconds, follow) ?? Make(PrimitiveType.Cylinder, pos, new Vector3(radius * 2, 0.04f, radius * 2), color, seconds, follow);
        return Record(go, FxKind.Disc, look, pos, radius, 0, color, seconds, follow != null, Vector3.zero);
    }

    public static GameObject Column(Vector3 pos, float radius, float height, Color color, float seconds)
    {
        var go = FxKit.Spawn(FxKit.Column, pos, new Vector3(radius, height, radius), color);
        if (go != null)
        {
            FxKit.AddLight(go, color, radius * 3 + 6, 60, offset: Vector3.up * 0.3f);
            Track(go, seconds, null);
        }
        else
        {
            go = Make(PrimitiveType.Cylinder, pos + Vector3.up * height / 2, new Vector3(radius * 2, height / 2, radius * 2), color, seconds, null);
        }
        return Record(go, FxKind.Column, Look.Auto, pos, radius, height, color, seconds, false, Vector3.zero);
    }

    /// <summary>A standing portal facing <paramref name="facing"/> (its 1 m radius ring stands on the ground); a column without the bundle.</summary>
    public static GameObject Portal(Vector3 pos, Vector3 facing, Color color, float seconds)
    {
        var context = Spells.SpellNet.Context;
        Spells.SpellNet.Context = null; // the fallback column is part of this portal, not a shape of its own
        var go = FxKit.Spawn(FxKit.Portal, pos, 1f, color);
        if (go == null)
        {
            go = Column(pos, 0.9f, 2.4f, color, seconds);
        }
        else
        {
            facing.y = 0;
            if (facing.sqrMagnitude > 1e-4f) go.transform.rotation = Quaternion.LookRotation(facing.normalized);
            FxKit.AddLight(go, color, 6, 40, offset: Vector3.up * 1.1f);
            Track(go, seconds, null);
        }
        Spells.SpellNet.Context = context;
        return Record(go, FxKind.Portal, Look.Auto, pos, 1f, 0, color, seconds, false, facing);
    }

    /// <summary>COS-80: hands a visual the local caster drew to the cast sync (no-op outside a cast).</summary>
    private static GameObject Record(GameObject go, FxKind kind, Look look, Vector3 pos, float radius, float height, Color color, float seconds,
        bool follow, Vector3 facing)
    {
        if (Spells.SpellNet.Context == null) return go;
        uint rgb = ((uint)Mathf.Clamp(Mathf.RoundToInt(color.r * 255), 0, 255) << 16) | ((uint)Mathf.Clamp(Mathf.RoundToInt(color.g * 255), 0, 255) << 8) |
                   (uint)Mathf.Clamp(Mathf.RoundToInt(color.b * 255), 0, 255);
        var v = new Vec3(pos.x, pos.y, pos.z);
        Spells.SpellNet.Recorded(go, new FxShape(kind, (byte)look, v, radius, height, rgb, seconds, follow, default, new Vec3(facing.x, facing.y, facing.z)));
        return go;
    }

    /// <summary>Makes a visual fly (a teammate's bolt or Ball Lightning, drawn from the caster's numbers).</summary>
    public static void SetVelocity(GameObject? go, Vector3 velocity)
    {
        if (go == null) return;
        var it = Items.FirstOrDefault(x => x.Go == go);
        if (it != null) it.Velocity = velocity;
    }

    /// <summary>The bundled effect for <paramref name="look"/>, tracked like a primitive; null without the bundle.</summary>
    private static GameObject? Fx(Look look, Vector3 pos, float radius, Color color, float seconds, Transform? follow)
    {
        if (!FxKit.Available) return null;
        GameObject? go;
        switch (look)
        {
            case Look.Burst:
                go = FxKit.Spawn(FxKit.Burst, pos, radius, color);
                if (go != null && radius >= 1.5f) FxKit.AddLight(go, color, radius + 4, 30, offset: Vector3.up);
                seconds = Mathf.Max(seconds, 0.7f); // let the wave finish
                break;
            case Look.Ring:
                go = FxKit.Spawn(FxKit.Ring, pos, radius, color);
                break;
            case Look.Dome:
                go = FxKit.Spawn(FxKit.Dome, pos, radius, color);
                if (go != null) FxKit.AddLight(go, color, radius + 4, 40, offset: Vector3.up * 1.5f);
                break;
            case Look.Orb:
                go = FxKit.Spawn(FxKit.Orb, pos, radius, color);
                if (go != null) FxKit.AddLight(go, color, 6 + radius * 4, 40);
                break;
            case Look.Bubble:
                go = FxKit.Spawn(FxKit.Bubble, pos, radius, color);
                break;
            case Look.BlackHole:
                // radius = the dark core's; the prefab's core is 0.22 of its unit size, the pull swirl reaches the rest.
                go = FxKit.Spawn(FxKit.BlackHole, pos, radius / 0.22f, color);
                if (go != null) FxKit.AddLight(go, new Color(0.55f, 0.25f, 1f), radius + 6, 80, essential: true);
                break;
            case Look.Snow:
                go = FxKit.Spawn(FxKit.Ring, pos, radius, color);
                if (go != null)
                {
                    var snow = FxKit.Spawn(FxKit.Snow, pos, new Vector3(radius, 7, radius), Color.white);
                    snow?.transform.SetParent(go.transform, true);
                }
                break;
            case Look.Gas:
                go = FxKit.Spawn(FxKit.Gas, pos, radius, Color.Lerp(color, new Color(0.45f, 0.6f, 0.2f), 0.6f));
                seconds = Mathf.Max(seconds, 2.5f); // the cloud lingers a moment after the hit
                break;
            case Look.Glyph:
                go = FxKit.Spawn(FxKit.Glyph, pos, radius, color) ?? FxKit.Spawn(FxKit.Burst, pos, radius, color);
                break;
            case Look.Flame:
                go = FxKit.Spawn(FxKit.Flame, pos, radius, color);
                break;
            case Look.SunFlare:
                // A ring on the ground and a beam from the sky onto it, lit from above.
                go = FxKit.Spawn(FxKit.Ring, pos, radius, color);
                if (go != null)
                {
                    var beam = FxKit.Spawn(FxKit.Beam, pos, new Vector3(radius * 0.4f, 60, radius * 0.4f), color);
                    beam?.transform.SetParent(go.transform, true);
                    FxKit.AddLight(go, color, radius + 10, 200, essential: true, offset: Vector3.up * 4);
                }
                break;
            default:
                go = null;
                break;
        }
        return go == null ? null : Track(go, seconds, follow);
    }

    private static GameObject Track(GameObject go, float seconds, Transform? follow)
    {
        Items.Add(new Item { Go = go, Left = seconds, Follow = follow, Offset = follow != null ? go.transform.position - follow.position : Vector3.zero });
        return go;
    }

    private static GameObject Make(PrimitiveType type, Vector3 pos, Vector3 scale, Color color, float seconds, Transform? follow)
    {
        var go = GameObject.CreatePrimitive(type);
        go.name = "cots-spell-fx";
        var col = go.GetComponent<Collider>();
        if (col != null) UnityEngine.Object.Destroy(col); // never block the player, bullets or enemies
        go.transform.position = pos;
        go.transform.localScale = scale;
        var rend = go.GetComponent<Renderer>();
        if (rend != null)
        {
            rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            rend.receiveShadows = false;
            var unlit = FindUnlit();
            if (unlit != null)
            {
                var mat = new Material(unlit);
                mat.SetColor("_UnlitColor", color);
                mat.color = color;
                rend.material = mat;
            }
            else
            {
                rend.material.color = color;
            }
        }
        return Track(go, seconds, follow);
    }

    /// <summary>Removes one effect early.</summary>
    public static void Remove(GameObject? go)
    {
        if (go == null) return;
        int i = Items.FindIndex(x => x.Go == go);
        if (i >= 0) Items.RemoveAt(i);
        Spells.SpellNet.Removed(go);
        UnityEngine.Object.Destroy(go);
    }

    public static void Step(float dt)
    {
        for (int i = Items.Count - 1; i >= 0; i--)
        {
            var it = Items[i];
            if (it.Go == null)
            {
                Items.RemoveAt(i);
                continue;
            }
            if (it.Follow != null) it.Go.transform.position = it.Follow.position + it.Offset;
            else if (it.Velocity != Vector3.zero)
            {
                if (it.Homing != null)
                {
                    var want = (it.Homing.position + Vector3.up * 1.2f - it.Go.transform.position).normalized;
                    it.Velocity = Vector3.RotateTowards(it.Velocity, want * it.Velocity.magnitude, SpellWorld.BoltTurnRate * dt, 0);
                }
                it.Go.transform.position += it.Velocity * dt;
            }
            it.Left -= dt;
            if (it.Left > 0) continue;
            Spells.SpellNet.Expired(it.Go);
            UnityEngine.Object.Destroy(it.Go);
            Items.RemoveAt(i);
        }
    }

    public static void Clear()
    {
        foreach (var it in Items)
            if (it.Go != null) UnityEngine.Object.Destroy(it.Go);
        Items.Clear();
    }

    private static Shader? FindUnlit()
    {
        if (_looked) return _unlit;
        _looked = true;
        _unlit = Shader.Find("HDRP/Unlit") ?? Shader.Find("Unlit/Color");
        return _unlit;
    }
}
