using ChampionsOfTheSons.Core.Content;
using ChampionsOfTheSons.Core.Rules;
using RedLoader;
using Sons.Weapon;
using UnityEngine;

namespace ChampionsOfTheSons.Mod.Weapons;

/// <summary>
/// Puts a weapon type's model on a held game weapon (COS-93). The game weapon stays in charge of animations, hits and
/// sounds: its own renderers are switched off, our model is parented to its root (which follows the hand bone), and its
/// hit volume (the BoxCollider next to <c>weaponInfo</c>) is stretched by the type's reach. Everything is put back when
/// the type stops applying. Used for the local player and for remote players' held weapons alike.
/// </summary>
internal sealed class WeaponSkin
{
    public const string ModelName = "cots-weapon-model";

    public readonly Transform Holder;
    public readonly WeaponTypeDef Type;
    public GameObject? Model { get; private set; }
    public string Source { get; private set; } = "";

    private readonly List<Renderer> _hidden = new();
    private readonly List<(BoxCollider Box, Vector3 Size, Vector3 Center)> _boxes = new();

    public WeaponSkin(Transform holder, WeaponTypeDef type)
    {
        Holder = holder;
        Type = type;
    }

    public bool Alive => Holder != null && Model != null;

    public void Apply(bool stretchReach)
    {
        HideGameRenderers();
        Model = BuildModel(Type, out var source);
        Source = source;
        var t = Model.transform;
        t.SetParent(Holder, false);
        Model.name = ModelName;
        bool bow = Type.Base == WeaponBase.Bow;
        if (bow)
        {
            // Bows: limbs along model X, string behind the grip (model -Z); the game's bows have limbs along Y and
            // shoot along +Z. Map model Z → holder Z and model Y (thickness) → holder X.
            t.localRotation = Quaternion.LookRotation(new Vector3(0, 0, 1), new Vector3(1, 0, 0));
        }
        else
        {
            // Our melee models point their blade along +Y with the grip at the origin; the game's held weapons point
            // along +Z with the flat of the blade facing ±X. Map model Y → holder Z and model X (blade width) → holder Y.
            t.localRotation = Quaternion.LookRotation(new Vector3(1, 0, 0), new Vector3(0, 0, 1));
        }
        float meshLen = ModelLength(Model, bow);
        float holderScale = Mathf.Max(1e-4f, Holder.lossyScale.z);
        float s = meshLen > 0 ? (float)Type.Length / (meshLen * holderScale) : 1;
        t.localScale = Vector3.one * s;
        t.localPosition = bow ? Vector3.zero : new Vector3(0, 0, -(float)Type.Grip * s);
        if (stretchReach) StretchReach((float)WeaponTypes.ReachFactor(Type));
    }

    /// <summary>Per frame: the game re-enables renderers on some events (blood, plating): keep them off.</summary>
    public void Keep()
    {
        foreach (var r in _hidden)
            if (r != null && r.enabled) r.enabled = false;
    }

    /// <summary>A few times a second: hide renderers the game added after we attached (a spear spawns its mesh late).</summary>
    public void Rescan()
    {
        if (Holder != null) HideGameRenderers();
    }

    public void Remove()
    {
        foreach (var r in _hidden)
            if (r != null) r.enabled = true;
        _hidden.Clear();
        foreach (var (box, size, center) in _boxes)
            if (box != null) { box.size = size; box.center = center; }
        _boxes.Clear();
        if (Model != null)
        {
            Model.SetActive(false); // Destroy waits for the end of the frame
            Model.name = "cots-removed";
            UnityEngine.Object.Destroy(Model);
        }
        Model = null;
    }

    private void HideGameRenderers()
    {
        foreach (var r in Holder.GetComponentsInChildren<Renderer>(true))
        {
            if (r == null || !r.enabled || r.TryCast<ParticleSystemRenderer>() != null || Keeps(r.transform)) continue;
            r.enabled = false;
            _hidden.Add(r);
        }
    }

    /// <summary>Lengthens the hit boxes along the blade (holder +Z), keeping their near end where it is.</summary>
    private void StretchReach(float factor)
    {
        if (Mathf.Approximately(factor, 1)) return;
        foreach (var info in Holder.GetComponentsInChildren<weaponInfo>(true))
        {
            foreach (var box in info.GetComponents<BoxCollider>())
            {
                _boxes.Add((box, box.size, box.center));
                var size = box.size;
                var center = box.center;
                float grow = size.z * (factor - 1);
                box.size = new Vector3(size.x, size.y, size.z * factor);
                box.center = new Vector3(center.x, center.y, center.z + grow / 2);
            }
        }
    }

    /// <summary>Renderers that stay: our model, and the arrow or spear projectile the game shows while aiming.</summary>
    private bool Keeps(Transform t)
    {
        for (var p = t; p != null && p != Holder; p = p.parent)
        {
            var n = p.name;
            if (n.StartsWith("cots-", StringComparison.Ordinal) || n.Contains("Arrow", StringComparison.OrdinalIgnoreCase)
                || n.Contains("Projectile", StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    /// <summary>Model length in model units: along +Y for melee models, across the limbs (X) for bows.</summary>
    private static float ModelLength(GameObject model, bool bow)
    {
        var mf = model.GetComponentInChildren<MeshFilter>();
        if (mf == null || mf.sharedMesh == null) return 1;
        var size = mf.sharedMesh.bounds.size;
        return bow ? size.x : size.y;
    }

    private static GameObject BuildModel(WeaponTypeDef type, out string source)
    {
        var holder = new GameObject("cots-weapon-tmp");
        var prefab = Bundles.CotsBundle.Spawn(type.Model, holder.transform);
        if (prefab != null)
        {
            prefab.transform.SetParent(null, false);
            UnityEngine.Object.Destroy(holder);
            source = "bundle:" + type.Model;
            return prefab;
        }
        UnityEngine.Object.Destroy(holder);
        var lit = Shader.Find("HDRP/Lit");
        if (type.StandIn != null && lit != null)
        {
            var standIn = GltfStandIn.Create(type.StandIn, lit);
            if (standIn != null)
            {
                source = "standin:" + type.StandIn;
                return standIn;
            }
        }
        source = "primitive";
        return Primitive(type);
    }

    /// <summary>A plain blade: a flat box along +Y on a short handle, for builds without the bundle or stand-ins.</summary>
    private static GameObject Primitive(WeaponTypeDef type)
    {
        var root = new GameObject("cots-weapon-primitive");
        var blade = GameObject.CreatePrimitive(PrimitiveType.Cube);
        UnityEngine.Object.Destroy(blade.GetComponent<Collider>());
        blade.transform.SetParent(root.transform, false);
        blade.transform.localScale = new Vector3(0.08f, 0.8f, 0.015f);
        blade.transform.localPosition = new Vector3(0, 0.45f, 0);
        var grip = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        UnityEngine.Object.Destroy(grip.GetComponent<Collider>());
        grip.transform.SetParent(root.transform, false);
        grip.transform.localScale = new Vector3(0.035f, 0.1f, 0.035f);
        grip.transform.localPosition = new Vector3(0, -0.05f, 0);
        return root;
    }
}

/// <summary>The local player's weapon type and its model in hand.</summary>
internal static class WeaponModels
{
    private static WeaponSkin? _local;
    private static IntPtr _localHolder;
    private static readonly Dictionary<IntPtr, float> GameDamage = new();

    /// <summary>The weapon type in effect for the local player (null: no typed item, or the held weapon doesn't fit).</summary>
    public static WeaponTypeDef? Active { get; private set; }

    /// <summary>The game weapon the active type is built on.</summary>
    public static MeleeWeaponController? Base { get; private set; }

    public static WeaponSkin? LocalSkin => _local;

    /// <summary>Raised when the local active type changes (co-op tells the others).</summary>
    public static event Action<WeaponTypeDef?>? Changed;

    /// <summary>Melee damage multiplier for the local player's hits: the type's damage over the game weapon's own.</summary>
    public static double MeleeDamageFactor
    {
        get
        {
            if (Active?.Damage is not double d || Base == null) return 1;
            var data = Base._meleeWeaponData;
            if (data == null) return 1;
            if (!GameDamage.TryGetValue(data.Pointer, out var game)) GameDamage[data.Pointer] = game = data.Damage;
            return game > 0 ? d / game : 1;
        }
    }

    private static float _scan;

    public static void Tick(float dt)
    {
        _local?.Keep();
        _scan -= dt;
        if (_scan > 0) return;
        _scan = 0.2f;
        Scan();
        _local?.Rescan();
    }

    /// <summary>Finds the held weapon and puts on, swaps or removes the model (self-tests call it directly).</summary>
    public static void Scan()
    {
        var found = HeldWeapons.Local();
        var held = found?.Holder;
        var kind = found?.Kind ?? HeldKind.None;
        var type = WeaponTypes.Active(PlayerState.Character.Inventory, ModContent.Db, kind);

        if (_local != null && (!_local.Alive || type != _local.Type || held == null || held.Pointer != _localHolder))
        {
            if (_local.Holder != null) _local.Remove();
            _local = null;
        }
        if (_local == null && type != null && held != null)
        {
            try
            {
                _local = new WeaponSkin(held, type);
                _local.Apply(stretchReach: true);
                _localHolder = held.Pointer;
                RLog.Msg($"Champions of the Sons: {type.Id} on {held.name} ({_local.Source})");
            }
            catch (Exception e)
            {
                RLog.Error($"Champions of the Sons: weapon model failed: {e}");
                _local = null;
            }
        }
        _local?.Keep();

        var active = _local != null ? type : null;
        Base = active != null && held != null ? held.GetComponent<MeleeWeaponController>() : null;
        if (active != Active)
        {
            Active = active;
            Changed?.Invoke(active);
        }
    }

    public static void Reset()
    {
        _local?.Remove();
        _local = null;
        Active = null;
        Base = null;
    }
}
