#nullable enable
// Effect prefabs built from the Unity Asset Store packs under Assets/ThirdParty/AssetStore (COS-211). The packs'
// materials use Unity's built-in particle shaders, which HDRP does not draw, and their sources may not be published
// (Asset Store EULA), so the folder is git-ignored and each effect is rebuilt here: the pack prefab is copied, every
// material is replaced by a Cots/Fx one with the same texture and blend, authored colours are reduced to brightness
// (the mod tints one prefab per spell colour), and everything the game cannot run or the mod adds itself (scripts,
// lights, audio, distortion layers) is removed. A row saved under an existing prefab name replaces that prefab.
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public static class CotsPackPrefabs
{
    public const string PackRoot = "Assets/ThirdParty/AssetStore";

    /// <summary>One pack prefab placed inside a generated prefab.</summary>
    public sealed class Part
    {
        public string Path = "";                 // under PackRoot
        public float Scale = 1;                  // brings the pack effect to the mod's unit size (radius 1 or 1 m)
        public Vector3 Offset;
        public Vector3 Euler;
        public bool KeepColor;                   // true = keep the authored colours instead of brightness only
        public bool DarkToClear;                 // dark colours fade out instead of drawing black (flames that end in soot)
        public float Rate = 1;                   // emission multiplier (effects the mod spawns many of at once)
        public string[] Drop = new string[0];    // child names to remove (layers that do not fit or cost too much)
        public float Fill;                       // > 0: the part's root particle is copied as a filled disc of this opacity behind it
    }

    /// <summary>A generated prefab: its bundle name, whether it loops, and the pack prefabs it is made of.</summary>
    public sealed class Row
    {
        public string Name = "";
        public bool? Loop;                       // null = as authored
        public bool Prewarm;                     // looping effects that must look full the moment they appear
        public bool Base;                        // true = add the parts to our own prefab of that name (CotsFxPrefabs)
        public Part[] Parts = new Part[0];
    }

    private const string Hovl = "HovlMagicEffects/Hovl Studio/Magic effects pack/Prefabs/";
    private const string Quick = "QuickEffects/GabrielAguiarProductions/FreeQuickEffectsVol1/Prefabs/";

    private const string Pack = "ParticlePack/UnityTechnologies/ParticlePack/EffectExamples/";

    private static Part H(string path, float scale = 1, float y = 0) => new Part { Path = Hovl + path + ".prefab", Scale = scale, Offset = new Vector3(0, y, 0) };
    private static Part Q(string name, float scale = 1, float y = 0) => new Part { Path = Quick + name + ".prefab", Scale = scale, Offset = new Vector3(0, y, 0) };

    // Sizes come from CotsInspect.DumpFx: the mod's unit is radius 1 around the prefab origin (discs lie on the ground).
    // The Hovl hits and shields are authored 1 m above their origin; the y offsets bring them back to it.
    private static readonly Row[] Table =
    {
        // Replacements for the spell prefabs (same names, so the mod's call sites do not change).
        new Row { Name = "Fx_Ring", Loop = true, Parts = new[] { H("Magic circles/Magic circle", 0.667f) } },
        new Row { Name = "Fx_Glyph", Loop = true, Parts = new[] { H("Magic circles/Magic circle 2", 0.5f) } },
        new Row { Name = "Fx_Burst", Loop = false, Parts = new[] { H("Hits and explosions/Holy hit", 0.5f), H("Sparks/Sparks explode white", 0.5f) } },
        new Row { Name = "Fx_Bubble", Loop = true, Parts = new[] { H("Magic shields/Magic shield blue", 0.714f, -0.714f) } },
        new Row { Name = "Fx_Dome", Loop = true, Base = true, Parts = new[] { H("Magic circles/Healing circle", 0.25f) } },
        // Centred on its origin: lifted so the whole ring stands above ground; its middle filled with the gate's colour (COS-213).
        new Row { Name = "Fx_Portal", Loop = true, Parts = new[] { new Part { Path = Hovl + "Portals/Portal blue.prefab", Scale = 1.2f, Offset = new Vector3(0, 1.45f, 0), Fill = 0.55f } } },
        new Row { Name = "Fx_Orb", Loop = true, Base = true, Parts = new[] { Q("vfx_Projectile_01", 4) } },
        new Row { Name = "Fx_Flame", Loop = true, Prewarm = true, Parts = new[] { new Part { Path = Quick + "vfx_Flames_01.prefab", Scale = 0.3f, Rate = 0.5f, DarkToClear = true } } },
        new Row { Name = "Fx_BallLightning", Loop = true, Base = true, Parts = new[] { Q("vfx_Electricity_01", 0.35f) } },
        new Row { Name = "Fx_Gas", Loop = true, Prewarm = true, Parts = new[] { new Part { Path = Pack + "Smoke & Steam Effects/Prefabs/PoisonGas.prefab", Scale = 0.3f } } },
        new Row { Name = "Fx_BlackHole", Base = true, Loop = true, Parts = new[] { new Part { Path = Quick + "vfx_Implosion_01.prefab", Scale = 0.2f, Drop = new[] { "Beam" } } } },

        // New looks: hits by kind of magic and the self-buff aura.
        new Row { Name = "Fx_HitFire", Loop = false, Parts = new[] { H("Hits and explosions/Explosion", 0.35f) } },
        new Row { Name = "Fx_HitIce", Loop = false, Parts = new[] { H("Hits and explosions/Snow hit", 0.5f) } },
        new Row { Name = "Fx_HitLightning", Loop = false, Parts = new[] { H("Hits and explosions/Electro hit", 0.5f) } },
        new Row { Name = "Fx_HitArcane", Loop = false, Parts = new[] { H("Hits and explosions/Star hit", 0.5f) } },
        new Row { Name = "Fx_HitStone", Loop = false, Parts = new[] { H("Hits and explosions/Stones hit", 0.5f) } },
        new Row { Name = "Fx_AuraBuff", Loop = true, Parts = new[] { H("Character auras/Buff") } },
    };

    private const int MaxTexture = 512;

    private const string Help = "Import these free packs from the Unity Asset Store into " + PackRoot + " (see docs/asset-bundles.md): "
        + "Magic Effects FREE (Hovl Studio, 247933), Free Quick Effects Vol. 1 (Gabriel Aguiar Prod, 304424), Particle Pack (Unity Technologies, 127325).";

    private static readonly Dictionary<(Material, bool), Material> Converted = new Dictionary<(Material, bool), Material>();
    private static readonly HashSet<string> MatNames = new HashSet<string>();

    public static void Generate()
    {
        if (Table.Length == 0) return;
        Converted.Clear();
        MatNames.Clear();
        _fillTex = null;
        foreach (var row in Table) CotsGen.SavePrefab(Build(row));
    }

    public static GameObject Build(Row row)
    {
        GameObject root;
        if (row.Base)
        {
            var own = AssetDatabase.LoadAssetAtPath<GameObject>($"{CotsGen.Root}/{row.Name}.prefab");
            if (own == null) throw new System.Exception($"{row.Name}: no generated prefab to add to (CotsFxPrefabs must run first)");
            root = Object.Instantiate(own);
            root.name = row.Name;
        }
        else root = new GameObject(row.Name);
        foreach (var part in row.Parts)
        {
            string path = PackRoot + "/" + part.Path;
            var src = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (src == null) throw new System.Exception($"{row.Name}: pack prefab not found: {path}. {Help}");
            var go = Object.Instantiate(src, root.transform);
            go.name = System.IO.Path.GetFileNameWithoutExtension(part.Path);
            go.transform.localPosition = part.Offset;
            go.transform.localRotation = Quaternion.Euler(part.Euler) * src.transform.localRotation;
            go.transform.localScale = src.transform.localScale * part.Scale;
            foreach (var name in part.Drop)
                foreach (var t in go.GetComponentsInChildren<Transform>(true).Where(t => t != null && t.name == name).ToList())
                    if (t != null) Object.DestroyImmediate(t.gameObject);
            Strip(go);
            foreach (var r in go.GetComponentsInChildren<Renderer>(true)) Remat(r, part.KeepColor);
            foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true)) Fit(ps, row, part);
            // Layers with nothing left to draw (distortion) and nothing under them only cost simulation time.
            foreach (var r in go.GetComponentsInChildren<Renderer>(true).Where(r => !r.enabled && r.transform.childCount == 0 && r.gameObject != go).ToList())
                Object.DestroyImmediate(r.gameObject);
            if (!part.KeepColor)
            {
                foreach (var tr in go.GetComponentsInChildren<TrailRenderer>(true)) tr.colorGradient = Gray(tr.colorGradient);
                foreach (var lr in go.GetComponentsInChildren<LineRenderer>(true)) lr.colorGradient = Gray(lr.colorGradient);
            }
            if (part.Fill > 0) AddFill(go, part.Fill);
        }
        return root;
    }

    private static Texture2D? _fillTex;

    /// <summary>A copy of the part's root particle (the portal's ring: same size, timing and facing) drawing a filled disc behind it,
    /// tinted like the rest by the mod. Alpha blended, so it shows the gate's colour without glowing white.</summary>
    private static void AddFill(GameObject part, float opacity)
    {
        var src = part.GetComponent<ParticleSystem>();
        var srcR = part.GetComponent<ParticleSystemRenderer>();
        if (src == null || srcR == null) throw new System.Exception($"{part.name}: Fill needs a particle system on the part's root");
        _fillTex ??= CotsGen.Tex("FxPortalFill", 128, (u, v) =>
        {
            float d = Mathf.Sqrt((u - 0.5f) * (u - 0.5f) + (v - 0.5f) * (v - 0.5f)) * 2f;   // 0 centre, 1 edge
            float a = (1f - CotsGen.Smooth(0.66f, 0.8f, d)) * Mathf.Lerp(0.85f, 1f, CotsGen.Smooth(0.2f, 0.75f, d));
            return new Color(1, 1, 1, a);
        });
        var go = new GameObject("fill");
        go.transform.SetParent(part.transform, false);
        var ps = go.AddComponent<ParticleSystem>();
        EditorUtility.CopySerialized(src, ps);
        var r = go.GetComponent<ParticleSystemRenderer>();
        EditorUtility.CopySerialized(srcR, r);
        // White tint at full strength: the mod's gate colour comes through as it is (a grey tint made it too dark to see over a forest).
        r.sharedMaterial = CotsGen.Fx("FxPortalFillMat", _fillTex, Color.white, 1.6f, additive: false);
        r.sortingFudge = srcR.sortingFudge + 10;   // drawn before (behind) the ring
        var main = ps.main;
        main.startColor = new Color(1, 1, 1, opacity);
    }

    /// <summary>Removes what the game cannot run (scripts) and what the mod adds itself (lights, sound, physics).</summary>
    private static void Strip(GameObject go)
    {
        foreach (var t in go.GetComponentsInChildren<Transform>(true)) GameObjectUtility.RemoveMonoBehavioursWithMissingScript(t.gameObject);
        foreach (var c in go.GetComponentsInChildren<Component>(true))
            if (c is MonoBehaviour || c is Light || c is AudioSource || c is Collider || c is Rigidbody || c is Animator || c is Animation)
                Object.DestroyImmediate(c);
        foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true))
        {
            var lights = ps.lights;
            lights.enabled = false;
            var col = ps.collision;
            col.enabled = false;
        }
    }

    private static void Remat(Renderer r, bool keepColor)
    {
        r.shadowCastingMode = ShadowCastingMode.Off;
        r.receiveShadows = false;
        var mats = r.sharedMaterials;
        bool any = false;
        for (int i = 0; i < mats.Length; i++)
        {
            mats[i] = mats[i] == null ? null! : Convert(mats[i], keepColor)!;
            any |= mats[i] != null;
        }
        r.sharedMaterials = mats;
        if (r is ParticleSystemRenderer pr && pr.trailMaterial != null) pr.trailMaterial = Convert(pr.trailMaterial, keepColor);
        // A layer whose only material was a distortion (screen-refraction) one has nothing left to draw.
        if (!any && !(r is ParticleSystemRenderer p && p.trailMaterial != null)) r.enabled = false;
    }

    /// <summary>The Cots/Fx stand-in for a built-in particle material; null for distortion materials.</summary>
    private static Material? Convert(Material src, bool keepColor)
    {
        if (src.shader != null && src.shader.name == "Cots/Fx") return src;
        if (Converted.TryGetValue((src, keepColor), out var done)) return done;
        Material? m = null;
        bool distortion = (src.HasProperty("_DistortionEnabled") && src.GetFloat("_DistortionEnabled") > 0.5f) || src.shader == null
            || src.shader.name.ToLowerInvariant().Contains("distort");
        if (!distortion)
        {
            var tex = (src.HasProperty("_MainTex") ? src.GetTexture("_MainTex") as Texture2D : null) ?? Texture2D.whiteTexture;
            // Pack textures go up to 2048 px; effects on screen never need that, and the bundle is embedded in the DLL.
            if (AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(tex)) is TextureImporter imp && imp.maxTextureSize > MaxTexture)
            {
                imp.maxTextureSize = MaxTexture;
                imp.SaveAndReimport();
            }
            string shader = src.shader!.name.ToLowerInvariant();
            // Built-in particle modes: 4 = additive; legacy shaders say it in their name.
            bool additive = shader.Contains("additive") || (src.HasProperty("_Mode") && Mathf.RoundToInt(src.GetFloat("_Mode")) == 4)
                || (src.HasProperty("_DstBlend") && Mathf.RoundToInt(src.GetFloat("_DstBlend")) == (int)BlendMode.One);
            Color c = src.HasProperty("_Color") ? src.GetColor("_Color") : src.HasProperty("_TintColor") ? src.GetColor("_TintColor") : Color.white;
            float bright = Mathf.Max(c.r, c.g, c.b, 0.0001f);
            // HDR colours become intensity; the hue goes unless the row keeps colours.
            Color tint = keepColor ? new Color(c.r / Mathf.Max(1, bright), c.g / Mathf.Max(1, bright), c.b / Mathf.Max(1, bright), c.a)
                : new Color(Mathf.Min(1, bright), Mathf.Min(1, bright), Mathf.Min(1, bright), c.a);
            string name = "Pk_" + new string(src.name.Select(ch => char.IsLetterOrDigit(ch) ? ch : '_').ToArray()) + (keepColor ? "_c" : "");
            for (int n = 2; !MatNames.Add(name); n++) name = name.TrimEnd('0', '1', '2', '3', '4', '5', '6', '7', '8', '9') + n;
            m = CotsGen.Fx(name, tex, tint, Mathf.Max(1, bright), additive, desaturate: keepColor ? 0 : 1);
            if (src.HasProperty("_MainTex"))
            {
                m.SetTextureScale("_MainTex", src.GetTextureScale("_MainTex"));
                m.SetTextureOffset("_MainTex", src.GetTextureOffset("_MainTex"));
            }
            if (src.HasProperty("_Cull")) m.SetFloat("_Cull", src.GetFloat("_Cull"));
            EditorUtility.SetDirty(m);
        }
        Converted[(src, keepColor)] = m!;
        return m;
    }

    /// <summary>Makes a particle system behave the way the mod drives effects: scaled by the holder, no shadows.</summary>
    private static void Fit(ParticleSystem ps, Row row, Part part)
    {
        bool? loop = row.Loop;
        bool keepColor = part.KeepColor;
        var main = ps.main;
        if (part.Rate != 1)
        {
            var em = ps.emission;
            em.rateOverTimeMultiplier *= part.Rate;
        }
        main.scalingMode = ParticleSystemScalingMode.Hierarchy;
        main.playOnAwake = true;
        main.stopAction = ParticleSystemStopAction.None;   // the mod destroys effects; a self-destroying child would break lookups
        if (loop != null) main.loop = loop.Value;
        if (row.Prewarm && main.loop) main.prewarm = true;
        if (keepColor) return;
        _clear = part.DarkToClear;
        main.startColor = Gray(main.startColor);
        var col = ps.colorOverLifetime;
        if (col.enabled) col.color = Gray(col.color);
        var speed = ps.colorBySpeed;
        if (speed.enabled) speed.color = Gray(speed.color);
        var trails = ps.trails;
        if (trails.enabled)
        {
            trails.colorOverLifetime = Gray(trails.colorOverLifetime);
            trails.colorOverTrail = Gray(trails.colorOverTrail);
        }
        _clear = false;
    }

    private static bool _clear;

    private static Color Gray(Color c)
    {
        float v = Mathf.Max(c.r, c.g, c.b);
        return _clear ? new Color(1, 1, 1, c.a * v) : new Color(v, v, v, c.a);
    }

    private static Gradient Gray(Gradient? g)
    {
        var o = new Gradient();
        if (g == null) return o;
        o.mode = g.mode;
        if (_clear)
        {
            // Brightness moves into opacity: sampled, since colour and alpha keys sit at different times.
            var alpha = new GradientAlphaKey[8];
            for (int i = 0; i < 8; i++)
            {
                var c = g.Evaluate(i / 7f);
                alpha[i] = new GradientAlphaKey(c.a * Mathf.Max(c.r, c.g, c.b), i / 7f);
            }
            o.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) }, alpha);
            return o;
        }
        o.SetKeys(g.colorKeys.Select(k => new GradientColorKey(Gray(k.color), k.time)).ToArray(), g.alphaKeys);
        return o;
    }

    private static ParticleSystem.MinMaxGradient Gray(ParticleSystem.MinMaxGradient g)
    {
        switch (g.mode)
        {
            case ParticleSystemGradientMode.Color: return new ParticleSystem.MinMaxGradient(Gray(g.color));
            case ParticleSystemGradientMode.TwoColors: return new ParticleSystem.MinMaxGradient(Gray(g.colorMin), Gray(g.colorMax));
            case ParticleSystemGradientMode.TwoGradients: return new ParticleSystem.MinMaxGradient(Gray(g.gradientMin), Gray(g.gradientMax));
            default:
                var o = new ParticleSystem.MinMaxGradient(Gray(g.gradient));
                o.mode = g.mode;   // Gradient or RandomColor
                return o;
        }
    }
}
