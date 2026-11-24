using ChampionsOfTheSons.Core.Content;
using ChampionsOfTheSons.Core.Rules;
using ChampionsOfTheSons.Mod.Audio;
using RedLoader;
using Sons.Weapon;
using UnityEngine;

namespace ChampionsOfTheSons.Mod.Weapons;

/// <summary>
/// Dresses a held weapon item of ours (COS-93, COS-186) in its type's model. The item is a copy of a game weapon, which
/// stays in charge of animations, hits and sounds: the game weapon's renderers (spawned with it, and some later) are
/// switched off, our model is parented to its root (which follows the hand bone), and its hit volume (the BoxCollider next
/// to <c>weaponInfo</c>) is stretched by the type's reach. Used for the local player and for remote players' held weapons.
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

    /// <summary>COS-207: set when the item is held as the game's own weapon, tinted ("#RRGGBB"); null for the type's model.</summary>
    public readonly string? GameTint;

    /// <summary>The tint of an item that is held as the game's own weapon (null: it shows its type's model).</summary>
    public static string? GameTintFor(string? baseId) =>
        baseId != null && ModContent.Db.Items.TryGetValue(baseId, out var def) && !string.IsNullOrEmpty(def.GameModelTint) ? def.GameModelTint : null;

    /// <summary>What a weapon shows: the item's tint over the game mesh, the game mesh as it is for a type that has no model
    /// of ours (<see cref="Untinted"/>, COS-209), or null for the type's model.</summary>
    public static string? GameLookFor(WeaponTypeDef? type, string? baseId) => GameTintFor(baseId) ?? (type?.GameModel == true ? Untinted : null);

    public const string Untinted = "";

    private readonly List<Renderer> _shown = new();
    /// <summary>Glow parts hung on the game weapon's own mesh (they go with the skin).</summary>
    private readonly List<GameObject> _gameGlow = new();
    private readonly HashSet<IntPtr> _shownSet = new();
    private readonly List<(Material Mat, Color? Color, float? Smoothness, float? Metallic)> _tinted = new();

    public WeaponSkin(Transform holder, WeaponTypeDef type, GlowLook look = default, string? gameTint = null)
    {
        Holder = holder;
        Type = type;
        GameTint = gameTint;
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
        var frame = GameFrame();
        if (GameTint != null)
        {
            // The game's own weapon, rusted: nothing of ours is drawn, but the skin still stands for the weapon in hand.
            ShowGameMesh();
            Model = new GameObject(ModelName);
            Model.transform.SetParent(Holder, false);
            Source = "game:" + (frame?.Source ?? "mesh");
            Placement = GameTint == Untinted ? "the game's own weapon" : "the game's own weapon, tinted " + GameTint;
            // The swing trail at the game weapon's tip, and the rarity light (the edge glow needs a model of ours).
            if (Type.TrailWidth > 0 && frame is { } gf)
                AddTrail(gf.Hand + gf.Along * (((float)Type.Length - (float)Type.Grip) * 0.92f / Mathf.Max(1e-4f, Holder.lossyScale.z)));
            // COS-210: the rarity glow on the game's own mesh (edge shell, light, particles), as on our models.
            GlowParts = WeaponGlowFx.ApplyToGameMesh(Holder, Model, Look, _gameGlow);
            if (Trail != null) WeaponGlowFx.Brighten(Trail, Look);
            Finish(stretchReach);
            return;
        }
        HideGameRenderers();
        Model = BuildModel(Type, out var source);
        Source = source;
        // Measured while the model still sits at the world origin: its bounds are its own-space bounds.
        var b = Bounds(Model);
        var t = Model.transform;
        t.SetParent(Holder, false);
        Model.name = ModelName;
        bool bow = Type.Base == WeaponBase.Bow;
        // Melee models point their head along +Y with the flat facing +Z and are laid along the game weapon's own axis.
        var rot = bow ? BowRotation(b) : MeleeRotation(frame);
        t.localRotation = rot;
        float holderScale = Mathf.Max(1e-4f, Holder.lossyScale.z);
        float len = bow ? Mathf.Max(b.size.x, b.size.y) : b.size.y;
        float s = len > 1e-4f ? (float)Type.Length / (len * holderScale) : 1;
        t.localScale = Vector3.one * s;
        // The hand holds the model Grip metres up from the end of the handle (bows: at the grip, which the game's bow has at
        // the holder origin). The hand is where the game holds its own weapon: on that weapon's axis, at the hand locator.
        // The handle is not always in the middle of the model's width (an axe's blade pushes the middle over).
        var anchor = bow ? BowGrip(b) : new Vector3(b.center.x + (float)Type.HandleOffset * b.size.x, b.min.y, b.center.z);
        var hand = bow || frame == null ? Vector3.zero : frame.Value.Hand;
        var along = bow ? Vector3.zero : frame?.Along ?? Vector3.forward;
        t.localPosition = hand - along * ((float)Type.Grip / holderScale) - rot * (anchor * s);
        Placement = bow ? "bow" : frame == null ? "no game mesh: along +Z from the holder origin"
            : $"along {frame.Value.Along}, flat {frame.Value.Flat}, head side {frame.Value.Side}, hand {frame.Value.Hand.ToString("0.000")} ({frame.Value.Source})";
        if (Type.TrailWidth > 0 && !bow) AddTrail(new Vector3(b.center.x, b.max.y - 0.08f * b.size.y, b.center.z));
        GlowParts = WeaponGlowFx.Apply(Model, Look, light: true);
        if (Trail != null) WeaponGlowFx.Brighten(Trail, Look);
        // After the glow: the string is plain, not an edge that glows.
        if (bow) String = BowString.Create(Model, b, Type.GripDepth);
        Finish(stretchReach);
    }

    /// <summary>The type's rules on the game weapon: its reach and whether it cuts trees.</summary>
    private void Finish(bool stretchReach)
    {
        if (stretchReach) StretchReach(WeaponModels.WantReach(Type));
        if (!WeaponTypes.CanCutTrees(Type))
            foreach (var info in Holder.GetComponentsInChildren<weaponInfo>(true))
            {
                _treeFlags.Add((info, info.noTreeCut));
                info.noTreeCut = true;
            }
    }

    /// <summary>COS-207: shows the game weapon's own mesh (our items keep it off) with the tint over its colour and its shine
    /// taken down, so it reads as worn and rusty. Each renderer once; called again for meshes the game adds later.</summary>
    private const float TintGain = 2.6f;

    private void ShowGameMesh()
    {
        bool plain = GameTint == Untinted;
        if (!ColorUtility.TryParseHtmlString(GameTint, out var tint)) tint = new Color(0.55f, 0.35f, 0.23f);
        foreach (var r in Holder.GetComponentsInChildren<Renderer>(true))
        {
            if (r == null || r.TryCast<ParticleSystemRenderer>() != null || Keeps(r.transform) || !_shownSet.Add(r.Pointer)) continue;
            _shown.Add(r);
            r.enabled = true;
            if (plain) continue; // the game's weapon as it is
            foreach (var m in r.materials)
            {
                if (m == null) continue;
                Color? color = m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor") : null;
                float? smooth = m.HasProperty("_Smoothness") ? m.GetFloat("_Smoothness") : null;
                float? metal = m.HasProperty("_Metallic") ? m.GetFloat("_Metallic") : null;
                _tinted.Add((m, color, smooth, metal));
                // Brighter than a plain multiply: the game's blades are dark steel, and rust on them must still read as rust.
                if (color is { } c) m.SetColor("_BaseColor", new Color(c.r * tint.r * TintGain, c.g * tint.g * TintGain, c.b * tint.b * TintGain, c.a));
                if (smooth is { } s) m.SetFloat("_Smoothness", s * 0.25f);
                if (metal is { } mt) m.SetFloat("_Metallic", mt * 0.5f);
            }
        }
    }

    /// <summary>
    /// COS-196: where the hand holds a bow model, in the model's own space. The prefab's origin is the middle of its bounds,
    /// but a recurve's grip is not there: it sits off the middle in depth (the axis the arrow flies along), so the hand closed
    /// beside the grip. The type says how far, as a fraction of the model's depth (<see cref="WeaponTypeDef.GripDepth"/>,
    /// measured from the model's vertices at mid-length; the bundle's meshes can't be read in the game).
    /// </summary>
    private Vector3 BowGrip(Bounds b)
    {
        int limb = b.size.x >= b.size.y ? 0 : 1, other = limb == 0 ? 1 : 0;
        int depth = b.size[other] >= b.size.z ? other : 2;
        var grip = b.center;
        grip[depth] += (float)Type.GripDepth * b.size[depth];
        return grip;
    }

    /// <summary>A bow's string (COS-205); null for other weapons.</summary>
    public BowString? String { get; private set; }

    /// <summary>After the body's animation each frame: the bow string follows the drawing hand.</summary>
    public void LateTick()
    {
        if (String != null && Holder != null) String.Tick(Holder);
    }

    /// <summary>How the model was laid onto the game weapon (log, self-test).</summary>
    public string Placement { get; private set; } = "";

    /// <summary>How the game holds its own weapon, in the holder's space: the direction the weapon points (handle to head),
    /// the direction its flat side faces, the side its head or edge leans to (zero: neither), and where the hand is.</summary>
    private readonly record struct Frame(Vector3 Along, Vector3 Flat, Vector3 Side, Vector3 Hand, string Source);

    /// <summary>
    /// COS-203: reads the game weapon's own mesh, since the game's held weapons do not agree on an axis or on where the hand
    /// is: the machete and the spear lie along the holder's Z with the hand at the origin (machete) or far down the shaft
    /// (spear), while the club, katana and axe lie along Y with the hand up to 26 cm from the origin. The weapon points
    /// along its mesh's longest side, away from the hand; its flat side faces along the thinnest; the hand is the held
    /// locator the holder hangs under. Null when the game mesh is not there (yet).
    /// </summary>
    private Frame? GameFrame()
    {
        Renderer? main = null;
        Vector3 lo = default, hi = default;
        float best = 0;
        foreach (var r in Holder.GetComponentsInChildren<Renderer>(true))
        {
            if (r == null || r.TryCast<ParticleSystemRenderer>() != null || Keeps(r.transform)) continue;
            if (r.TryCast<MeshRenderer>() == null && r.TryCast<SkinnedMeshRenderer>() == null) continue;
            var lb = r.localBounds;
            var at = r.TryCast<SkinnedMeshRenderer>() is { rootBone: not null } smr ? smr.rootBone : r.transform;
            Vector3 a = Holder.InverseTransformPoint(at.TransformPoint(lb.min)), c = Holder.InverseTransformPoint(at.TransformPoint(lb.max));
            Vector3 mn = Vector3.Min(a, c), mx = Vector3.Max(a, c);
            float longest = Mathf.Max(mx.x - mn.x, Mathf.Max(mx.y - mn.y, mx.z - mn.z));
            // A weapon, not a marker far from the hand (the bow's aim indicator) or a speck.
            if (longest < 0.2f || longest > 4f || ((mn + mx) / 2).magnitude > 2f || longest <= best) continue;
            best = longest;
            main = r;
            lo = mn;
            hi = mx;
        }
        if (main == null) return null;
        Vector3 size = hi - lo, centre = (lo + hi) / 2;
        int along = size.x >= size.y && size.x >= size.z ? 0 : size.y >= size.z ? 1 : 2;
        int o1 = (along + 1) % 3, o2 = (along + 2) % 3;
        int flat = size[o1] <= size[o2] ? o1 : o2, side = flat == o1 ? o2 : o1;
        var hand = Holder.parent != null ? Holder.InverseTransformPoint(Holder.parent.position) : Vector3.zero;
        Vector3 Axis(int i, float sign) { var v = Vector3.zero; v[i] = sign; return v; }
        float lean = centre[side] - hand[side];
        return new Frame(Axis(along, centre[along] >= hand[along] ? 1 : -1), Axis(flat, 1),
            Mathf.Abs(lean) > 0.15f * size[side] ? Axis(side, Mathf.Sign(lean)) : Vector3.zero, hand, main.name);
    }

    /// <summary>
    /// COS-196: the game's bow has its limbs along the holder's Y and its string toward -Z (the drawing hand pulls from
    /// z -0.27 to -0.58; measured on the game's own crafted bow). Ours is turned the same way: limbs (the model's longest side)
    /// along Y, and the string side (opposite the grip's, <see cref="WeaponTypeDef.GripDepth"/>) toward -Z. Before, the string
    /// side pointed along X, a quarter turn off, which only looked right while the arms were stuck in another weapon's pose.
    /// </summary>
    private Quaternion BowRotation(Bounds b)
    {
        int limb = b.size.x >= b.size.y ? 0 : 1, other = limb == 0 ? 1 : 0;
        int depth = b.size[other] >= b.size.z ? other : 2;
        Vector3 limbs = Vector3.zero, stringSide = Vector3.zero;
        limbs[limb] = 1;
        stringSide[depth] = Type.GripDepth > 0 ? -1 : 1;
        return Quaternion.LookRotation(Vector3.back, Vector3.up) * Quaternion.Inverse(Quaternion.LookRotation(stringSide, limbs));
    }

    /// <summary>Melee models point their head along +Y with the flat facing +Z; a head or edge that leans to one side leans
    /// along X. Turned so the model lies as the game's weapon does (<see cref="GameFrame"/>); without a game mesh, along the
    /// holder's +Z with the flat facing X, as the machete.</summary>
    private Quaternion MeleeRotation(Frame? frame)
    {
        if (frame is not { } f) return Quaternion.LookRotation(new Vector3(1, 0, 0), new Vector3(0, 0, 1));
        var rot = Quaternion.LookRotation(f.Flat, f.Along);
        // COS-208: the model's head leans away from its handle's side (the type says where the handle is; the model itself is
        // centred, so its bounds cannot). It must lean the way the game weapon's head does, or an axe is held blade backwards.
        float head = Math.Abs(Type.HandleOffset) > 0.01 ? -Mathf.Sign((float)Type.HandleOffset) : 0;
        if (f.Side != Vector3.zero && head != 0 && Vector3.Dot(rot * new Vector3(head, 0, 0), f.Side) < 0)
            rot = Quaternion.LookRotation(-f.Flat, f.Along);
        return rot;
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
        if (Holder == null) return;
        if (GameTint != null) ShowGameMesh();
        else HideGameRenderers();
    }

    public void Remove()
    {
        WeaponFx.Skins.Remove(this);
        // The game weapon's mesh stays off: the holder is one of our items, which never shows the game weapon (COS-187).
        _hidden.Clear();
        // A game mesh we showed and tinted goes back to its own colours, and off again: the next item on this holder may
        // show its type's model.
        foreach (var (mat, color, smooth, metal) in _tinted)
        {
            if (mat == null) continue;
            if (color is { } c) mat.SetColor("_BaseColor", c);
            if (smooth is { } s) mat.SetFloat("_Smoothness", s);
            if (metal is { } mt) mat.SetFloat("_Metallic", mt);
        }
        _tinted.Clear();
        foreach (var g in _gameGlow)
            if (g != null) UnityEngine.Object.Destroy(g);
        _gameGlow.Clear();
        foreach (var r in _shown)
            if (r != null) r.enabled = false;
        _shown.Clear();
        _shownSet.Clear();
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
        String = null;
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

    /// <summary>COS-191: the base damage of a hit with the held mod weapon (the weapon slot item's level and rarity), or null when
    /// no mod weapon is in hand.</summary>
    public static double? ModWeaponDamage =>
        Active != null && PlayerState.Character.Inventory.Get(WeaponTypes.WeaponSlot) is { } item
            ? WeaponTypes.WeaponDamage(Active, item.Level, item.Rarity) : null;

    /// <summary>COS-191: the held mod weapon's own armor pierce per hit (0 without one).</summary>
    public static double ModWeaponPierce =>
        Active != null && PlayerState.Character.Inventory.Get(WeaponTypes.WeaponSlot) is { } item
            ? WeaponTypes.WeaponPierce(item.Level, item.Rarity) : 0;

    /// <summary>Melee damage multiplier for the local player's hits: the mod weapon's damage (COS-191) over the game weapon's
    /// own, so the game's own variations (combo hits) keep their ratio.</summary>
    public static double MeleeDamageFactor
    {
        get
        {
            if (Active == null || Active.Base == WeaponBase.Bow || ModWeaponDamage is not double d || Base == null) return 1;
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
        // Every skin, remote players' too: a bow's string follows its own body's hand.
        foreach (var skin in WeaponFx.Skins) skin.LateTick();
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
        // The weapon in hand is its own type (COS-186); the game's own weapons carry none.
        var type = found is { } f ? RealWeapons.Get(f.ItemId)?.Type : null;

        var look = type != null ? GlowLook.For(PlayerState.Character.Inventory.Get(WeaponTypes.WeaponSlot)) : GlowLook.None;
        string? gameTint = type != null ? WeaponSkin.GameLookFor(type, PlayerState.Character.Inventory.Get(WeaponTypes.WeaponSlot)?.BaseId) : null;
        if (_local != null && (!_local.Alive || type != _local.Type || look.Key != _local.Look.Key || gameTint != _local.GameTint || held == null || held.Pointer != _localHolder
            || (type != null && Mathf.Abs(_local.Reach - WantReach(type)) > 1e-3f)))
        {
            if (_local.Holder != null) _local.Remove();
            _local = null;
        }
        if (_local == null && type != null && held != null)
        {
            try
            {
                _local = new WeaponSkin(held, type, look, gameTint);
                _local.Apply(stretchReach: true);
                _localHolder = held.Pointer;
                RLog.Msg($"Champions of the Sons: {type.Id} on {held.name} ({_local.Source}{(look.Glows ? ", glow " + look.Key : "")}; {_local.Placement})");
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
        KeepSawPowered();
        if (active != Active)
        {
            Active = active;
            Changed?.Invoke(active);
        }
    }

    /// <summary>What the mod chainsaw's power read when it was last topped up (self-test).</summary>
    public static string SawPower { get; private set; } = "";

    /// <summary>COS-209: a mod weapon built on the game's chainsaw never needs a battery: its power is kept full (checked with
    /// the weapon scan, a few times a second).</summary>
    private static void KeepSawPowered()
    {
        var saw = Base != null ? Base.TryCast<ChainsawWeaponController>() : null;
        if (saw == null) return;
        try
        {
            float before = saw._remainingPower;
            if (before < 0.9f) saw._remainingPower = 1f;
            SawPower = $"power {before:0.00} -> {saw._remainingPower:0.00}, has power {saw.HasPower()}, on {saw._isPoweredOn}, full charge {saw._fullChargeDurationMinutes:0.0} min, attack held {saw._anyAttackHeld}";
        }
        catch (Exception e)
        {
            SawPower = "failed: " + e.Message;
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
