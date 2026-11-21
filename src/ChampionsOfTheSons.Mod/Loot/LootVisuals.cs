using ChampionsOfTheSons.Core;
using ChampionsOfTheSons.Core.Rules;
using ChampionsOfTheSons.Mod.UI;
using UnityEngine;

namespace ChampionsOfTheSons.Mod.Loot;

/// <summary>
/// World objects for ground drops (Max 2026-11-21: keep it simple): the item type's model from the asset bundle lying on the
/// ground (COS-63; a rarity-tinted gem when the bundle or model is missing), a ring in the rarity's colour around it, and for
/// Rare and Legendary a short line rising from the ring over and over. No name plate. Near the end of its lifetime it blinks.
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
        public LineRenderer? Ring;
        public Transform? Pulse;
        public float PulseHeight, PulsePeriod = 1, PulsePhase;
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
        v.Ring = Ring(root.transform, pos, color);
        // Rare and Legendary only: a short line that keeps rising from the ring (Max 2026-11-21).
        if (drop.Item.Rarity >= Rarity.Rare)
        {
            var pulse = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Prepare(pulse, root.transform, color);
            v.Pulse = pulse.transform;
            v.PulseHeight = drop.Item.Rarity == Rarity.Legendary ? 3.2f : 2.2f;
            v.PulsePeriod = drop.Item.Rarity == Rarity.Legendary ? 0.9f : 1.2f;
            v.PulsePhase = (drop.Id * 0.37f) % 1f;
        }
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

    /// <summary>The rarity ring around the drop, its points set on the ground so it follows slopes.</summary>
    private static LineRenderer? Ring(Transform parent, Vector3 center, Color color)
    {
        var go = new GameObject("ring");
        go.transform.SetParent(parent, false);
        var line = go.AddComponent<LineRenderer>();
        if (line == null) return null;
        const int Points = 28;
        line.useWorldSpace = true;
        line.loop = true;
        line.positionCount = Points;
        line.widthMultiplier = 0.045f;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        line.receiveShadows = false;
        var unlit = FindUnlit();
        var mat = unlit != null ? new Material(unlit) : line.material;
        mat.SetColor("_UnlitColor", color);
        mat.color = color;
        line.material = mat;
        line.startColor = line.endColor = color;
        for (int i = 0; i < Points; i++)
        {
            float a = i * Mathf.PI * 2f / Points;
            var p = center + new Vector3(Mathf.Cos(a) * RingRadius, 0, Mathf.Sin(a) * RingRadius);
            p.y = GroundNear(p, center.y) + 0.04f;
            line.SetPosition(i, p);
        }
        return line;
    }

    public const float RingRadius = 0.55f;

    /// <summary>The ground height at <paramref name="p"/> closest to <paramref name="y"/> (the drop's own ground), within half a metre.</summary>
    private static float GroundNear(Vector3 p, float y)
    {
        float best = y, bestDist = 0.5f;
        foreach (var h in Physics.RaycastAll(new Vector3(p.x, y + 0.5f, p.z), Vector3.down, 1f, ~0, QueryTriggerInteraction.Ignore))
        {
            float d = Mathf.Abs(h.point.y - y);
            if (d < bestDist) { best = h.point.y; bestDist = d; }
        }
        return best;
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
        if (v.Pulse != null)
        {
            float t = (time / v.PulsePeriod + v.PulsePhase) % 1f;
            const float Length = 0.55f;
            float y = 0.05f + t * v.PulseHeight;
            float width = Mathf.Lerp(0.05f, 0.015f, t);
            v.Pulse.localScale = new Vector3(width, Length / 2f, width);
            v.Pulse.position = v.Position + Vector3.up * (y + Length / 2f);
        }
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
