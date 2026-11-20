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
    private static Texture2D _white = null!, _ring = null!, _fill = null!, _smoke = null!, _flame = null!, _glyph = null!, _noise = null!, _swirl = null!, _chain = null!;

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
        _chain = CotsGen.Tex("FxChain", 64, (u, v) =>
        {
            // One oval link per tile, seen from the side.
            float x = (u - 0.5f) * 2.2f, y = (Frac(v) - 0.5f) * 2f;
            float d = Mathf.Abs(Mathf.Sqrt(x * x * 2.2f + y * y) - 0.75f);
            return White(1 - CotsGen.Smooth(0.08f, 0.2f, d));
        }, repeat: true);

        _disc = Disc();
        _sphere = Sphere("FxSphereMesh", 24, 16, false);
        _hemisphere = Sphere("FxHemisphereMesh", 24, 8, true);
        _cylinder = Cylinder();

        Ring();
        Burst();
        Orb();
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
    }

    // ---- prefabs ----

    /// <summary>A lasting ground area or telegraph: faint fill, bright pulsing edge, motes rising from the edge.</summary>
    private static void Ring()
    {
        var go = new GameObject("Fx_Ring");
        CotsGen.Child(go, "fill", _disc, CotsGen.Fx("FxRingFill", _fill, new Color(1, 1, 1, 0.3f), 1, additive: false), new Vector3(0, 0.04f, 0));
        CotsGen.Child(go, "edge", _disc, CotsGen.Fx("FxRingEdge", _ring, Color.white, 2.2f, pulse: 0.6f), new Vector3(0, 0.06f, 0));
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
        CotsGen.Child(go, "core", _cylinder, CotsGen.Fx("FxColumnCore", _noise, Color.white, 1.8f, fresnel: 1.5f, fresnelInvert: true, scroll: new Vector2(0.15f, -1.1f)));
        CotsGen.Child(go, "shell", _cylinder, CotsGen.Fx("FxColumnShell", _noise, new Color(1, 1, 1, 0.6f), 1f, fresnel: 2f, fresnelInvert: true, scroll: new Vector2(-0.1f, -0.6f)),
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
        CotsGen.Child(go, "glow", _cylinder, CotsGen.Fx("FxBeamGlow", _white, new Color(1, 1, 1, 0.8f), 1f, fresnel: 1.5f, fresnelInvert: true));
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
