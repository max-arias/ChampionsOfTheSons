using ChampionsOfTheSons.Core.Content;
using RedLoader;
using Sons.Items.Core;
using Sons.Player;
using SonsSdk;
using TheForest.Items.Inventory;
using TheForest.Utils;
using UnityEngine;

namespace ChampionsOfTheSons.Mod.Weapons;

/// <summary>
/// COS-185 spike: a weapon type as a real game item. The long sword is registered in the game's item database as a copy
/// of the machete's item data, with its own id, weapon numbers and a held prefab cloned from the machete's (animations,
/// hit logic, sounds) that carries our model. The player can then own and hold it without any vanilla weapon.
/// </summary>
internal static class RealWeapons
{
    /// <summary>Game item id of the long sword. Far above the game's ids (under 1000) and the SDK example range (8000).</summary>
    public const int LongSwordId = 47101;
    public const int MacheteId = 359;
    public const string TypeId = "long_sword";

    public static ItemData? Item { get; private set; }

    /// <summary>The held template (inactive, kept across scenes): what the game instantiates in the hand.</summary>
    public static Transform? HeldTemplate { get; private set; }

    /// <summary>Spike log lines: when and how each step went.</summary>
    public static readonly List<string> Notes = new();

    /// <summary>Whether the item was registered before the player first spawned (save loading needs that).</summary>
    public static bool RegisteredBeforeWorld { get; private set; }

    private static GameObject? _templateRoot;
    private static IntPtr _propsDone;
    private static bool _registerFailed;

    public static bool IsReal(int itemId) => itemId == LongSwordId;

    private static void Note(string s)
    {
        Notes.Add($"[{Time.realtimeSinceStartup:0.0}s] {s}");
        RLog.Msg("Champions of the Sons: real weapons: " + s);
    }

    /// <summary>Registers the item as soon as the game's item database exists (menu or world).</summary>
    public static void TryRegister(bool inWorld)
    {
        if (Item != null || _registerFailed) return;
        if (ItemDatabaseManager._instance == null) return;
        try
        {
            if (ItemTools.IsItemRegistered(LongSwordId))
            {
                _registerFailed = true;
                Note($"id {LongSwordId} is already taken: {ItemDatabaseManager.ItemById(LongSwordId)?.Name}");
                return;
            }
            var machete = ItemDatabaseManager.ItemById(MacheteId);
            if (machete == null)
            {
                Note("no machete item data yet");
                return;
            }
            var data = UnityEngine.Object.Instantiate(machete);
            data.name = "CotsLongSwordItemData";
            data._id = LongSwordId;
            data._name = "CotsLongSword";
            data._editorName = "CotsLongSword";
            data._excludeFromSpawnAll = true;
            data._uiData._itemId = LongSwordId;
            data._uiData._title = "Long Sword";
            data._uiData._translationKey = null;
            ItemTools.RegisterItem(data);
            Item = data;
            RegisteredBeforeWorld = !inWorld;
            Note($"registered {LongSwordId} ({(inWorld ? "in world" : "before the world")}); machete held prefab {(machete._heldPrefab != null ? machete._heldPrefab.name : "null")}, "
                 + $"type {data._type}, hotkeyable {data._canBeHotKeyed}, equip slot {data._equipmentSlot}, melee data {(data.MeleeWeaponData != null ? $"dmg {data.MeleeWeaponData.Damage}" : "null")}, "
                 + $"same data object as machete {(data.MeleeWeaponData != null && machete.MeleeWeaponData != null && data.MeleeWeaponData.Pointer == machete.MeleeWeaponData.Pointer)}");
        }
        catch (Exception e)
        {
            _registerFailed = true;
            Note("register failed: " + e);
        }
    }

    /// <summary>In the world: builds the held template (needs the bundle) and adds the hand locator for this player body.</summary>
    public static void EnsureWorld()
    {
        if (Item == null) return;
        try
        {
            if (HeldTemplate == null) BuildTemplate();
            var props = LocalPlayer.Inventory?.InventoryProps;
            if (props == null || props.Pointer == _propsDone) return;
            InventoryProps.PropDefinition? machete = null;
            foreach (var d in props._propDefinitions)
                if (d != null && d._itemId == MacheteId) { machete = d; break; }
            if (machete?._heldLocator == null)
            {
                Note("no machete prop definition on this player");
                _propsDone = props.Pointer;
                return;
            }
            var src = machete._heldLocator;
            var loc = new GameObject("CotsLongSwordHeldLocator").transform;
            loc.SetParent(src.parent, false);
            loc.localPosition = src.localPosition;
            loc.localRotation = src.localRotation;
            loc.localScale = src.localScale;
            props._propDefinitions.Add(new InventoryProps.PropDefinition { _heldLocator = loc, _itemId = LongSwordId });
            _propsDone = props.Pointer;
            Note($"held locator added next to {src.name} under {src.parent?.name}");
        }
        catch (Exception e)
        {
            _propsDone = LocalPlayer.Inventory?.InventoryProps?.Pointer ?? IntPtr.Zero;
            Note("world setup failed: " + e);
        }
    }

    private static void BuildTemplate()
    {
        var src = ItemDatabaseManager.ItemById(MacheteId)?._heldPrefab;
        var type = ModContent.Db.WeaponTypes.TryGetValue(TypeId, out var t) ? t : null;
        if (src == null || type == null)
        {
            Note($"no template: machete held prefab {src != null}, type {type != null}");
            return;
        }
        _templateRoot = new GameObject("cots-real-weapon-templates");
        _templateRoot.SetActive(false); // nothing under it wakes up (no Awake/Start on the clone)
        UnityEngine.Object.DontDestroyOnLoad(_templateRoot);
        var held = UnityEngine.Object.Instantiate(src.gameObject, _templateRoot.transform);
        held.name = "CotsLongSwordHeld";
        int ids = 0;
        foreach (var id in held.GetComponentsInChildren<HeldItemIdentifier>(true)) { id._itemId = LongSwordId; ids++; }

        // The held controller gets its weapon numbers from the item data when it spawns (the prefab has none), and the
        // item data's copy is our own (Instantiate copies it): set the type's numbers there once.
        var ctl = held.GetComponentInChildren<Sons.Weapon.MeleeWeaponController>(true);
        var data = Item!.MeleeWeaponData;
        string before = data != null ? $"dmg {data.Damage} swing {data.SwingSpeed} stamina {data.StaminaCost}" : "none";
        bool sharedWithItem = ctl?._meleeWeaponData != null;
        if (data != null && type.Damage is double dmg)
        {
            data.Damage = (float)dmg;
            data.SwingSpeed *= (float)type.SwingSpeed;
            data.SwingSpeedTired *= (float)type.SwingSpeed;
            data.StaminaCost *= (float)type.StaminaCost;
        }

        // Our model on the clone, the game mesh off, the hit box stretched: the same work the overlay does on a live weapon.
        var skin = new WeaponSkin(held.transform, type);
        skin.Apply(stretchReach: true);
        WeaponFx.Skins.Remove(skin); // a template, not a weapon in the world
        _data = data;
        HeldTemplate = held.transform;
        Item!._heldPrefab = held.transform;
        Note($"template from {src.name}: {ids} identifier(s), controller {(ctl != null ? ctl.GetIl2CppType().Name : "none")}, data {before} -> "
             + $"{(data != null ? $"dmg {data.Damage} swing {data.SwingSpeed:0.00} stamina {data.StaminaCost:0.00}" : "none")}, prefab controller has its own data {sharedWithItem}, model {skin.Source}");
    }

    private static Sons.Items.Core.MeleeWeaponData? _data;
    private static IntPtr _dataSetOn;

    /// <summary>Where the live controller's weapon numbers came from before we set ours (spike log).</summary>
    public static string DataSource { get; private set; } = "";

    /// <summary>
    /// A few times a second on our held weapon: renderers the game adds after it spawns (late meshes) stay off, and the
    /// controller gets the type's weapon numbers (it is handed the machete's when it spawns).
    /// </summary>
    public static void KeepGameMeshOff()
    {
        var root = LocalPlayer.Transform;
        if (root == null || Item == null) return;
        foreach (var id in root.GetComponentsInChildren<HeldItemIdentifier>(false))
        {
            if (id == null || !IsReal(id._itemId)) continue;
            var ctl = id.GetComponent<Sons.Weapon.MeleeWeaponController>();
            if (ctl != null && _data != null && ctl.Pointer != _dataSetOn)
            {
                var had = ctl._meleeWeaponData;
                var machete = ItemDatabaseManager.ItemById(MacheteId)?.MeleeWeaponData;
                DataSource = had == null ? "none" : $"dmg {had.Damage}, same object as the machete item's {machete != null && had.Pointer == machete.Pointer}, as ours {had.Pointer == _data.Pointer}, as our item's now {Item.MeleeWeaponData != null && had.Pointer == Item.MeleeWeaponData.Pointer}";
                ctl._meleeWeaponData = _data;
                ctl.ApplySwingSpeed();
                _dataSetOn = ctl.Pointer;
                Note("live controller data was " + DataSource);
            }
            foreach (var r in id.GetComponentsInChildren<Renderer>(true))
            {
                if (r == null || !r.enabled || r.TryCast<ParticleSystemRenderer>() != null || IsOurs(r.transform, id.transform)) continue;
                r.enabled = false;
                LateMeshes++;
            }
        }
    }

    private static readonly int RightIdParam = Animator.StringToHash("RightHandHeldItemId");
    private static readonly int RightIdFloatParam = Animator.StringToHash("RightHandHeldItemIdFloat");

    /// <summary>Frames the animator was moved onto the base weapon's id (self-test).</summary>
    public static int AnimatorFixes { get; private set; }

    /// <summary>
    /// Every frame: the player animator chooses weapon states by the held item's id, and knows nothing of ours, so while
    /// our weapon is in the right hand it is told the base weapon's id (the machete's animations play).
    /// </summary>
    public static void KeepAnimatorOnBase()
    {
        var inv = LocalPlayer.Inventory;
        var right = inv != null ? inv.RightHandItem : null;
        if (right == null || !IsReal(right._itemID)) return;
        var anim = LocalPlayer.Animator;
        if (anim == null || anim.GetInteger(RightIdParam) == MacheteId) return;
        anim.SetInteger(RightIdParam, MacheteId);
        anim.SetFloat(RightIdFloatParam, MacheteId);
        AnimatorFixes++;
    }

    /// <summary>Swing events of our held weapon (self-test).</summary>
    public static int Swings { get; private set; }

    /// <summary>Swing events of any held melee weapon (self-test control: the machete).</summary>
    public static int AllSwings { get; private set; }

    public static void OnSwing(Sons.Weapon.MeleeWeaponController controller)
    {
        AllSwings++;
        var id = controller.GetComponent<HeldItemIdentifier>();
        if (id != null && IsReal(id._itemId)) Swings++;
    }

    /// <summary>Game renderers switched off after spawn (self-test: a late mesh shows up here).</summary>
    public static int LateMeshes { get; private set; }

    public static bool IsOurs(Transform t, Transform holder)
    {
        for (var p = t; p != null && p != holder; p = p.parent)
            if (p.name.StartsWith("cots-", StringComparison.Ordinal)) return true;
        return false;
    }
}

/// <summary>COS-185 spike wiring: registration, world setup and the late-mesh keeper.</summary>
public sealed class RealWeaponsFeature : IModFeature
{
    private float _scan;

    public void Update(float dt, bool inWorld)
    {
        RealWeapons.TryRegister(inWorld);
        if (!inWorld) return;
        RealWeapons.EnsureWorld();
        RealWeapons.KeepAnimatorOnBase();
        _scan -= dt;
        if (_scan > 0) return;
        _scan = 0.2f;
        RealWeapons.KeepGameMeshOff();
    }

    public void RegisterSelfTests() => RealWeaponsSelfTest.Register();
}
