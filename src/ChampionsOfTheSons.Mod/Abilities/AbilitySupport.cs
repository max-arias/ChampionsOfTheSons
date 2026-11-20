using ChampionsOfTheSons.Core;
using ChampionsOfTheSons.Core.Rules;
using ChampionsOfTheSons.Mod.Bridge;
using HarmonyLib;
using RedLoader;
using TheForest.Utils;
using UnityEngine;

namespace ChampionsOfTheSons.Mod.Abilities;

/// <summary>
/// Who an enemy's active ability can target. Only the local player for now (the host's); co-op (track E) extends
/// <see cref="Positions"/> with the remote players so every ability picks up the other targets without changes.
/// </summary>
internal static class AbilityTargets
{
    /// <summary>Self-tests: pretend the player stands here (null = the real player).</summary>
    public static Vector3? TestPosition { get; set; }

    public static bool PlayerAlive
    {
        get
        {
            try
            {
                return LocalPlayer.IsInWorld && LocalPlayer.Vitals != null && LocalPlayer.Vitals.Health._currentValue > 0;
            }
            catch
            {
                return false;
            }
        }
    }

    public static IEnumerable<Vector3> Positions()
    {
        if (PlayerAlive) yield return TestPosition ?? LocalPlayer.Transform.position;
    }

    public static Vector3? Nearest(Vector3 from)
    {
        Vector3? best = null;
        float bestDist = float.MaxValue;
        foreach (var p in Positions())
        {
            float d = Vector3.Distance(p, from);
            if (d < bestDist)
            {
                bestDist = d;
                best = p;
            }
        }
        return best;
    }

    public static float Flat(Vector3 a, Vector3 b) => new Vector2(a.x - b.x, a.z - b.z).magnitude;
}

/// <summary>Damage and crowd control from abilities onto the player, plus per-enemy counters the self-tests read (only kept while self-testing).</summary>
internal static class AbilityDamage
{
    private static readonly Dictionary<(EnemyRuntime, EnemyAbility), int> Fired = new();
    private static readonly Dictionary<(EnemyRuntime, EnemyAbility), int> Hits = new();
    private static readonly Dictionary<(EnemyRuntime, EnemyAbility), double> Dealt = new();

    public static int FiredCount(EnemyRuntime e, EnemyAbility a) => Fired.GetValueOrDefault((e, a));
    public static int HitCount(EnemyRuntime e, EnemyAbility a) => Hits.GetValueOrDefault((e, a));
    public static double DamageDealt(EnemyRuntime e, EnemyAbility a) => Dealt.GetValueOrDefault((e, a));

    public static void Reset()
    {
        Fired.Clear();
        Hits.Clear();
        Dealt.Clear();
    }

    /// <summary>An ability started a cast (counted when the telegraph begins).</summary>
    public static void NoteFired(EnemyRuntime e, EnemyAbility a)
    {
        if (SelfTest.Enabled) Fired[(e, a)] = FiredCount(e, a) + 1;
    }

    /// <summary>An ability landed an effect that is not damage (a warp, a root).</summary>
    public static void NoteEffect(EnemyRuntime e, EnemyAbility a)
    {
        if (SelfTest.Enabled) Hits[(e, a)] = HitCount(e, a) + 1;
    }

    /// <summary>Magic damage to the player: the ability's roll times the enemy's own damage multipliers; the player's defenses apply in the PlayerStats.Hit hook.</summary>
    public static void HitPlayer(EnemyRuntime enemy, EnemyAbility ability, double baseDamage)
    {
        double dmg = baseDamage * enemy.Combat.DamageDealtMultiplier * WorldState.Rules.EnemyDamage;
        if (ability == EnemyAbility.Meteor) dmg *= PlayerState.Character.Stats.Get("perk_meteor_damage_taken");
        if (SelfTest.Enabled)
        {
            Hits[(enemy, ability)] = HitCount(enemy, ability) + 1;
            Dealt[(enemy, ability)] = DamageDealt(enemy, ability) + dmg;
        }
        if (dmg <= 0) return;
        try
        {
            LocalPlayer.Stats.Hit((float)dmg, isDemonic: true);
        }
        catch (Exception e)
        {
            RLog.Error($"Champions of the Sons: ability damage failed: {e}");
        }
    }
}

/// <summary>Movement effects on the player: slows and the Black Hole pull, each keyed by the enemy ability that owns it. Applied in <see cref="AbilityForcePatch"/>, aged by the host loop.</summary>
internal static class AbilityForces
{
    private sealed class Slow
    {
        public float Amount, Left;
    }

    private sealed class Pull
    {
        public Vector3 Velocity;
        public float Left;
    }

    private static readonly Dictionary<string, Slow> Slows = new();
    private static readonly Dictionary<string, Pull> Pulls = new();

    public static int SlowCount => Slows.Count;
    public static bool Pulling => Pulls.Values.Any(p => p.Left > 0 && p.Velocity != Vector3.zero);

    /// <summary>Slows owned by the abilities of one enemy (keys end with its identity hash).</summary>
    public static int SlowsOf(EnemyRuntime e) => Slows.Keys.Count(k => k.EndsWith(":" + System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(e), StringComparison.Ordinal));

    public static Vector3 PullOf(EnemyRuntime e) =>
        Pulls.Where(kv => kv.Key.EndsWith(":" + System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(e), StringComparison.Ordinal) && kv.Value.Left > 0)
            .Aggregate(Vector3.zero, (sum, kv) => sum + kv.Value.Velocity);

    /// <summary>Per source, keeps the strongest amount and longest duration.</summary>
    public static void AddSlow(string source, double speedMult, float seconds)
    {
        if (Slows.TryGetValue(source, out var s))
        {
            s.Amount = Mathf.Min(s.Amount, (float)speedMult);
            s.Left = Mathf.Max(s.Left, seconds);
        }
        else
        {
            Slows[source] = new Slow { Amount = (float)speedMult, Left = seconds };
        }
    }

    public static void RemoveSlow(string source) => Slows.Remove(source);

    public static void SetPull(string source, Vector3 velocity, float seconds)
    {
        if (seconds <= 0 || velocity == Vector3.zero) Pulls.Remove(source);
        else Pulls[source] = new Pull { Velocity = velocity, Left = seconds };
    }

    public static float SpeedMultiplier
    {
        get
        {
            float m = 1;
            foreach (var s in Slows.Values) m *= s.Amount;
            return m;
        }
    }

    public static Vector3 PullVelocity => Pulls.Values.Where(p => p.Left > 0).Aggregate(Vector3.zero, (sum, p) => sum + p.Velocity);

    public static void Age(float dt)
    {
        foreach (var key in Pulls.Keys.ToList())
            if ((Pulls[key].Left -= dt) <= 0) Pulls.Remove(key);
        foreach (var key in Slows.Keys.ToList())
        {
            var s = Slows[key];
            s.Left -= dt;
            if (s.Left <= 0) Slows.Remove(key);
        }
    }

    public static void Reset()
    {
        Slows.Clear();
        Pulls.Clear();
    }
}

[HarmonyPatch]
internal static class AbilityForcePatch
{
    [HarmonyPatch(typeof(FirstPersonCharacter), nameof(FirstPersonCharacter.GetTargetVelocity))]
    [HarmonyPostfix]
    private static void AfterTargetVelocity(ref Vector3 __result)
    {
        float m = AbilityForces.SpeedMultiplier;
        if (!Mathf.Approximately(m, 1)) __result = new Vector3(__result.x * m, __result.y, __result.z * m);
        var pull = AbilityForces.PullVelocity;
        if (pull != Vector3.zero) __result += pull;
    }
}

/// <summary>
/// Ability visuals. With the asset bundle each named object is a bundled effect (COS-102, <see cref="Visuals.FxKit"/>)
/// inside an adapter that keeps the primitive's transform meaning (a sphere's scale is its diameter, a cylinder is
/// centred and 2 m tall at scale 1), so ability code positions, scales and stretches either one the same way.
/// Without the bundle: Unity primitives with flat unlit colour.
/// </summary>
internal static class AbilityVisuals
{
    private static Shader? _unlit;
    private static bool _shaderLooked;

    /// <summary>The effect drawn for each named ability object, and how the inner effect sits in the primitive-shaped adapter.</summary>
    private static (string Fx, Vector3 Offset, Vector3 Scale, Color? Tint)? FxFor(string name, PrimitiveType type)
    {
        var sphere = (Vector3.zero, Vector3.one * 0.5f);
        var column = (Vector3.down, new Vector3(0.5f, 2f, 0.5f)); // cylinder centre and half-height -> base and full height
        var disc = (Vector3.zero, new Vector3(0.5f, 25f, 0.5f));  // discs are 0.04 tall: undo that
        (string, (Vector3, Vector3), Color?)? pick = name switch
        {
            "cots-ability-flash" => (Visuals.FxKit.Burst, sphere, null),
            "cots-ability-radiance" or "cots-ability-flare" or "cots-ability-warp" => (Visuals.FxKit.Orb, sphere, null),
            "cots-ability-meteor" => (Visuals.FxKit.Orb, sphere, null),
            // The core sphere is the hole's dark centre; the effect's pull swirl reaches about four times further.
            "cots-ability-blackhole" => (Visuals.FxKit.BlackHole, (Vector3.zero, Vector3.one * 2.3f), new Color(1f, 0.3f, 0.3f)),
            "cots-ability-laser" => (Visuals.FxKit.Beam, column, null),
            "cots-ability-chains" => (Visuals.FxKit.Chain, column, null),
            "cots-ability-cataclysm" => (Visuals.FxKit.Column, column, null),
            _ when type == PrimitiveType.Cylinder && name.EndsWith("-disc", StringComparison.Ordinal) => (Visuals.FxKit.Ring, disc, null),
            "cots-ability-aura" or "cots-ability-meteor-mark" or "cots-ability-blackhole-mark" or "cots-ability-trap" => (Visuals.FxKit.Ring, disc, null),
            _ => null,
        };
        return pick is { } p ? (p.Item1, p.Item2.Item1, p.Item2.Item2, p.Item3) : null;
    }

    public static GameObject Make(PrimitiveType type, Color color, Vector3 position, Vector3 scale, string name = "cots-ability")
    {
        if (Visuals.FxKit.Available && FxFor(name, type) is { } fx)
        {
            var inner = Visuals.FxKit.Spawn(fx.Fx, position, Vector3.one, fx.Tint ?? Visuals.FxKit.Glow(color));
            if (inner != null)
            {
                var holder = new GameObject(name);
                holder.transform.position = position;
                inner.transform.SetParent(holder.transform, false);
                inner.transform.localPosition = fx.Offset;
                inner.transform.localScale = fx.Scale;
                holder.transform.localScale = scale;
                return holder;
            }
        }
        var go = GameObject.CreatePrimitive(type);
        go.name = name;
        var col = go.GetComponent<Collider>();
        if (col != null) UnityEngine.Object.Destroy(col); // never block the player or bullets
        go.transform.position = position;
        go.transform.localScale = scale;
        Paint(go, color);
        return go;
    }

    /// <summary>A flat disc lying on the ground, <paramref name="radius"/> m wide.</summary>
    public static GameObject Disc(Color color, Vector3 position, float radius, string name = "cots-ability-disc") =>
        Make(PrimitiveType.Cylinder, color, position + Vector3.up * 0.05f, new Vector3(radius * 2, 0.04f, radius * 2), name);

    /// <summary>A thin cylinder from <paramref name="a"/> to <paramref name="b"/>.</summary>
    public static void Stretch(GameObject go, Vector3 a, Vector3 b, float width)
    {
        var d = b - a;
        float len = d.magnitude;
        go.transform.position = (a + b) / 2;
        if (len > 0.01f) go.transform.up = d / len;
        go.transform.localScale = new Vector3(width, len / 2, width); // a cylinder is 2 m tall at scale 1
        if (go.name == "cots-ability-chains" && go.GetComponent<Renderer>() == null)
            foreach (var r in go.GetComponentsInChildren<Renderer>())
                r.material.mainTextureScale = new Vector2(1, len / Mathf.Max(width * 1.6f, 0.05f)); // one link per ~1.6 widths
    }

    public static Light AddLight(GameObject go, Color color, float range, float intensity)
    {
        var light = go.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = color;
        light.range = range;
        light.intensity = intensity;
        return light;
    }

    public static void Recolor(GameObject go, Color color)
    {
        var rend = go.GetComponent<Renderer>();
        if (rend == null)
        {
            Visuals.FxKit.Tint(go, Visuals.FxKit.Glow(color));
            return;
        }
        rend.material.SetColor("_UnlitColor", color);
        rend.material.color = color;
    }

    private static void Paint(GameObject go, Color color)
    {
        var rend = go.GetComponent<Renderer>();
        if (rend == null) return;
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

    private static Shader? FindUnlit()
    {
        if (_shaderLooked) return _unlit;
        _shaderLooked = true;
        _unlit = Shader.Find("HDRP/Unlit") ?? Shader.Find("Unlit/Color");
        return _unlit;
    }
}
