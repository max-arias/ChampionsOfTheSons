using ChampionsOfTheSons.Mod.Bundles;
using UnityEngine;

namespace ChampionsOfTheSons.Mod.Visuals;

/// <summary>
/// Spawns the bundled effect prefabs (COS-102): <c>Fx_*</c> from unity/CotsBundles (CotsFxPrefabs). Every prefab is
/// authored white at unit size (radius 1 m; beams and columns 0..1 along +Y), so callers give a scale and a tint and
/// this sets each renderer's <c>_TintColor</c>. Returns null when the bundle or prefab is missing: callers keep their
/// primitive fallback. Point lights are added here, capped, and dropped under "reduce heavy effects".
/// </summary>
internal static class FxKit
{
    public const string Ring = "Fx_Ring", Burst = "Fx_Burst", Orb = "Fx_Orb", Bubble = "Fx_Bubble", Dome = "Fx_Dome", Column = "Fx_Column",
        BlackHole = "Fx_BlackHole", Snow = "Fx_Snow", Portal = "Fx_Portal", Beam = "Fx_Beam", Chain = "Fx_Chain", Flame = "Fx_Flame",
        Glyph = "Fx_Glyph", Gas = "Fx_Gas";

    /// <summary>Every effect prefab name (self-tests, the frame-cost check).</summary>
    public static readonly string[] All = { Ring, Burst, Orb, Bubble, Dome, Column, BlackHole, Snow, Portal, Beam, Chain, Flame, Glyph, Gas };

    /// <summary>Effects that only decorate (hand flames, hand glyphs): hidden under "reduce heavy effects".</summary>
    private static readonly HashSet<string> Decorative = new() { Flame, Glyph };

    private const int MaxLights = 12;
    private static readonly List<Light> Lights = new();
    private static int _tintId = -1;

    public static bool Available => CotsBundle.Loaded;

    /// <summary>The "reduce heavy effects" setting: fewer particles, no decorative effects, fewer lights.</summary>
    public static bool Reduced => VisualsFeature.ReduceHeavyEffects;

    /// <summary>
    /// An instance of <paramref name="prefab"/> at <paramref name="pos"/>, scaled and tinted, or null (no bundle, or a decorative
    /// effect while reduced). <paramref name="parent"/>: keep world position and follow it.
    /// </summary>
    /// <param name="essential">Shown even under "reduce heavy effects" (an area the player must see, like Black Flame's ring).</param>
    public static GameObject? Spawn(string prefab, Vector3 pos, Vector3 scale, Color tint, Transform? parent = null, bool essential = false)
    {
        if (Reduced && !essential && Decorative.Contains(prefab)) return null;
        var holder = new GameObject("cots-fx-" + prefab);
        holder.transform.position = pos;
        var go = CotsBundle.Spawn(prefab, holder.transform);
        if (go == null)
        {
            UnityEngine.Object.Destroy(holder);
            return null;
        }
        go.transform.localPosition = Vector3.zero;
        holder.transform.localScale = scale;
        if (parent != null) holder.transform.SetParent(parent, true);
        Tint(holder, Glow(tint));
        if (Reduced) Thin(holder);
        return holder;
    }

    public static GameObject? Spawn(string prefab, Vector3 pos, float radius, Color tint, Transform? parent = null, bool essential = false) =>
        Spawn(prefab, pos, Vector3.one * radius, tint, parent, essential);

    /// <summary>The prefab's authored tint per material instance, so re-tinting (traps arming) never compounds.</summary>
    private static readonly Dictionary<int, Color> AuthoredTint = new();

    /// <summary>Sets the tint of every effect renderer under <paramref name="go"/> (instanced materials).</summary>
    public static void Tint(GameObject go, Color tint)
    {
        if (_tintId < 0) _tintId = Shader.PropertyToID("_TintColor");
        if (AuthoredTint.Count > 4096) AuthoredTint.Clear();
        foreach (var r in go.GetComponentsInChildren<Renderer>(true))
        {
            var m = r.material;
            if (!m.HasProperty(_tintId)) continue;
            int key = m.GetInstanceID();
            if (!AuthoredTint.TryGetValue(key, out var authored)) AuthoredTint[key] = authored = m.GetColor(_tintId);
            // Keep the prefab's authored alpha (faint telegraph fills, dark cores) and multiply the colour.
            m.SetColor(_tintId, new Color(tint.r * authored.r, tint.g * authored.g, tint.b * authored.b, authored.a * tint.a));
        }
    }

    /// <summary>
    /// A colour that shows as glow: the effects are mostly additive, so a very dark colour (the spells' "dark") would
    /// vanish. Dark colours are lifted to the same hue at a visible brightness; black becomes violet.
    /// </summary>
    public static Color Glow(Color c)
    {
        float m = Mathf.Max(c.r, Mathf.Max(c.g, c.b));
        if (m >= 0.35f) return c;
        if (m < 0.02f) return new Color(0.6f, 0.25f, 1f, c.a);
        float k = 0.8f / m;
        return new Color(c.r * k, c.g * k, c.b * k, c.a);
    }

    /// <summary>Halves particle emission (reduce heavy effects).</summary>
    private static void Thin(GameObject go)
    {
        foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true))
        {
            var em = ps.emission;
            em.rateOverTimeMultiplier *= 0.5f;
            em.rateOverDistanceMultiplier *= 0.5f;
        }
    }

    /// <summary>A point light on <paramref name="go"/>, unless reduced (only <paramref name="essential"/> lights) or the cap is reached.</summary>
    public static Light? AddLight(GameObject go, Color color, float range, float intensity, bool essential = false, Vector3 offset = default)
    {
        Lights.RemoveAll(l => l == null);
        if ((Reduced && !essential) || Lights.Count >= MaxLights) return null;
        var lo = new GameObject("light");
        lo.transform.SetParent(go.transform, false);
        lo.transform.localPosition = offset;
        var light = lo.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = color;
        light.range = range;
        light.intensity = intensity;
        light.shadows = LightShadows.None;
        Lights.Add(light);
        return light;
    }

}
