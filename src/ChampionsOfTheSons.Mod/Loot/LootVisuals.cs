using ChampionsOfTheSons.Core;
using ChampionsOfTheSons.Core.Rules;
using ChampionsOfTheSons.Mod.UI;
using UnityEngine;

namespace ChampionsOfTheSons.Mod.Loot;

/// <summary>
/// World objects for ground drops (Max 2026-11-21: keep it simple): the item type's model from the asset bundle lying on the
/// ground (COS-63; a rarity-tinted gem when the bundle or model is missing) in a beam of its rarity's colour (Fx_LootBeam: a glow
/// pool, a beam fading upward, rising sparks; small and short for Common and Magic, tall with a ring for Rare and Legendary).
/// No name plate. Near the end of its lifetime it blinks.
/// </summary>
internal static class LootVisuals
{
    internal sealed class Visual
    {
        public int Id;
        public GameObject Root = null!;
        public Transform Gem = null!;
        public Color Color;
        public Rarity Rarity;
        public Vector3 Position;
        /// <summary>The root's scale when created (the fade shrinks from it).</summary>
        public float Scale = 1;
        /// <summary>The Fx_LootBeam effect (glow pool, beam, sparks; the ring on Rare and Legendary).</summary>
        public GameObject? Beam;
        public bool HasRing;
        public bool Shown = true;
    }

    private static readonly Dictionary<int, Visual> ById = new();
    private static Shader? _unlit;
    private static bool _shaderLooked;

    public static IReadOnlyDictionary<int, Visual> All => ById;

    public static Color ColorOf(Rarity r) => ColorUtility.TryParseHtmlString(UiTheme.RarityHex(r), out var c) ? c : Color.white;

    public static void Create(GroundDrop drop)
    {
        Destroy(drop.Id);
        var pos = new Vector3(drop.X, drop.Y, drop.Z);
        var color = ColorOf(drop.Item.Rarity);
        // Size grows with rarity on the old 0..7 scale, so a Legendary drop is as big as a Primal one was (COS-109).
        float rarity = (int)drop.Item.Rarity * 7f / Rarities.Max;
        const float DropScale = 0.9f; // Max (2026-11-20): drops 10% smaller

        var root = new GameObject("cots-drop-" + drop.Id);
        root.transform.position = pos;

        // The item type's model when the asset bundle has it (COS-63), else the bundled crystal marker (COS-5), else a primitive gem.
        GameObject? gem = null;
        if (DropPrefabFor(drop.Item.BaseId) is string model && Bundles.CotsBundle.Spawn(model, root.transform) is GameObject m)
        {
            gem = m;
            foreach (var r in gem.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            gem.transform.localPosition = new Vector3(0, 0.6f, 0);
            gem.transform.localScale = Vector3.one * (DropScale * (1f + 0.05f * rarity));
            gem.transform.localRotation = Quaternion.Euler(0, (drop.Id * 47) % 360, 0);
            LieOnGround(gem.transform, pos.y, tip: true);
            // Top-rarity and unique weapons glow on the ground as in hand (COS-108).
            Weapons.WeaponGlowFx.Apply(gem, Weapons.GlowLook.For(drop.Item), light: false);
        }
        if (gem == null) gem = Bundles.CotsBundle.Spawn(Bundles.CotsBundle.LootMarker, root.transform);
        if (gem != null && gem.name == Bundles.CotsBundle.LootMarker)
        {
            Tint(gem, color);
            gem.transform.localPosition = new Vector3(0, 0.4f, 0);
            gem.transform.localScale = Vector3.one * (DropScale * (1f + 0.1f * rarity));
            LieOnGround(gem.transform, pos.y, tip: false);
        }
        else if (gem == null)
        {
            gem = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Prepare(gem, root.transform, color);
            gem.transform.localPosition = new Vector3(0, 0.4f, 0);
            gem.transform.localScale = Vector3.one * (DropScale * (0.22f + 0.03f * rarity));
            gem.transform.localRotation = Quaternion.Euler(0, 45, 0);
            LieOnGround(gem.transform, pos.y, tip: false);
        }
        var v = new Visual { Id = drop.Id, Root = root, Gem = gem.transform, Color = color, Rarity = drop.Item.Rarity, Position = pos };
        Beam(v, root.transform, pos, drop.Item.Rarity, color);
        ById[drop.Id] = v;
        Animate(v, 0, null);
    }

    /// <summary>Sets a drop's lowest point on the ground; with <paramref name="tip"/>, a long upright model (a sword, a bow) is first laid on its side.</summary>
    private static void LieOnGround(Transform model, float groundY, bool tip)
    {
        if (Bounds(model) is not { } b) return;
        if (tip && b.size.y > b.size.x && b.size.y > b.size.z) model.localRotation = Quaternion.Euler(90, model.localEulerAngles.y, 0);
        if (Bounds(model) is { } after) model.position += Vector3.up * (groundY + 0.02f - after.min.y);
    }

    private static Bounds? Bounds(Transform t)
    {
        Bounds? all = null;
        foreach (var r in t.GetComponentsInChildren<Renderer>())
        {
            if (r is ParticleSystemRenderer || !r.enabled) continue;
            if (all is { } a) { a.Encapsulate(r.bounds); all = a; }
            else all = r.bounds;
        }
        return all;
    }

    public const string LootBeam = "Fx_LootBeam";

    /// <summary>How a rarity's beam looks (Max 2026-11-21, after his reference picture): every rarity gets the glow pool, beam and sparks,
    /// Common and Magic small and short, Rare and Legendary tall with a ring at the base.</summary>
    internal readonly record struct BeamLook(float Height, float Size, float Sparks, float Streaks, bool Ring);

    internal static BeamLook LookOf(Rarity r) => r switch
    {
        Rarity.Common => new(0.8f, 0.7f, 4, 0, false),
        Rarity.Magic => new(1.3f, 0.8f, 7, 1, false),
        Rarity.Rare => new(2.6f, 1.0f, 14, 3, true),
        _ => new(3.8f, 1.2f, 24, 5, true),
    };

    private static void Beam(Visual v, Transform parent, Vector3 pos, Rarity rarity, Color color)
    {
        var look = LookOf(rarity);
        var fx = Visuals.FxKit.Spawn(LootBeam, pos, look.Size, color, parent);
        if (fx == null) return;
        foreach (var t in fx.GetComponentsInChildren<Transform>(true))
        {
            switch (t.name)
            {
                case "beam" or "core":
                    t.localScale = new Vector3(t.localScale.x, look.Height / look.Size, t.localScale.z);
                    break;
                case "ring":
                    t.gameObject.SetActive(look.Ring);
                    break;
            }
        }
        foreach (var ps in fx.GetComponentsInChildren<ParticleSystem>(true))
        {
            var em = ps.emission;
            var main = ps.main;
            if (ps.gameObject.name == "sparks")
            {
                em.rateOverTimeMultiplier = look.Sparks * (Visuals.FxKit.Reduced ? 0.5f : 1f);
                main.startLifetimeMultiplier = Mathf.Max(0.5f, look.Height * 0.45f / look.Size);
            }
            else if (ps.gameObject.name == "streaks")
            {
                em.rateOverTimeMultiplier = look.Streaks;
                main.startLifetimeMultiplier = look.Height / (2.7f * look.Size);
                if (look.Streaks <= 0) ps.Clear();
            }
        }
        // The hot core reads whiter than the beam around it.
        foreach (var r in fx.GetComponentsInChildren<Renderer>(true))
            if (r.gameObject.name == "core") Visuals.FxKit.Tint(r.gameObject, Color.Lerp(color, Color.white, 0.55f));
        v.Beam = fx;
        v.HasRing = look.Ring;
    }



    /// <summary>Every drop model prefab (self-tests).</summary>
    public static readonly string[] DropPrefabs =
    {
        "Drop_Sword", "Drop_GreatSword", "Drop_Axe", "Drop_Hammer", "Drop_Bow", "Drop_Polearm", "Drop_Shield",
        "Drop_Quiver", "Drop_Scroll", "Drop_Armor", "Drop_Jewelry", "Drop_Material", "Drop_Consumable",
    };

    /// <summary>
    /// The bundled model for an item: weapons by their weapon model, the rest by item type. No armor or jewelry models
    /// exist in the free sets we use, so armor drops show a chest, jewelry a coin, spell scrolls a key, materials a small
    /// chest and consumables (hearts) a flask.
    /// </summary>
    public static string? DropPrefabFor(string baseId)
    {
        if (!ModContent.Db.Items.TryGetValue(baseId, out var def)) return null;
        return def.Type switch
        {
            "weapon" => def.WeaponModel switch
            {
                "great_sword" => "Drop_GreatSword",
                "axe" => "Drop_Axe",
                "hammer" => "Drop_Hammer",
                "greatbow" => "Drop_Bow",
                "polearm" => "Drop_Polearm",
                _ => "Drop_Sword",
            },
            "shield" => "Drop_Shield",
            "quiver" => "Drop_Quiver",
            "spell_scroll" => "Drop_Scroll",
            "helmet" or "chest_armor" or "pants" or "boot" or "glove" or "shoulder_armor" or "bracer" => "Drop_Armor",
            "ring" or "amulet" => "Drop_Jewelry",
            "material" => "Drop_Material",
            "other" => "Drop_Consumable",
            _ => null,
        };
    }

    public static void Destroy(int id)
    {
        if (!ById.Remove(id, out var v)) return;
        // The effect's materials are per-instance (tinted): free them with the drop.
        if (v.Beam != null)
            foreach (var r in v.Beam.GetComponentsInChildren<Renderer>(true))
                if (r.sharedMaterial != null) UnityEngine.Object.Destroy(r.sharedMaterial);
        if (v.Root != null) UnityEngine.Object.Destroy(v.Root);
    }

    /// <summary>Seconds over which a drop shrinks away before it despawns (COS-96).</summary>
    public const float FadeSeconds = 2f;

    /// <summary>Seconds before despawning when a drop starts to blink (Max 2026-11-21: it blinks, faster and dimmer as it runs out).</summary>
    public const float BlinkSeconds = 20f;

    /// <summary>Whether a drop with <paramref name="remaining"/> seconds left is shown at <paramref name="time"/>: always until the last
    /// <see cref="BlinkSeconds"/>, then on and off, the blink getting faster (1 s to 0.15 s) and the visible part shorter (75% to 35%).</summary>
    public static bool BlinkOn(double remaining, float time)
    {
        if (remaining >= BlinkSeconds) return true;
        float left = Mathf.Clamp01((float)remaining / BlinkSeconds);
        float period = Mathf.Lerp(0.15f, 1f, left);
        float on = Mathf.Lerp(0.35f, 0.75f, left);
        return time / period % 1f < on;
    }

    /// <summary>Per frame: the rising line, the expiry blink and the last-seconds shrink.</summary>
    public static void Animate(GroundLoot ground)
    {
        foreach (var v in ById.Values) Animate(v, Time.time, ground.Get(v.Id));
    }

    private static void Animate(Visual v, float time, GroundDrop? drop)
    {
        if (v.Root == null) return;
        if (drop == null) return;
        bool shown = BlinkOn(drop.Remaining, time);
        if (shown != v.Shown)
        {
            v.Shown = shown;
            foreach (var r in v.Root.GetComponentsInChildren<Renderer>(true)) r.enabled = shown;
        }
        if (drop.Remaining < FadeSeconds)
            v.Root.transform.localScale = Vector3.one * Mathf.Max(0.05f, (float)drop.Remaining / FadeSeconds) * v.Scale;
    }

    /// <summary>Rarity color on a bundled mesh (HDRP/Lit "_BaseColor", other shaders "_Color").</summary>
    private static void Tint(GameObject go, Color color)
    {
        foreach (var r in go.GetComponentsInChildren<Renderer>())
        {
            var m = r.material;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color);
            else m.color = color;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
        foreach (var c in go.GetComponentsInChildren<Collider>()) UnityEngine.Object.Destroy(c);
    }

    private static void Prepare(GameObject go, Transform parent, Color color)
    {
        go.transform.SetParent(parent, false);
        var col = go.GetComponent<Collider>();
        if (col != null) UnityEngine.Object.Destroy(col); // never block the player or bullets
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
