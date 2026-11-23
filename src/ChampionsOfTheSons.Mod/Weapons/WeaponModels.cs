using ChampionsOfTheSons.Core.Content;
using ChampionsOfTheSons.Core.Rules;
using ChampionsOfTheSons.Mod.Audio;
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
    private readonly List<(weaponInfo Info, bool NoTreeCut)> _treeFlags = new();

    /// <summary>The item's glow (COS-108).</summary>
    public readonly GlowLook Look;

    /// <summary>Glow parts made (edge shells, light, wisp).</summary>
    public int GlowParts { get; private set; }

    public WeaponSkin(Transform holder, WeaponTypeDef type, GlowLook look = default)
    {
        Holder = holder;
        Type = type;
        Look = look.Style == null ? GlowLook.None : look; // default(GlowLook) has no style // default(GlowLook) has no style
    }

    public bool Alive => Holder != null && Model != null;

    /// <summary>Swing trail at the tip (null: the bundle has no trail prefab or the type has none).</summary>
    public TrailRenderer? Trail { get; private set; }
    private float _trailUntil;

    /// <summary>Swing events seen and whether the trail was ever lit (self-tests).</summary>
    public int Swings { get; private set; }
    public bool TrailLit { get; private set; }

    /// <summary>Pointers of the game weapon controllers under the holder (the swing and shot hooks match on them).</summary>
    public readonly HashSet<IntPtr> Controllers = new();

    public void Apply(bool stretchReach)
    {
        foreach (var c in Holder.GetComponentsInChildren<MeleeWeaponController>(true)) Controllers.Add(c.Pointer);
        foreach (var c in Holder.GetComponentsInChildren<RangedWeaponController>(true)) Controllers.Add(c.Pointer);
        WeaponFx.Skins.Add(this);
        HideGameRenderers();
        Model = BuildModel(Type, out var source);
        Source = source;
        // Measured while the model still sits at the world origin: its bounds are its own-space bounds.
        var b = Bounds(Model);
        var t = Model.transform;
        t.SetParent(Holder, false);
        Model.name = ModelName;
        bool bow = Type.Base == WeaponBase.Bow;
        // Melee models point their head along +Y with the flat facing +Z; the game's held weapons point along +Z with the
        // flat facing X. Bows have their limbs along X and shoot along Z, like the game's. Model Y -> holder Z, model Z -> X.
        // A bow prefab's limbs run along its longest side: X (mapped onto the game bow's Y) or already Y.
        bool limbsX = b.size.x >= b.size.y;
        var rot = !bow ? Quaternion.LookRotation(new Vector3(1, 0, 0), new Vector3(0, 0, 1))
            : limbsX ? Quaternion.LookRotation(new Vector3(0, 0, 1), new Vector3(1, 0, 0)) : Quaternion.identity;
        t.localRotation = rot;
        float holderScale = Mathf.Max(1e-4f, Holder.lossyScale.z);
        float len = bow ? Mathf.Max(b.size.x, b.size.y) : b.size.y;
        float s = len > 1e-4f ? (float)Type.Length / (len * holderScale) : 1;
        t.localScale = Vector3.one * s;
        // The hand (holder origin) holds the model Grip metres up from the end of the handle (bows: from the grip centre).
        var anchor = bow ? b.center : new Vector3(b.center.x, b.min.y, b.center.z);
        t.localPosition = new Vector3(0, 0, -(float)Type.Grip / holderScale) - rot * (anchor * s);
        if (Type.TrailWidth > 0 && !bow) AddTrail(new Vector3(b.center.x, b.max.y - 0.08f * b.size.y, b.center.z));
        GlowParts = WeaponGlowFx.Apply(Model, Look, light: true);
        if (Trail != null) WeaponGlowFx.Brighten(Trail, Look);
        if (stretchReach) StretchReach(WeaponModels.WantReach(Type));
        if (!WeaponTypes.CanCutTrees(Type))
            foreach (var info in Holder.GetComponentsInChildren<weaponInfo>(true))
            {
                _treeFlags.Add((info, info.noTreeCut));
                info.noTreeCut = true;
            }
    }

    /// <summary>Turns the trail on for a swing (the game's swing event), for about one swing's length.</summary>
    public void OnSwing(float seconds)
    {
        Swings++;
        if (Trail == null) return;
        TrailLit = true;
        _trailUntil = Time.time + seconds;
        Trail.emitting = true;
    }

    private void AddTrail(Vector3 tip)
    {
        var at = new GameObject("cots-trail-tip").transform;
        at.SetParent(Model!.transform, false);
        at.localPosition = tip;
        var fx = Bundles.CotsBundle.Spawn("Fx_WeaponTrail", at);
        Trail = fx != null ? fx.GetComponent<TrailRenderer>() : null;
        if (Trail == null) return;
        Trail.widthMultiplier = (float)Type.TrailWidth;
        Trail.emitting = false;
        Trail.Clear();
    }

    /// <summary>Combined renderer bounds in world space (the model's own space while it sits at the origin).</summary>
    private static Bounds Bounds(GameObject go)
    {
        var rs = go.GetComponentsInChildren<Renderer>(true);
        if (rs.Length == 0) return new Bounds(Vector3.zero, Vector3.one);
        var b = rs[0].bounds;
        foreach (var r in rs) b.Encapsulate(r.bounds);
        return b;
    }

    /// <summary>Per frame: the game re-enables renderers on some events (blood, plating): keep them off.</summary>
    public void Keep()
    {
        foreach (var r in _hidden)
            if (r != null && r.enabled) r.enabled = false;
        if (Trail != null && Trail.emitting && Time.time > _trailUntil) Trail.emitting = false;
    }

    /// <summary>A few times a second: hide renderers the game added after we attached (a spear spawns its mesh late).</summary>
    public void Rescan()
    {
        if (Holder != null) HideGameRenderers();
    }

    public void Remove()
    {
        WeaponFx.Skins.Remove(this);
        foreach (var r in _hidden)
            if (r != null) r.enabled = true;
        _hidden.Clear();
        foreach (var (box, size, center) in _boxes)
            if (box != null) { box.size = size; box.center = center; }
        _boxes.Clear();
        foreach (var (info, flag) in _treeFlags)
            if (info != null) info.noTreeCut = flag;
        _treeFlags.Clear();
        if (Model != null)
        {
            Model.SetActive(false); // Destroy waits for the end of the frame
            Model.name = "cots-removed";
            UnityEngine.Object.Destroy(Model);
        }
        Model = null;
        Trail = null;
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
    /// <summary>The reach factor the hit box was stretched by (1: not stretched).</summary>
    public float Reach { get; private set; } = 1;

    private void StretchReach(float factor)
    {
        Reach = factor;
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
        source = "primitive";
        return Primitive();
    }

    /// <summary>A plain blade: a flat box along +Y on a short handle, for builds without the bundle.</summary>
    private static GameObject Primitive()
    {
        var root = new GameObject("cots-weapon-primitive");
        var blade = GameObject.CreatePrimitive(PrimitiveType.Cube);
        UnityEngine.Object.Destroy(blade.GetComponent<Collider>());
        blade.transform.SetParent(root.transform, false);
        blade.transform.localScale = new Vector3(0.08f, 0.8f, 0.015f);
        blade.transform.localPosition = new Vector3(0, 0.5f, 0);
        var grip = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        UnityEngine.Object.Destroy(grip.GetComponent<Collider>());
        grip.transform.SetParent(root.transform, false);
        grip.transform.localScale = new Vector3(0.035f, 0.1f, 0.035f);
        grip.transform.localPosition = new Vector3(0, 0.1f, 0);
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
            // Melee weapons carry their damage in their weapon data; the spear has none, only its hit script's damage.
            var data = Base._meleeWeaponData;
            var info = Base._weaponInfo;
            IntPtr key = data != null ? data.Pointer : info != null ? info.Pointer : IntPtr.Zero;
            if (key == IntPtr.Zero) return 1;
            if (!GameDamage.TryGetValue(key, out var game)) GameDamage[key] = game = data != null ? data.Damage : info!._weaponDamage;
            return game > 0 ? d / game : 1;
        }
    }

    private static float _scan;

    /// <summary>The hit-box stretch for a weapon type: the type's own reach x the player's weapon size (weapon_range, COS-148).</summary>
    public static float WantReach(WeaponTypeDef? type) =>
        type == null || type.Base == WeaponBase.Bow ? 1f
            : (float)(WeaponTypes.ReachFactor(type) * Math.Clamp(PlayerState.Character.Stats.Get(CombatStatIds.WeaponRange), 0.25, 4));

    public static void Tick(float dt)
    {
        WeaponGlowFx.Tick();
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
        // A real weapon item (COS-186) is its own type; a game weapon carries the slot's type only when nothing real exists
        // for it (the overlay route, removed by COS-187).
        var real = found is { } f ? RealWeapons.Get(f.ItemId) : null;
        var type = real != null ? real.Type
            : RealWeapons.ByType(WeaponTypes.Equipped(PlayerState.Character.Inventory, ModContent.Db)?.Id)?.Item != null ? null
            : WeaponTypes.Active(PlayerState.Character.Inventory, ModContent.Db, kind);

        var look = type != null ? GlowLook.For(PlayerState.Character.Inventory.Get(WeaponTypes.WeaponSlot)) : GlowLook.None;
        if (_local != null && (!_local.Alive || type != _local.Type || look.Key != _local.Look.Key || held == null || held.Pointer != _localHolder
            || (type != null && Mathf.Abs(_local.Reach - WantReach(type)) > 1e-3f)))
        {
            if (_local.Holder != null) _local.Remove();
            _local = null;
        }
        if (_local == null && type != null && held != null)
        {
            try
            {
                _local = new WeaponSkin(held, type, look);
                _local.Apply(stretchReach: true);
                _localHolder = held.Pointer;
                RLog.Msg($"Champions of the Sons: {type.Id} on {held.name} ({_local.Source}{(look.Glows ? ", glow " + look.Key : "")})");
            }
            catch (Exception e)
            {
                RLog.Error($"Champions of the Sons: weapon model failed: {e}");
                _local = null;
            }
        }
        _local?.Keep();

        var active = _local != null ? type : null;
        ProjectileDrop.SetNoGravityHolder(active?.NoGravity == true ? _local?.Holder : null);
        Base = active != null && held != null ? held.GetComponent<MeleeWeaponController>() : null;
        if (active != Active)
        {
            Active = active;
            Changed?.Invoke(active);
        }
    }

    public static void Reset()
    {
        ProjectileDrop.SetDrop(true);
        _local?.Remove();
        _local = null;
        Active = null;
        Base = null;
    }
}

/// <summary>
/// Arrows that ignore gravity (COS-93 greatbow, COS-118). The ballistics handler has one global "bullet drop" flag: it is
/// off while the no-gravity bow is held. A per-bullet Harmony patch on <c>BulletHandler.updateVelocity</c> (its
/// <c>ref BulletData</c> argument) crashed the game natively on the first bullet of any firearm, so it is gone (2026-11-22).
/// </summary>
internal static class ProjectileDrop
{
    private static global::Ballistics.BallisticSettings? _settings;
    private static bool? _original;
    private static bool _scanned;
    private static IntPtr _holderKey;

    /// <summary>Whether projectiles drop right now (null: no ballistics handler found yet).</summary>
    public static bool? Drop => Settings()?.BulletDrop;

    /// <summary>The no-gravity bow the local player holds now (null: none): projectiles fly straight while it is held.</summary>
    public static void SetNoGravityHolder(Transform? holder)
    {
        IntPtr key = holder != null ? holder.Pointer : IntPtr.Zero;
        if (key == _holderKey && _scanned) return; // called every scan: only work on a change
        _scanned = true;
        _holderKey = key;
        SetDrop(holder == null);
    }

    public static void SetDrop(bool drop)
    {
        if (drop && _original == null) return; // never switched: nothing to put back
        try
        {
            var settings = Settings();
            if (settings == null) return;
            _original ??= settings.BulletDrop;
            bool want = drop ? _original.Value : false;
            if (settings.BulletDrop != want) settings.BulletDrop = want;
        }
        catch (Exception e)
        {
            RLog.Error($"Champions of the Sons: projectile drop switch failed: {e}");
            _settings = null;
        }
    }

    private static global::Ballistics.BallisticSettings? Settings()
    {
        if (_settings != null) return _settings;
        var handler = UnityEngine.Object.FindObjectOfType<global::Ballistics.BulletHandler>();
        _settings = handler != null ? handler.GetSettings() : null;
        return _settings;
    }
}

/// <summary>
/// Per-type swing and hit sounds and the swing trail, driven by the game's own swing and shot events. The event hooks
/// compare only object pointers: IL2CPP can fold identical methods, so a hook may also run for unrelated objects.
/// </summary>
internal static class WeaponFx
{
    public const float SoundRange = 25f;

    /// <summary>All weapon skins in the world: the local one and remote players' (co-op).</summary>
    public static readonly List<WeaponSkin> Skins = new();

    public static WeaponSkin? SkinOf(IntPtr controller)
    {
        foreach (var s in Skins)
            if (s.Controllers.Contains(controller)) return s;
        return null;
    }

    public static void Swing(IntPtr controller)
    {
        var skin = SkinOf(controller);
        if (skin == null || skin.Holder == null) return;
        // About one swing: the game's swing animation is near 0.45 s at normal speed.
        skin.OnSwing(0.45f / (float)Math.Max(0.3, skin.Type.SwingSpeed));
        ModAudio.PlayAt(skin.Type.SwingSound, skin.Holder.position, SoundRange, 0.8f, (float)skin.Type.SoundPitch);
        LastSwingCue = ModAudio.LastCue;
    }

    /// <summary>The cue the last swing event left playing (self-tests).</summary>
    public static string? LastSwingCue { get; private set; }

    /// <summary>The local player's melee hit on an enemy.</summary>
    public static void LocalHit()
    {
        var skin = WeaponModels.LocalSkin;
        if (skin?.Holder == null || skin.Type.Base == WeaponBase.Bow) return;
        ModAudio.PlayAt(skin.Type.HitSound, skin.Holder.position, SoundRange, 0.9f, (float)skin.Type.SoundPitch);
        WeaponGlowFx.HitSparks(skin);
    }
}
