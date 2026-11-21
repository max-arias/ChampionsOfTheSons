using ChampionsOfTheSons.Core;
using ChampionsOfTheSons.Core.Rules;
using ChampionsOfTheSons.Mod.Visuals;
using UnityEngine;

namespace ChampionsOfTheSons.Mod.Weapons;

/// <summary>How one weapon item glows (COS-108): its look, particle style, colour and edge strength.</summary>
internal readonly record struct GlowLook(GlowLevel Level, string Style, Color Color, float Strength)
{
    public static readonly GlowLook None = new(GlowLevel.Common, WeaponGlow.DefaultStyle, Color.white, 0);

    public bool Glows => Strength > 0;
    public bool Extras => WeaponGlow.HasExtras(Level);

    /// <summary>Tells looks apart (a skin is rebuilt when its item's look changes).</summary>
    public string Key => Glows ? $"{Level}:{Style}:{Color.r:0.00},{Color.g:0.00},{Color.b:0.00}" : ""; // ColorUtility is stripped

    public static GlowLook For(string? baseId, Rarity rarity)
    {
        if (baseId == null || !ModContent.Db.Items.TryGetValue(baseId, out var def)) return None;
        var level = WeaponGlow.Level(def, rarity);
        if (level == GlowLevel.Common) return None;
        var (r, g, b) = WeaponGlow.Color(def, level);
        return new GlowLook(level, WeaponGlow.Style(def), new Color(r, g, b), (float)WeaponGlow.Strength(level));
    }

    public static GlowLook For(ItemInstance? item) => item == null ? None : For(item.BaseId, item.Rarity);
}

/// <summary>
/// Puts a weapon glow on a model (COS-108), for held weapons and ground drops alike. Magic and Rare: a fresnel shell in the
/// look's colour over every mesh (Fx_WeaponEdge), faint for Magic, medium for Rare. Legendary (and every unique): a bright
/// shell, embers (or the unique's own particle style) along the longest mesh, a pulsing light in hand, a flame-coloured
/// longer swing trail, and sparks where a hit lands. "Reduce heavy effects" leaves out the particles, light and sparks.
/// Everything is parented under the model, so it goes when the model goes.
/// </summary>
internal static class WeaponGlowFx
{
    public const string Edge = "Fx_WeaponEdge";
    public const float LightRange = 2.6f, LightIntensity = 14f;
    /// <summary>HDRP emission of the weapon's own material per look, in nits (tuned by eye: brighter blows out to white).</summary>
    public static float EmissionNits(GlowLevel level) => level switch
    {
        GlowLevel.Magic => 260f,
        GlowLevel.Rare => 650f,
        GlowLevel.Legendary => 1400f,
        _ => 0f,
    };
    private static int _emissive = -1, _fresnel = -1, _intensity = -1;

    private static readonly List<GameObject> Spawned = new();
    private static readonly List<(Light Light, float Base)> Pulsing = new();

    /// <summary>COS-119: material instances the glow made (renderer.materials / .material copy them), with the object they belong
    /// to; destroyed once that object is gone, so equips and glowing drops no longer leave instances behind.</summary>
    private static readonly List<(GameObject Owner, Material Material)> Owned = new();
    private static float _sweep;
    private static bool? _reducedSeen;

    /// <summary>Glow material instances alive (self-tests).</summary>
    public static int OwnedMaterials => Owned.Count;

    /// <summary>Glow parts alive now (frame-cost logs, self-tests).</summary>
    public static int Alive
    {
        get
        {
            Spawned.RemoveAll(g => g == null);
            return Spawned.Count;
        }
    }

    public static string ParticlePrefab(string style) => style switch
    {
        "frost" => "Fx_WeaponFrost",
        "wisp" => "Fx_WeaponWisp",
        _ => "Fx_WeaponEmber",
    };

    /// <summary>Adds the glow under <paramref name="model"/>; returns how many glow parts it made (0 for no glow).</summary>
    public static int Apply(GameObject model, GlowLook look, bool light)
    {
        if (!look.Glows) return 0;
        int parts = 0;
        MeshFilter? longest = null;
        float longestLen = 0;
        // Additive shell: the strength scales its brightness (Magic barely shows, Legendary blazes).
        var tint = new Color(look.Color.r * look.Strength, look.Color.g * look.Strength, look.Color.b * look.Strength, Mathf.Clamp01(0.4f + look.Strength));
        if (_emissive < 0)
        {
            _emissive = Shader.PropertyToID("_EmissiveColor");
            _fresnel = Shader.PropertyToID("_Fresnel");
            _intensity = Shader.PropertyToID("_Intensity");
        }
        foreach (var mf in model.GetComponentsInChildren<MeshFilter>(true))
        {
            if (mf.sharedMesh == null || IsGlow(mf.transform, model.transform)) continue;
            // The weapon's own (HDRP/Lit) material glows from inside, in the look's colour.
            var own = mf.GetComponent<MeshRenderer>();
            if (own != null)
                foreach (var m in own.materials)
                {
                    if (m == null) continue;
                    Owned.Add((model, m));
                    if (m.HasProperty(_emissive)) m.SetColor(_emissive, look.Color * EmissionNits(look.Level));
                }
            var shell = Bundles.CotsBundle.Spawn(Edge, mf.transform);
            if (shell != null)
            {
                shell.name = "cots-glow-edge";
                shell.transform.localPosition = Vector3.zero;
                shell.transform.localRotation = Quaternion.identity;
                shell.transform.localScale = Vector3.one * 1.015f;
                var smf = shell.GetComponentInChildren<MeshFilter>();
                if (smf != null) smf.sharedMesh = mf.sharedMesh;
                FxKit.Tint(shell, tint);
                // A softer fresnel than the prefab's, so flat faces of low-poly blades carry some of the rim too.
                foreach (var sr in shell.GetComponentsInChildren<Renderer>(true))
                {
                    var sm = sr.material;
                    Owned.Add((shell, sm));
                    if (sm.HasProperty(_fresnel)) sm.SetFloat(_fresnel, 1.1f);
                    if (sm.HasProperty(_intensity)) sm.SetFloat(_intensity, 1.3f + look.Strength);
                }
                Spawned.Add(shell);
                parts++;
            }
            float len = Vector3.Scale(mf.sharedMesh.bounds.size, mf.transform.lossyScale).magnitude;
            if (len > longestLen) { longestLen = len; longest = mf; }
        }

        if (!look.Extras || FxKit.Reduced) return parts;
        if (light && FxKit.AddLight(model, look.Color, LightRange, LightIntensity) is { } l)
        {
            l.gameObject.name = "cots-glow-light";
            Pulsing.Add((l, LightIntensity));
            parts++;
        }
        if (longest != null && AddParticles(longest, look)) parts++;
        return parts;
    }

    /// <summary>Per frame: Legendary lights breathe.</summary>
    public static void Tick()
    {
        _sweep += Time.deltaTime;
        if (_sweep > 2f)
        {
            _sweep = 0;
            for (int i = Owned.Count - 1; i >= 0; i--)
            {
                if (Owned[i].Owner != null) continue;
                if (Owned[i].Material != null) UnityEngine.Object.Destroy(Owned[i].Material);
                Owned.RemoveAt(i);
            }
        }
        // COS-119: "reduce heavy effects" hides the extras (light, particles) of glows already out at once; switching it off
        // brings them back on the next equip or drop.
        bool reduced = FxKit.Reduced;
        if (_reducedSeen != reduced)
        {
            if (_reducedSeen != null)
            {
                Spawned.RemoveAll(g => g == null);
                foreach (var g in Spawned) if (g.name == "cots-glow-wisp") g.SetActive(!reduced);
                foreach (var (l, _) in Pulsing) if (l != null) l.enabled = !reduced;
            }
            _reducedSeen = reduced;
        }
        if (Pulsing.Count == 0) return;
        Pulsing.RemoveAll(p => p.Light == null);
        float k = 0.7f + 0.3f * Mathf.Sin(Time.time * Mathf.PI * 1.6f) + 0.08f * Mathf.Sin(Time.time * 11f);
        foreach (var (l, b) in Pulsing) l.intensity = b * k;
    }

    /// <summary>The particles along the mesh's longest side, from past the grip to the end.</summary>
    private static bool AddParticles(MeshFilter mf, GlowLook look)
    {
        var go = Bundles.CotsBundle.Spawn(ParticlePrefab(look.Style), mf.transform);
        if (go == null) return false;
        go.name = "cots-glow-wisp";
        var b = mf.sharedMesh.bounds;
        var size = b.size;
        Vector3 axis = size.x >= size.y && size.x >= size.z ? Vector3.right : size.y >= size.z ? Vector3.up : Vector3.forward;
        float len = Vector3.Dot(size, axis);
        go.transform.localRotation = Quaternion.FromToRotation(Vector3.up, axis);
        go.transform.localPosition = b.center - axis * (len * 0.3f);
        go.transform.localScale = Vector3.one;
        // The prefab's box is 1 unit long and 0.03 thick; the mesh may be in other units (the shape scales with it).
        float unit = 1f / Mathf.Max(1e-4f, mf.transform.lossyScale.magnitude / 1.732f);
        foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true))
        {
            var shape = ps.shape;
            var s = shape.scale;
            shape.scale = new Vector3(s.x * unit, len * 0.8f, s.z * unit);
            shape.position = new Vector3(0, len * 0.4f, 0);
            var main = ps.main;
            main.startColor = new ParticleSystem.MinMaxGradient(Color.Lerp(look.Color, new Color(1f, 0.85f, 0.5f), 0.3f));
        }
        Spawned.Add(go);
        return true;
    }

    /// <summary>Legendary: a flame-coloured, wider and longer swing trail.</summary>
    public static void Brighten(TrailRenderer trail, GlowLook look)
    {
        if (!look.Extras) return;
        FxKit.Tint(trail.gameObject, look.Color * 1.6f);
        trail.widthMultiplier *= 1.35f;
        trail.time *= 1.6f;
    }

    /// <summary>Legendary: sparks where a melee hit lands (at the blade tip).</summary>
    public static void HitSparks(WeaponSkin skin)
    {
        if (!skin.Look.Extras || FxKit.Reduced || skin.Holder == null) return;
        var at = skin.Trail != null ? skin.Trail.transform.position : skin.Holder.position;
        var fx = FxKit.Spawn(FxKit.Burst, at, 0.12f, skin.Look.Color);
        if (fx != null)
        {
            fx.name = "cots-glow-sparks";
            UnityEngine.Object.Destroy(fx, 1.2f);
            LastSparks = Time.time;
        }
    }

    /// <summary>When the last hit sparks were made (self-tests).</summary>
    public static float LastSparks { get; private set; } = -1;

    private static bool IsGlow(Transform t, Transform root)
    {
        for (var p = t; p != null && p != root; p = p.parent)
            if (p.name.StartsWith("cots-glow", StringComparison.Ordinal) || p.name.StartsWith("cots-trail", StringComparison.Ordinal)) return true;
        return false;
    }
}
