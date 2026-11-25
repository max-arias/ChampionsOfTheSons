#nullable enable
// Spell, ability and state effects (COS-102): Shuriken particle systems and a few meshes on the Cots/Fx shader, all
// built from code with our own procedural textures (no third-party effect files). Each prefab is authored white at
// unit size and the mod scales and tints it (FxLibrary in the mod): the tint goes into every renderer's _TintColor.
//   unit = radius 1 m unless noted; ground effects lie in XZ at y = 0; beams and columns run 0..1 along +Y.
// Particles use Hierarchy scaling, so the transform scale scales shapes, sizes and (local-space) speeds alike.
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public static class CotsFxPrefabs
{
    private static Texture2D? _dot;
    private static Mesh _disc = null!, _sphere = null!, _hemisphere = null!, _cylinder = null!;
    private static Texture2D _white = null!, _ring = null!, _fill = null!, _smoke = null!, _flame = null!, _glyph = null!, _noise = null!, _swirl = null!, _chain = null!, _bolt = null!;

    public static void Generate()
    {
        _dot = null;
        SoftDot();
        _white = CotsGen.Tex("FxWhite", 4, (u, v) => Color.white);
        _ring = CotsGen.Tex("FxRing", 256, (u, v) =>
        {
            float r = Dist(u, v);
            float edge = Mathf.Exp(-Mathf.Pow((r - 0.9f) / 0.035f, 2));
            float inner = CotsGen.Smooth(0.3f, 0.9f, r) * 0.25f * (r < 0.92f ? 1 : 0);
            return White(Mathf.Clamp01(edge + inner));
        });
        _fill = CotsGen.Tex("FxFill", 256, (u, v) =>
        {
            float r = Dist(u, v);
            float a = r > 1 ? 0 : 0.35f + 0.65f * Mathf.Pow(r, 4) - CotsGen.Smooth(0.97f, 1f, r);
            return White(Mathf.Clamp01(a));
        });
        _smoke = CotsGen.Tex("FxSmoke", 128, (u, v) =>
        {
            float r = Dist(u, v);
            float n = CotsGen.Fbm(u, v, 4, 7);
            return White(Mathf.Clamp01((1 - CotsGen.Smooth(0.2f, 1f, r)) * (0.45f + 0.75f * n)));
        });
        _flame = CotsGen.Tex("FxFlame", 128, (u, v) =>
        {
            // A teardrop: wide and soft at the bottom, narrowing to a tip, with a noisy edge.
            float x = (u - 0.5f) * 2, y = v;
            float width = Mathf.Lerp(0.75f, 0.05f, Mathf.Pow(y, 0.8f)) * (0.85f + 0.3f * CotsGen.Noise(u, v, 6, 3));
            float a = 1 - CotsGen.Smooth(width * 0.4f, width, Mathf.Abs(x));
            a *= CotsGen.Smooth(0f, 0.2f, y) * (1 - CotsGen.Smooth(0.75f, 1f, y));
            return White(Mathf.Clamp01(a));
        });
        _glyph = CotsGen.Tex("FxGlyph", 256, Glyph);
        _noise = CotsGen.Tex("FxNoise", 128, (u, v) => White(Mathf.Clamp01(Mathf.Pow(CotsGen.Fbm(u, v, 4, 11), 1.6f) * 1.6f)), repeat: true);
        _swirl = CotsGen.Tex("FxSwirl", 256, (u, v) =>
        {
            float x = u - 0.5f, y = v - 0.5f, r = Mathf.Sqrt(x * x + y * y) * 2;
            float ang = Mathf.Atan2(y, x) / (Mathf.PI * 2) + 0.5f;
            float s = CotsGen.Noise(Frac(ang * 3 + r * 0.9f), Frac(r * 0.7f), 6, 5);
            float a = Mathf.Pow(s, 2) * (1 - CotsGen.Smooth(0.75f, 1f, r)) * CotsGen.Smooth(0.05f, 0.4f, r) * 1.8f;
            return White(Mathf.Clamp01(a));
        });
        // The black hole's swirl is Kenney's hand-drawn twirl (CC0, COS-211) when the texture is there; ours otherwise.
        var twirl = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/ThirdParty/Kenney/twirl_01.png");
        if (twirl != null) _swirl = twirl;
        _chain = CotsGen.Tex("FxChain", 64, (u, v) =>
        {
            // One oval link per tile, seen from the side.
            float x = (u - 0.5f) * 2.2f, y = (Frac(v) - 0.5f) * 2f;
            float d = Mathf.Abs(Mathf.Sqrt(x * x * 2.2f + y * y) - 0.75f);
            return White(1 - CotsGen.Smooth(0.08f, 0.2f, d));
        }, repeat: true);

        _bolt = CotsGen.Tex("FxBolt", 256, (u, v) =>
        {
            // One jagged lightning bolt running top (v = 1, centred) to bottom, with a fainter fork halfway down.
            // Straight segments between random points (value noise sampled at its cell corners), so it is jagged, not wavy.
            float Jag(float y, int cells, int seed)
            {
                float t = y * cells;
                int i = Mathf.FloorToInt(t);
                return Mathf.Lerp(CotsGen.Noise(0.37f, (i + 0.01f) / cells, cells, seed), CotsGen.Noise(0.37f, (i + 1.01f) / cells, cells, seed), t - i) - 0.5f;
            }
            float Off(float y) => (Jag(y, 7, 21) * 0.45f + Jag(y, 19, 22) * 0.16f) * (1 - CotsGen.Smooth(0.85f, 1f, y));
            float x = u - 0.5f - Off(v);
            float a = Mathf.Exp(-Mathf.Pow(x / 0.016f, 2)) + 0.3f * Mathf.Exp(-Mathf.Pow(x / 0.06f, 2));
            if (v < 0.6f && v > 0.22f)
            {
                float fx = u - 0.5f - Off(0.6f) - (0.6f - v) * 0.5f - Jag(v, 14, 23) * 0.12f * (1 - CotsGen.Smooth(0.5f, 0.6f, v));
                a += 0.6f * Mathf.Exp(-Mathf.Pow(fx / 0.012f, 2)) * CotsGen.Smooth(0.22f, 0.3f, v);
            }
            return White(Mathf.Clamp01(a * CotsGen.Smooth(0f, 0.06f, v)));
        });

        _disc = Disc();
        _sphere = Sphere("FxSphereMesh", 24, 16, false);
        _hemisphere = Sphere("FxHemisphereMesh", 24, 8, true);
        _cylinder = Cylinder();

        Ring();
        Burst();
        Orb();
        BallLightning();
        Bolt();
        Bubble();
        Dome();
        Column();
        BlackHole();
        Snow();
        Portal();
        Beam();
        Chain();
        Flame();
        Glyph();
        Gas();
        WeaponTrail();
        WeaponGlow();
        TierAura();
        TierEmbers();
        TierShimmer();
        TierPower();
        LootBeam();
    }

    // ---- ground drops (COS-96, Max 2026-11-21: a glow pool, a beam and rising sparks, sized by rarity) ----

    /// <summary>
    /// Fx_LootBeam, white (the mod tints it in the rarity colour), standing on the ground at the origin. "glow": a soft pool of light,
    /// radius 0.5. "ring": a pulsing ring, radius 0.35 (the mod keeps it for Rare and Legendary). "beam" and "core": a soft beam and a hot
    /// thin core, 1 m tall, bright at the bottom and fading out upward (the mod sets their height). "sparks": motes rising around the
    /// base. "streaks": short streaks running up the beam. The mod sets the rates and lifetimes per rarity.
    /// </summary>
    private static void LootBeam()
    {
        // The fade sits in the colour as well as the alpha: additive blending adds the colour as is.
        var fade = CotsGen.Tex("FxBeamFade", 64, (u, v) =>
        {
            float a = Mathf.Pow(1 - v, 1.8f) * CotsGen.Smooth(0f, 0.04f, v);
            return new Color(a, a, a, a);
        });
        var go = new GameObject("Fx_LootBeam");
        CotsGen.Child(go, "glow", _disc, CotsGen.Fx("FxLootGlow", SoftDot(), Color.white, 3f), new Vector3(0, 0.08f, 0), scale: new Vector3(0.6f, 1, 0.6f));
        CotsGen.Child(go, "ring", _disc, CotsGen.Fx("FxLootRing", _ring, Color.white, 3f, pulse: 0.5f), new Vector3(0, 0.1f, 0), scale: new Vector3(0.4f, 1, 0.4f));
        CotsGen.Child(go, "beam", _cylinder, CotsGen.Fx("FxLootBeam", fade, Color.white, 1.6f, fresnel: 2f, fresnelInvert: true), Vector3.zero, scale: new Vector3(0.09f, 1, 0.09f));
        CotsGen.Child(go, "core", _cylinder, CotsGen.Fx("FxLootCore", fade, Color.white, 4f, fresnel: 1f, fresnelInvert: true), Vector3.zero, scale: new Vector3(0.012f, 1, 0.012f));

        // A soft halo at the base, standing up so grass does not hide it the way it hides the pool.
        var halo = Particles(Sub(go, "halo"), CotsGen.Fx("FxLootHalo", SoftDot(), Color.white, 1.6f));
        halo.transform.localPosition = new Vector3(0, 0.2f, 0);
        Steady(halo, 2.5f, 1.6f, 0.9f);
        FadeInOut(halo);

        var sparks = Particles(Sub(go, "sparks"), CotsGen.Fx("FxLootSpark", SoftDot(), Color.white, 5f));
        Steady(sparks, 14, 1.1f, 0.06f);
        var sm = sparks.main;
        sm.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.09f);
        sm.maxParticles = 60;
        var ss = sparks.shape;
        ss.enabled = true;
        ss.shapeType = ParticleSystemShapeType.Circle;
        ss.radius = 0.18f;
        ss.radiusThickness = 1f;
        ss.rotation = new Vector3(90, 0, 0);
        var sv = sparks.velocityOverLifetime;
        sv.enabled = true;
        sv.space = ParticleSystemSimulationSpace.Local;
        sv.x = new ParticleSystem.MinMaxCurve(0f, 0f); sv.z = new ParticleSystem.MinMaxCurve(0f, 0f); // one curve mode on all axes
        sv.y = new ParticleSystem.MinMaxCurve(0.5f, 1.8f);
        var sn = sparks.noise;
        sn.enabled = true;
        sn.strength = 0.15f;
        sn.frequency = 1.2f;
        FadeOut(sparks);

        var streaks = Particles(Sub(go, "streaks"), CotsGen.Fx("FxLootStreak", SoftDot(), Color.white, 4f), ParticleSystemRenderMode.Stretch);
        Steady(streaks, 3, 0.6f, 0.05f);
        streaks.GetComponent<ParticleSystemRenderer>().velocityScale = 0.12f;
        var tm = streaks.main;
        tm.startSize = new ParticleSystem.MinMaxCurve(0.035f, 0.06f);
        var ts = streaks.shape;
        ts.enabled = true;
        ts.shapeType = ParticleSystemShapeType.Circle;
        ts.radius = 0.02f;
        ts.rotation = new Vector3(90, 0, 0);
        var tv = streaks.velocityOverLifetime;
        tv.enabled = true;
        tv.space = ParticleSystemSimulationSpace.Local;
        tv.x = new ParticleSystem.MinMaxCurve(0f, 0f); tv.z = new ParticleSystem.MinMaxCurve(0f, 0f);
        tv.y = new ParticleSystem.MinMaxCurve(2.2f, 3.2f);
        FadeOut(streaks);

        CotsGen.SavePrefab(go);
    }

    // ---- enemy tier effects (COS-110): white, tinted and sized per tier by the mod ----

    /// <summary>
    /// Fx_TierAura, radius 1, lying at the enemy's feet: a faint filled disc, a pulsing edge ring and motes drifting up from the rim.
    /// Elites, minibosses and bosses share it in their tier colour; the mod sets the motes' rate per tier.
    /// </summary>
    private static void TierAura()
    {
        var go = new GameObject("Fx_TierAura");
        CotsGen.Child(go, "fill", _disc, CotsGen.Fx("FxTierAuraFill", _fill, new Color(1, 1, 1, 0.22f), 1.2f, additive: false), new Vector3(0, 0.03f, 0));
        CotsGen.Child(go, "edge", _disc, CotsGen.Fx("FxTierAuraEdge", _ring, Color.white, 2.6f, pulse: 0.45f), new Vector3(0, 0.05f, 0));
        var motes = Particles(Sub(go, "motes"), CotsGen.Fx("FxTierMotes", SoftDot(), Color.white, 2.2f));
        Steady(motes, 10, 2.2f, 0.07f);
        var m = motes.main;
        m.simulationSpace = ParticleSystemSimulationSpace.World;
        m.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.09f);
        m.maxParticles = 120;
        var sh = motes.shape;
        sh.enabled = true;
        sh.shapeType = ParticleSystemShapeType.Circle;
        sh.radius = 0.9f;
        sh.radiusThickness = 0.3f;
        sh.rotation = new Vector3(90, 0, 0);
        Velocity(motes, new Vector3(0, 0.55f, 0));
        var noise = motes.noise;
        noise.enabled = true;
        noise.strength = 0.2f;
        noise.frequency = 0.6f;
        FadeInOut(motes);
        CotsGen.SavePrefab(go);
    }

    /// <summary>Fx_TierEmbers, radius 1 sphere: embers rising off a body (shoulders for a miniboss, the whole frame for a boss).</summary>
    private static void TierEmbers()
    {
        var go = new GameObject("Fx_TierEmbers");
        var embers = Particles(Sub(go, "embers"), CotsGen.Fx("FxTierEmbers", SoftDot(), Color.white, 3.2f));
        Steady(embers, 16, 1.4f, 0.06f);
        var m = embers.main;
        m.simulationSpace = ParticleSystemSimulationSpace.World;
        m.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.08f);
        m.maxParticles = 160;
        var sh = embers.shape;
        sh.enabled = true;
        sh.shapeType = ParticleSystemShapeType.Sphere;
        sh.radius = 1f;
        sh.radiusThickness = 0.5f;
        Velocity(embers, new Vector3(0, 0.9f, 0));
        var noise = embers.noise;
        noise.enabled = true;
        noise.strength = 0.35f;
        noise.frequency = 1.2f;
        Shrink(embers);
        FadeInOut(embers);
        CotsGen.SavePrefab(go);
    }

    /// <summary>Fx_TierShimmer, a 0..1 column along +Y, radius 1: faint scrolling noise that reads as heat haze around a boss.</summary>
    private static void TierShimmer()
    {
        var go = new GameObject("Fx_TierShimmer");
        CotsGen.Child(go, "haze", _cylinder, CotsGen.Fx("FxTierShimmer", _noise, new Color(1, 1, 1, 0.18f), 0.6f, fresnel: 1.2f, fresnelInvert: true,
            scroll: new Vector2(0.05f, -0.45f), fadeEnds: true));
        CotsGen.SavePrefab(go);
    }

    /// <summary>
    /// Fx_TierPower (COS-110, Max: "floating particles going around the bosses, like a power-up"): unit scale = a body of about
    /// 2 m at its feet. Three systems in world space so they trail a moving body: "flames", stretched streaks rising up a
    /// cylinder shell around the body and swirling as they go; "orbit", sparks circling the waist; "rise", fast motes shooting
    /// up from a ground ring. The mod tints it in the tier colour and sets each rate per tier (miniboss lighter than boss).
    /// </summary>
    private static void TierPower()
    {
        var go = new GameObject("Fx_TierPower");

        var flames = Particles(Sub(go, "flames"), CotsGen.Fx("FxTierPowerFlame", SoftDot(), Color.white, 3.6f), ParticleSystemRenderMode.Stretch);
        Steady(flames, 70, 0.9f, 0.18f);
        var fm = flames.main;
        fm.simulationSpace = ParticleSystemSimulationSpace.World;
        fm.startSize = new ParticleSystem.MinMaxCurve(0.08f, 0.22f);
        fm.maxParticles = 260;
        var fr = flames.GetComponent<ParticleSystemRenderer>();
        fr.velocityScale = 0.12f;
        fr.lengthScale = 2.2f;
        var fs = flames.shape;
        fs.enabled = true;
        fs.shapeType = ParticleSystemShapeType.Cone;
        fs.angle = 4f;
        fs.radius = 0.75f;
        fs.radiusThickness = 0.15f;
        fs.rotation = new Vector3(-90, 0, 0);
        var fv = flames.velocityOverLifetime;
        fv.enabled = true;
        fv.space = ParticleSystemSimulationSpace.Local;
        fv.x = new ParticleSystem.MinMaxCurve(0f, 0f); fv.z = new ParticleSystem.MinMaxCurve(0f, 0f); // one curve mode on all axes
        fv.y = new ParticleSystem.MinMaxCurve(2.4f, 3.4f);
        fv.orbitalY = 2.2f;
        fv.radial = 0.15f;
        Shrink(flames);
        FadeInOut(flames);

        var orbit = Particles(Sub(go, "orbit"), CotsGen.Fx("FxTierPowerSpark", SoftDot(), Color.white, 4.5f));
        orbit.transform.localPosition = new Vector3(0, 1.0f, 0);
        Steady(orbit, 24, 2.4f, 0.07f);
        var om = orbit.main;
        om.simulationSpace = ParticleSystemSimulationSpace.Local; // circles with the body
        om.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.11f);
        om.maxParticles = 120;
        var os = orbit.shape;
        os.enabled = true;
        os.shapeType = ParticleSystemShapeType.Circle;
        os.radius = 1.05f;
        os.radiusThickness = 0.1f;
        os.rotation = new Vector3(90, 0, 0);
        var ov = orbit.velocityOverLifetime;
        ov.enabled = true;
        ov.space = ParticleSystemSimulationSpace.Local;
        ov.x = new ParticleSystem.MinMaxCurve(0f, 0f); ov.z = new ParticleSystem.MinMaxCurve(0f, 0f);
        ov.y = new ParticleSystem.MinMaxCurve(-0.35f, 0.6f);
        ov.orbitalY = 2.8f;
        var onz = orbit.noise;
        onz.enabled = true;
        onz.strength = 0.12f;
        onz.frequency = 1.5f;
        FadeInOut(orbit);

        var rise = Particles(Sub(go, "rise"), CotsGen.Fx("FxTierPowerRise", SoftDot(), Color.white, 3.0f), ParticleSystemRenderMode.Stretch);
        Steady(rise, 18, 0.7f, 0.05f);
        var rm = rise.main;
        rm.simulationSpace = ParticleSystemSimulationSpace.World;
        rm.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.07f);
        rm.maxParticles = 80;
        rise.GetComponent<ParticleSystemRenderer>().velocityScale = 0.08f;
        var rs = rise.shape;
        rs.enabled = true;
        rs.shapeType = ParticleSystemShapeType.Circle;
        rs.radius = 1.3f;
        rs.radiusThickness = 0.4f;
        rs.rotation = new Vector3(90, 0, 0);
        Velocity(rise, new Vector3(0, 5.5f, 0));
        FadeOut(rise);

        CotsGen.SavePrefab(go);
    }

    // ---- prefabs ----

    /// <summary>
    /// Weapon glow (COS-108). Fx_WeaponEdge: a fresnel shell; the mod gives its MeshFilter the weapon's own mesh and tints it
    /// in the rarity colour. Fx_WeaponWisp / _Ember / _Frost: unique-weapon particles from a thin box 1 m along +Y (the mod
    /// sets the box length to the blade), simulated in world space so they hang behind a moving blade.
    /// </summary>
    private static void WeaponGlow()
    {
        var edge = new GameObject("Fx_WeaponEdge");
        CotsGen.Child(edge, "shell", _disc, CotsGen.Fx("FxWeaponEdgeMat", _white, new Color(1, 1, 1, 0.8f), 1.3f, fresnel: 1.8f, pulse: 0.15f, cullBack: true));
        CotsGen.SavePrefab(edge);

        WeaponParticles("Fx_WeaponWisp", rate: 9, life: 1.8f, size: 0.05f, rise: 0.12f, noise: 0.2f, intensity: 1.6f);
        WeaponParticles("Fx_WeaponEmber", rate: 14, life: 1.1f, size: 0.03f, rise: 0.55f, noise: 0.35f, intensity: 2.4f);
        WeaponParticles("Fx_WeaponFrost", rate: 10, life: 1.9f, size: 0.045f, rise: -0.12f, noise: 0.12f, intensity: 1.8f);
    }

    private static void WeaponParticles(string name, float rate, float life, float size, float rise, float noise, float intensity)
    {
        var go = new GameObject(name);
        var ps = Particles(Sub(go, "motes"), CotsGen.Fx(name + "Mat", SoftDot(), Color.white, intensity));
        Steady(ps, rate, life, size);
        var main = ps.main;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.scalingMode = ParticleSystemScalingMode.Shape;
        main.startSize = new ParticleSystem.MinMaxCurve(size * 0.6f, size * 1.3f);
        var shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.position = new Vector3(0, 0.5f, 0);
        shape.scale = new Vector3(0.03f, 1f, 0.03f);
        Velocity(ps, new Vector3(0, rise, 0));
        { var vel = ps.velocityOverLifetime; vel.space = ParticleSystemSimulationSpace.World; } // rise is world up, whatever the blade's angle
        var n = ps.noise;
        n.enabled = true;
        n.strength = noise;
        n.frequency = 0.6f;
        FadeInOut(ps);
        CotsGen.SavePrefab(go);
    }

    /// <summary>
    /// The swing trail of a held weapon type (COS-93): a TrailRenderer the mod parents to the weapon tip, switches on while
    /// the tip moves fast and widens per type. Not emitting by default; width 1 (the mod sets widthMultiplier).
    /// </summary>
    private static void WeaponTrail()
    {
        var swipe = CotsGen.Tex("FxSwipe", 128, (u, v) =>
        {
            // u runs along the trail (0 = at the blade), v across it: bright core near the blade edge, fading behind.
            float across = Mathf.Exp(-Mathf.Pow((v - 0.6f) / 0.28f, 2));
            float along = Mathf.Pow(1 - u, 1.5f);
            return White(Mathf.Clamp01(across * along * 1.2f));
        });
        var go = new GameObject("Fx_WeaponTrail");
        var trail = go.AddComponent<TrailRenderer>();
        trail.sharedMaterial = CotsGen.Fx("FxWeaponTrailMat", swipe, new Color(0.85f, 0.9f, 1f, 0.7f), 1.4f);
        trail.time = 0.16f;
        trail.minVertexDistance = 0.03f;
        trail.widthMultiplier = 1f;
        trail.widthCurve = new AnimationCurve(new Keyframe(0, 1), new Keyframe(1, 0.15f));
        trail.textureMode = LineTextureMode.Stretch;
        trail.alignment = LineAlignment.View;
        trail.emitting = false;
        trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        trail.receiveShadows = false;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
            new[] { new GradientAlphaKey(0.8f, 0), new GradientAlphaKey(0, 1) });
        trail.colorGradient = g;
        CotsGen.SavePrefab(go);
    }

    /// <summary>A lasting ground area or telegraph: faint fill, bright pulsing edge, motes rising from the edge.</summary>
    private static void Ring()
    {
        var go = new GameObject("Fx_Ring");
        CotsGen.Child(go, "fill", _disc, CotsGen.Fx("FxRingFill", _fill, new Color(1, 1, 1, 0.45f), 1.4f, additive: false), new Vector3(0, 0.04f, 0));
        CotsGen.Child(go, "edge", _disc, CotsGen.Fx("FxRingEdge", _ring, Color.white, 4f, pulse: 0.6f), new Vector3(0, 0.06f, 0));
        var ps = Particles(Sub(go, "motes"), CotsGen.Fx("FxMotes", SoftDot(), Color.white, 2.5f));
        var main = ps.main;
        main.startLifetime = 1.3f;
        main.startSpeed = 0;
        main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.11f);
        main.maxParticles = 80;
        { var em = ps.emission; em.rateOverTime = 30; }
        var shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius = 1;
        shape.radiusThickness = 0;
        shape.rotation = new Vector3(90, 0, 0);
        Velocity(ps, new Vector3(0, 0.7f, 0));
        FadeInOut(ps);
        CotsGen.SavePrefab(go);
    }

    /// <summary>A one-shot radial burst: expanding shock ring, flying sparks and a flash (casts, war cries, explosions, hits).</summary>
    private static void Burst()
    {
        var go = new GameObject("Fx_Burst");
        var wave = Particles(Sub(go, "wave"), CotsGen.Fx("FxBurstWave", _ring, Color.white, 2.5f), ParticleSystemRenderMode.HorizontalBillboard);
        OneShot(wave, 1, 0.5f);
        var wm = wave.main;
        wm.startSize = 2f;
        var ws = wave.sizeOverLifetime;
        ws.enabled = true;
        ws.size = new ParticleSystem.MinMaxCurve(1, new AnimationCurve(new Keyframe(0, 0.15f, 0, 3), new Keyframe(1, 1, 0, 0)));
        FadeOut(wave);

        var sparks = Particles(Sub(go, "sparks"), CotsGen.Fx("FxSparks", SoftDot(), Color.white, 3f), ParticleSystemRenderMode.Stretch);
        OneShot(sparks, 36, 0.45f);
        var sm = sparks.main;
        sm.startSpeed = new ParticleSystem.MinMaxCurve(2.5f, 5f);
        sm.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.09f);
        var ss = sparks.shape;
        ss.enabled = true;
        ss.shapeType = ParticleSystemShapeType.Hemisphere;
        ss.radius = 0.15f;
        ss.rotation = new Vector3(-90, 0, 0);
        sparks.GetComponent<ParticleSystemRenderer>().velocityScale = 0.12f;
        Drag(sparks, 3);
        FadeOut(sparks);

        var flash = Particles(Sub(go, "flash"), CotsGen.Fx("FxFlash", SoftDot(), Color.white, 3f));
        OneShot(flash, 1, 0.18f);
        var fm = flash.main;
        fm.startSize = 1.6f;
        flash.transform.localPosition = new Vector3(0, 0.4f, 0);
        FadeOut(flash);
        CotsGen.SavePrefab(go);
    }

    /// <summary>A projectile or held orb: bright core, soft halo, world-space trail.</summary>
    private static void Orb()
    {
        var go = new GameObject("Fx_Orb");
        var core = Particles(Sub(go, "core"), CotsGen.Fx("FxOrbCore", SoftDot(), Color.white, 4f));
        Steady(core, 30, 0.2f, 1.8f);
        var halo = Particles(Sub(go, "halo"), CotsGen.Fx("FxOrbHalo", SoftDot(), Color.white, 0.7f));
        Steady(halo, 10, 0.35f, 4f);
        var trail = Particles(Sub(go, "trail"), CotsGen.Fx("FxOrbTrail", SoftDot(), Color.white, 2f));
        var tm = trail.main;
        tm.simulationSpace = ParticleSystemSimulationSpace.World;
        tm.startLifetime = 0.45f;
        tm.startSpeed = 0;
        tm.startSize = new ParticleSystem.MinMaxCurve(0.8f, 1.2f);
        tm.maxParticles = 150;
        { var em = trail.emission; em.rateOverTime = 25; }
        { var em = trail.emission; em.rateOverDistance = 10; }
        var sh = trail.shape;
        sh.enabled = true;
        sh.shapeType = ParticleSystemShapeType.Sphere;
        sh.radius = 0.4f;
        Shrink(trail);
        FadeOut(trail);
        CotsGen.SavePrefab(go);
    }

    /// <summary>
    /// Ball Lightning, ball radius 1: a white-hot core in a glow, bolts crackling through it in every direction, bolts striking
    /// down from it (2.4 units, to the ground under a ball flying at chest height) and sparks falling away.
    /// </summary>
    private static void BallLightning()
    {
        var go = new GameObject("Fx_BallLightning");
        var core = Particles(Sub(go, "core"), CotsGen.Fx("FxBallCore", SoftDot(), Color.white, 2.2f));
        Steady(core, 30, 0.2f, 1.0f);
        var halo = Particles(Sub(go, "halo"), CotsGen.Fx("FxBallHalo", SoftDot(), Color.white, 0.3f));
        Steady(halo, 10, 0.35f, 2.6f);

        var boltMat = CotsGen.Fx("FxBallBolt", _bolt, Color.white, 5f);
        var arcs = Particles(Sub(go, "arcs"), boltMat);
        var am = arcs.main;
        am.startLifetime = new ParticleSystem.MinMaxCurve(0.05f, 0.13f);
        am.startSpeed = 0;
        am.startSize = new ParticleSystem.MinMaxCurve(2.4f, 4.2f);
        am.startRotation = new ParticleSystem.MinMaxCurve(0, Mathf.PI * 2);
        am.maxParticles = 12;
        { var em = arcs.emission; em.rateOverTime = 26; }
        arcs.GetComponent<ParticleSystemRenderer>().flip = new Vector3(0.5f, 0, 0);
        FadeOut(arcs);

        var sparks = Particles(Sub(go, "sparks"), CotsGen.Fx("FxBallSparks", SoftDot(), Color.white, 3f), ParticleSystemRenderMode.Stretch);
        var pm = sparks.main;
        pm.simulationSpace = ParticleSystemSimulationSpace.World;
        pm.startLifetime = new ParticleSystem.MinMaxCurve(0.3f, 0.55f);
        pm.startSpeed = new ParticleSystem.MinMaxCurve(1.5f, 4f);
        pm.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.1f);
        pm.gravityModifier = 1f;
        pm.maxParticles = 40;
        { var em = sparks.emission; em.rateOverTime = 30; }
        var psh = sparks.shape;
        psh.enabled = true;
        psh.shapeType = ParticleSystemShapeType.Sphere;
        psh.radius = 0.6f;
        sparks.GetComponent<ParticleSystemRenderer>().velocityScale = 0.1f;
        FadeOut(sparks);
        CotsGen.SavePrefab(go);
    }

    /// <summary>A bubble around the caster (shields, parry): fresnel sphere, bright at the rim, clear in the middle.</summary>
    private static void Bubble()
    {
        var go = new GameObject("Fx_Bubble");
        CotsGen.Child(go, "shell", _sphere, CotsGen.Fx("FxBubble", _white, Color.white, 1.6f, fresnel: 2.5f, pulse: 0.8f, cullBack: true));
        CotsGen.SavePrefab(go);
    }

    /// <summary>A ground dome (healing dome, sanctuaries): fresnel hemisphere over a ring with rising motes.</summary>
    private static void Dome()
    {
        var go = new GameObject("Fx_Dome");
        CotsGen.Child(go, "shell", _hemisphere, CotsGen.Fx("FxDome", _white, Color.white, 1.2f, fresnel: 2f, cullBack: true));
        CotsGen.Child(go, "edge", _disc, CotsGen.Fx("FxDomeEdge", _ring, Color.white, 1.8f), new Vector3(0, 0.05f, 0));
        var ps = Particles(Sub(go, "motes"), CotsGen.Fx("FxDomeMotes", SoftDot(), Color.white, 2.5f));
        Steady(ps, 20, 2f, 0.09f);
        var shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius = 0.95f;
        shape.rotation = new Vector3(90, 0, 0);
        Velocity(ps, new Vector3(0, 0.45f, 0));
        FadeInOut(ps);
        CotsGen.SavePrefab(go);
    }

    /// <summary>A fire or arcane column, radius 1 and 1 m tall (the mod scales Y to the height): scrolling core, flames, embers.</summary>
    private static void Column()
    {
        var go = new GameObject("Fx_Column");
        CotsGen.Child(go, "core", _cylinder, CotsGen.Fx("FxColumnCore", _noise, Color.white, 1.8f, fresnel: 1.5f, fresnelInvert: true, scroll: new Vector2(0.15f, -1.1f), fadeEnds: true));
        CotsGen.Child(go, "shell", _cylinder, CotsGen.Fx("FxColumnShell", _noise, new Color(1, 1, 1, 0.35f), 0.8f, fresnel: 2f, fresnelInvert: true, scroll: new Vector2(-0.1f, -0.6f), fadeEnds: true),
            scale: new Vector3(1.25f, 1, 1.25f));
        var flames = Particles(Sub(go, "flames"), CotsGen.Fx("FxColumnFlames", _flame, Color.white, 2f));
        Steady(flames, 40, 0.6f, 0.9f);
        var fs = flames.shape;
        fs.enabled = true;
        fs.shapeType = ParticleSystemShapeType.Circle;
        fs.radius = 0.9f;
        fs.rotation = new Vector3(90, 0, 0);
        Velocity(flames, new Vector3(0, 0.8f, 0));
        FadeInOut(flames);
        var embers = Particles(Sub(go, "embers"), CotsGen.Fx("FxColumnEmbers", SoftDot(), Color.white, 3f));
        Steady(embers, 25, 1f, 0.05f);
        var es = embers.shape;
        es.enabled = true;
        es.shapeType = ParticleSystemShapeType.Circle;
        es.radius = 1.1f;
        es.rotation = new Vector3(90, 0, 0);
        Velocity(embers, new Vector3(0, 1f, 0));
        FadeOut(embers);
        CotsGen.SavePrefab(go);
    }

    /// <summary>A black hole, radius 1 = its pull radius: dark core with a glowing rim, a turning swirl, debris pulled in.</summary>
    private static void BlackHole()
    {
        var go = new GameObject("Fx_BlackHole");
        CotsGen.Child(go, "core", _sphere, CotsGen.Fx("FxHoleCore", _white, new Color(0.02f, 0.01f, 0.03f, 1), 1, additive: false, fresnel: 1.2f, fresnelInvert: true, cullBack: true),
            scale: Vector3.one * 0.22f);
        CotsGen.Child(go, "rim", _sphere, CotsGen.Fx("FxHoleRim", _white, Color.white, 2.5f, fresnel: 3f, cullBack: true), scale: Vector3.one * 0.26f);
        var swirl = Particles(Sub(go, "swirl"), CotsGen.Fx("FxHoleSwirl", _swirl, Color.white, 1.5f));
        Steady(swirl, 1.2f, 2.5f, 1.4f);
        Spin(swirl, 160);
        FadeInOut(swirl);
        var debris = Particles(Sub(go, "debris"), CotsGen.Fx("FxHoleDebris", _smoke, new Color(0.35f, 0.33f, 0.38f, 0.9f), 1, additive: false));
        Steady(debris, 30, 0.9f, 0.12f);
        var dm = debris.main;
        dm.startSpeed = -1.1f; // inward
        var ds = debris.shape;
        ds.enabled = true;
        ds.shapeType = ParticleSystemShapeType.Sphere;
        ds.radius = 1;
        ds.radiusThickness = 0;
        var vel = debris.velocityOverLifetime;
        vel.enabled = true;
        vel.space = ParticleSystemSimulationSpace.Local;
        vel.orbitalY = 2.5f;
        vel.x = vel.y = vel.z = 0;
        Shrink(debris);
        FadeInOut(debris);
        CotsGen.SavePrefab(go);
    }

    /// <summary>A snow storm, radius 1 and 1 m tall (the mod scales Y): falling, drifting flakes and low mist.</summary>
    private static void Snow()
    {
        var go = new GameObject("Fx_Snow");
        var flakes = Particles(Sub(go, "flakes"), CotsGen.Fx("FxSnowFlakes", SoftDot(), Color.white, 1.6f));
        Steady(flakes, 160, 1.6f, 0.06f);
        flakes.transform.localPosition = new Vector3(0, 1, 0);
        var fs = flakes.shape;
        fs.enabled = true;
        fs.shapeType = ParticleSystemShapeType.Circle;
        fs.radius = 1;
        fs.rotation = new Vector3(90, 0, 0);
        Velocity(flakes, new Vector3(0, -0.65f, 0));
        var noise = flakes.noise;
        noise.enabled = true;
        noise.strength = 0.25f;
        noise.frequency = 0.8f;
        FadeInOut(flakes);
        var mist = Particles(Sub(go, "mist"), CotsGen.Fx("FxSnowMist", _smoke, new Color(1, 1, 1, 0.35f), 1, additive: false));
        Steady(mist, 6, 3f, 0.9f);
        var ms = mist.shape;
        ms.enabled = true;
        ms.shapeType = ParticleSystemShapeType.Circle;
        ms.radius = 0.85f;
        ms.rotation = new Vector3(90, 0, 0);
        mist.transform.localPosition = new Vector3(0, 0.1f, 0);
        FadeInOut(mist);
        CotsGen.SavePrefab(go);
    }

    /// <summary>A portal standing on the ground, radius 1, facing +Z: turning swirl, bright ring, motes circling the rim.</summary>
    private static void Portal()
    {
        var go = new GameObject("Fx_Portal");
        var swirl = Particles(Sub(go, "swirl"), CotsGen.Fx("FxPortalSwirl", _swirl, Color.white, 1.8f));
        Steady(swirl, 1.2f, 2.5f, 2f);
        Spin(swirl, 120);
        FadeInOut(swirl);
        swirl.GetComponent<ParticleSystemRenderer>().alignment = ParticleSystemRenderSpace.Local;
        var ring = Particles(Sub(go, "ring"), CotsGen.Fx("FxPortalRing", _ring, Color.white, 2f));
        Steady(ring, 1.2f, 2.5f, 2.15f);
        FadeInOut(ring);
        ring.GetComponent<ParticleSystemRenderer>().alignment = ParticleSystemRenderSpace.Local;
        var motes = Particles(Sub(go, "motes"), CotsGen.Fx("FxPortalMotes", SoftDot(), Color.white, 3f));
        Steady(motes, 30, 1f, 0.07f);
        var ms = motes.shape;
        ms.enabled = true;
        ms.shapeType = ParticleSystemShapeType.Circle;
        ms.radius = 1;
        ms.radiusThickness = 0;
        var vel = motes.velocityOverLifetime;
        vel.enabled = true;
        vel.space = ParticleSystemSimulationSpace.Local;
        vel.orbitalZ = 2f;
        vel.x = vel.y = vel.z = 0;
        FadeInOut(motes);
        go.transform.GetChild(0).localPosition = go.transform.GetChild(1).localPosition = go.transform.GetChild(2).localPosition = new Vector3(0, 1.1f, 0);
        CotsGen.SavePrefab(go);
    }

    /// <summary>An energy beam 0..1 along +Y, radius 1 (the mod stretches it): scrolling core inside a soft glow.</summary>
    private static void Beam()
    {
        var go = new GameObject("Fx_Beam");
        CotsGen.Child(go, "core", _cylinder, CotsGen.Fx("FxBeamCore", _noise, Color.white, 2.5f, fresnel: 1f, fresnelInvert: true, scroll: new Vector2(0.3f, -3f)),
            scale: new Vector3(0.5f, 1, 0.5f));
        CotsGen.Child(go, "glow", _cylinder, CotsGen.Fx("FxBeamGlow", _white, new Color(1, 1, 1, 0.8f), 1f, fresnel: 1.5f, fresnelInvert: true, fadeEnds: true));
        CotsGen.SavePrefab(go);
    }

    /// <summary>A chain 0..1 along +Y (the mod stretches it and sets the link tiling to the length).</summary>
    private static void Chain()
    {
        var go = new GameObject("Fx_Chain");
        CotsGen.Child(go, "links", _cylinder, CotsGen.Fx("FxChainLinks", _chain, new Color(0.75f, 0.75f, 0.8f, 1), 1, additive: false));
        CotsGen.SavePrefab(go);
    }

    /// <summary>Flames rising from a point, radius 1 (hand flames, burning enemies): trailing in world space.</summary>
    private static void Flame()
    {
        var go = new GameObject("Fx_Flame");
        var ps = Particles(Sub(go, "flames"), CotsGen.Fx("FxFlameParticles", _flame, Color.white, 2.2f));
        Steady(ps, 28, 0.55f, 0.9f);
        var m = ps.main;
        m.simulationSpace = ParticleSystemSimulationSpace.World;
        var sh = ps.shape;
        sh.enabled = true;
        sh.shapeType = ParticleSystemShapeType.Sphere;
        sh.radius = 0.4f;
        Velocity(ps, new Vector3(0, 1.4f, 0));
        Shrink(ps);
        FadeInOut(ps);
        CotsGen.SavePrefab(go);
    }

    /// <summary>A turning glyph circle, radius 1, lying flat (the mod rotates it upright for hand glyphs).</summary>
    private static void Glyph()
    {
        var go = new GameObject("Fx_Glyph");
        // Local alignment (not a horizontal billboard) so the mod can stand the glyph up at the hand by rotating it.
        var ps = Particles(Sub(go, "glyph"), CotsGen.Fx("FxGlyph", _glyph, Color.white, 2f));
        ps.GetComponent<ParticleSystemRenderer>().alignment = ParticleSystemRenderSpace.Local;
        Steady(ps, 0.8f, 2.5f, 2f);
        Spin(ps, 45);
        FadeInOut(ps);
        ps.transform.localPosition = new Vector3(0, 0.05f, 0);
        ps.transform.localEulerAngles = new Vector3(90, 0, 0);
        CotsGen.SavePrefab(go);
    }

    /// <summary>A lingering gas cloud, radius 1: slow alpha-blended puffs.</summary>
    private static void Gas()
    {
        var go = new GameObject("Fx_Gas");
        var ps = Particles(Sub(go, "puffs"), CotsGen.Fx("FxGas", _smoke, new Color(1, 1, 1, 0.55f), 1, additive: false));
        Steady(ps, 12, 2.5f, 1.3f);
        var sh = ps.shape;
        sh.enabled = true;
        sh.shapeType = ParticleSystemShapeType.Hemisphere;
        sh.radius = 0.8f;
        sh.rotation = new Vector3(-90, 0, 0);
        var m = ps.main;
        m.startSpeed = 0.15f;
        m.startRotation = new ParticleSystem.MinMaxCurve(0, Mathf.PI * 2);
        Spin(ps, 15);
        FadeInOut(ps);
        CotsGen.SavePrefab(go);
    }

    // ---- particle helpers (also used by CotsLootPrefabs) ----

    public static Texture2D SoftDot() => _dot ??= CotsGen.Tex("FxDot", 64, (u, v) => White(Mathf.Exp(-Mathf.Pow(Dist(u, v) * 2.2f, 2))));

    public static ParticleSystem Particles(GameObject go, Material mat, ParticleSystemRenderMode mode = ParticleSystemRenderMode.Billboard)
    {
        var ps = go.AddComponent<ParticleSystem>();
        var main = ps.main;
        main.playOnAwake = true;
        main.loop = true;
        main.scalingMode = ParticleSystemScalingMode.Hierarchy;
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.startColor = Color.white;
        var shape = ps.shape;
        shape.enabled = false;
        var r = go.GetComponent<ParticleSystemRenderer>();
        r.sharedMaterial = mat;
        r.renderMode = mode;
        r.shadowCastingMode = ShadowCastingMode.Off;
        r.receiveShadows = false;
        r.maxParticleSize = 5; // fraction of the screen; let big rings and swirls fill it close up
        return ps;
    }

    private static GameObject Sub(GameObject parent, string name)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent.transform, false);
        return go;
    }

    private static void Steady(ParticleSystem ps, float rate, float life, float size)
    {
        var m = ps.main;
        m.startLifetime = life;
        m.startSpeed = 0;
        m.startSize = size;
        m.maxParticles = Mathf.CeilToInt(rate * life * 1.5f) + 4;
        m.prewarm = true;
        { var em = ps.emission; em.rateOverTime = rate; }
    }

    private static void OneShot(ParticleSystem ps, int count, float life)
    {
        var m = ps.main;
        m.loop = false;
        m.duration = life;
        m.startLifetime = life;
        m.startSpeed = 0;
        m.maxParticles = count;
        var e = ps.emission;
        e.rateOverTime = 0;
        e.SetBursts(new[] { new ParticleSystem.Burst(0, (short)count) });
    }

    private static void Velocity(ParticleSystem ps, Vector3 v)
    {
        var vel = ps.velocityOverLifetime;
        vel.enabled = true;
        vel.space = ParticleSystemSimulationSpace.Local;
        vel.x = v.x;
        vel.y = v.y;
        vel.z = v.z;
    }

    private static void Drag(ParticleSystem ps, float drag)
    {
        var l = ps.limitVelocityOverLifetime;
        l.enabled = true;
        l.drag = drag;
    }

    private static void Spin(ParticleSystem ps, float degPerSecond)
    {
        var m = ps.main;
        m.startRotation = new ParticleSystem.MinMaxCurve(0, Mathf.PI * 2);
        var rot = ps.rotationOverLifetime;
        rot.enabled = true;
        rot.z = degPerSecond * Mathf.Deg2Rad;
    }

    private static void Shrink(ParticleSystem ps)
    {
        var s = ps.sizeOverLifetime;
        s.enabled = true;
        s.size = new ParticleSystem.MinMaxCurve(1, AnimationCurve.Linear(0, 1, 1, 0.1f));
    }

    private static void FadeOut(ParticleSystem ps) => Fade(ps, new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(0, 1) });

    private static void FadeInOut(ParticleSystem ps) =>
        Fade(ps, new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(1, 0.25f), new GradientAlphaKey(1, 0.7f), new GradientAlphaKey(0, 1) });

    private static void Fade(ParticleSystem ps, GradientAlphaKey[] alpha)
    {
        var col = ps.colorOverLifetime;
        col.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) }, alpha);
        col.color = g;
    }

    // ---- textures and meshes ----

    private static float Dist(float u, float v) => Mathf.Sqrt((u - 0.5f) * (u - 0.5f) + (v - 0.5f) * (v - 0.5f)) * 2;
    private static float Frac(float x) => x - Mathf.Floor(x);
    private static Color White(float a) => new(1, 1, 1, a);

    /// <summary>Our own rune circle: two rings, tick marks and twelve simple angular marks between them.</summary>
    private static Color Glyph(float u, float v)
    {
        float r = Dist(u, v);
        float ang = Mathf.Atan2(v - 0.5f, u - 0.5f);
        float a = Line(r, 0.93f, 0.018f) + Line(r, 0.7f, 0.014f) + Line(r, 0.36f, 0.012f) * 0.8f;
        float sector = Frac(ang / (Mathf.PI * 2) * 12 + 0.5f);
        // Marks in the band between 0.72 and 0.9: alternating a bar and a chevron per sector.
        int idx = Mathf.FloorToInt(Frac(ang / (Mathf.PI * 2) + 0.5f) * 12);
        if (r > 0.74f && r < 0.89f)
        {
            float t = (r - 0.74f) / 0.15f;
            float mark = idx % 2 == 0 ? Line(sector, 0.5f, 0.035f) : Line(sector, 0.5f + (t - 0.5f) * 0.35f, 0.03f) + Line(sector, 0.5f - (t - 0.5f) * 0.35f, 0.03f);
            a += Mathf.Clamp01(mark);
        }
        // Spokes from the inner ring to the middle ring.
        if (r > 0.38f && r < 0.68f) a += Line(Frac(ang / (Mathf.PI * 2) * 6 + 0.5f), 0.5f, 0.02f) * 0.7f;
        return White(Mathf.Clamp01(a) * (r < 0.97f ? 1 : 0));
    }

    private static float Line(float x, float at, float width) => Mathf.Exp(-Mathf.Pow((x - at) / width, 2));

    /// <summary>A flat disc in XZ, radius 1, planar UVs (0.5, 0.5 at the centre), both faces drawn by the shader.</summary>
    private static Mesh Disc()
    {
        const int seg = 64;
        var v = new List<Vector3> { Vector3.zero };
        var uv = new List<Vector2> { new(0.5f, 0.5f) };
        var n = new List<Vector3> { Vector3.up };
        var t = new List<int>();
        for (int i = 0; i <= seg; i++)
        {
            float a = i * Mathf.PI * 2 / seg;
            v.Add(new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)));
            uv.Add(new Vector2(0.5f + Mathf.Cos(a) * 0.5f, 0.5f + Mathf.Sin(a) * 0.5f));
            n.Add(Vector3.up);
            if (i > 0) t.AddRange(new[] { 0, i + 1, i });
        }
        var m = new Mesh { name = "FxDiscMesh" };
        m.SetVertices(v);
        m.SetUVs(0, uv);
        m.SetNormals(n);
        m.SetTriangles(t, 0);
        return CotsGen.SaveMesh(m);
    }

    private static Mesh Sphere(string name, int lon, int lat, bool upperHalf)
    {
        var v = new List<Vector3>();
        var uv = new List<Vector2>();
        var t = new List<int>();
        int rows = lat;
        for (int y = 0; y <= rows; y++)
        {
            float phi = upperHalf ? Mathf.PI / 2 * y / rows : Mathf.PI * y / rows - Mathf.PI / 2;
            for (int x = 0; x <= lon; x++)
            {
                float th = Mathf.PI * 2 * x / lon;
                v.Add(new Vector3(Mathf.Cos(phi) * Mathf.Cos(th), Mathf.Sin(phi), Mathf.Cos(phi) * Mathf.Sin(th)));
                uv.Add(new Vector2((float)x / lon, (float)y / rows));
            }
        }
        for (int y = 0; y < rows; y++)
            for (int x = 0; x < lon; x++)
            {
                int a = y * (lon + 1) + x, b = a + lon + 1;
                t.AddRange(new[] { a, b, a + 1, a + 1, b, b + 1 });
            }
        var m = new Mesh { name = name };
        m.SetVertices(v);
        m.SetUVs(0, uv);
        m.SetNormals(v);
        m.SetTriangles(t, 0);
        return CotsGen.SaveMesh(m);
    }

    /// <summary>
    /// Fx_Bolt, one lightning bolt from the origin (where it lands) up to y 1 (where it starts), 1 wide: a quad in the XY
    /// plane. The mod stretches it from Ball Lightning to its target for a blink and turns its face to the camera (COS-211).
    /// </summary>
    private static void Bolt()
    {
        var go = new GameObject("Fx_Bolt");
        var v = new List<Vector3>();
        var uv = new List<Vector2>();
        var n = new List<Vector3>();
        var t = new List<int>();
        foreach (var side in new[] { Vector3.right })
        {
            int b = v.Count;
            v.AddRange(new[] { -side * 0.5f, side * 0.5f, -side * 0.5f + Vector3.up, side * 0.5f + Vector3.up });
            uv.AddRange(new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1) });
            var normal = Vector3.Cross(side, Vector3.up);
            n.AddRange(new[] { normal, normal, normal, normal });
            t.AddRange(new[] { b, b + 2, b + 1, b + 1, b + 2, b + 3 });
        }
        var m = new Mesh { name = "FxBoltMesh" };
        m.SetVertices(v);
        m.SetUVs(0, uv);
        m.SetNormals(n);
        m.SetTriangles(t, 0);
        CotsGen.Child(go, "bolt", CotsGen.SaveMesh(m), CotsGen.Fx("FxBoltStrike", _bolt, Color.white, 3.5f));
        CotsGen.SavePrefab(go);
    }

    /// <summary>An open cylinder, radius 1, from y 0 to 1; U around, V up (the scroll runs along V).</summary>
    private static Mesh Cylinder()
    {
        const int seg = 32;
        var v = new List<Vector3>();
        var uv = new List<Vector2>();
        var n = new List<Vector3>();
        var t = new List<int>();
        for (int i = 0; i <= seg; i++)
        {
            float a = i * Mathf.PI * 2 / seg;
            var d = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
            v.Add(d);
            v.Add(d + Vector3.up);
            n.Add(d);
            n.Add(d);
            uv.Add(new Vector2((float)i / seg * 2, 0));
            uv.Add(new Vector2((float)i / seg * 2, 1));
            if (i < seg)
            {
                int b = i * 2;
                t.AddRange(new[] { b, b + 1, b + 2, b + 2, b + 1, b + 3 });
            }
        }
        var m = new Mesh { name = "FxCylinderMesh" };
        m.SetVertices(v);
        m.SetUVs(0, uv);
        m.SetNormals(n);
        m.SetTriangles(t, 0);
        return CotsGen.SaveMesh(m);
    }
}
