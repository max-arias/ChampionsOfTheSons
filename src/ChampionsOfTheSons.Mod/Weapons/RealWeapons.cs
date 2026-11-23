using ChampionsOfTheSons.Core.Content;
using ChampionsOfTheSons.Core.Rules;
using RedLoader;
using Sons.Items.Core;
using Sons.Player;
using SonsSdk;
using TheForest.Items.Inventory;
using TheForest.Utils;
using UnityEngine;

namespace ChampionsOfTheSons.Mod.Weapons;

/// <summary>
/// Weapon types as real game items (COS-185, COS-186). Each type is registered in the game's item database as a copy of
/// its game weapon's item data (the type's <c>gameItem</c>), with its own id and a held prefab cloned
/// from the base's (animations, hit logic, sounds). The weapon slot gives and takes the item (<see cref="WeaponSlotSync"/>);
/// the model, glow, reach and numbers go on the held clone the same way as before (<see cref="WeaponModels"/>,
/// <c>StatBridge</c>, <c>EnemyPatches</c>).
/// </summary>
internal static class RealWeapons
{
    public const int MacheteId = 359;

    /// <summary>One registered weapon item.</summary>
    internal sealed class Weapon
    {
        public readonly string TypeId;
        public readonly int Id;
        public int BaseId;
        public ItemData? Item;
        public Transform? Template;
        public Weapon(string typeId, int id) { TypeId = typeId; Id = id; }
        public WeaponTypeDef? Type => ModContent.Db.WeaponTypes.TryGetValue(TypeId, out var t) ? t : null;
    }

    /// <summary>
    /// Game item ids per type: fixed, far above the game's ids (under 1000) and the SDK example range (8000). Never derive
    /// them from content order: they end up on hotkeys and must stay the same across versions.
    /// </summary>
    public static readonly IReadOnlyList<Weapon> All = new Weapon[]
    {
        new("long_sword", 47101), new("great_sword", 47102), new("hammer", 47103),
        new("axe", 47104), new("polearm", 47105), new("greatbow", 47106),
    };

    private static readonly Dictionary<int, Weapon> ById = All.ToDictionary(w => w.Id);

    public static Weapon? ByType(string? typeId) => typeId == null ? null : All.FirstOrDefault(w => w.TypeId == typeId);
    public static Weapon? Get(int itemId) => ById.TryGetValue(itemId, out var w) ? w : null;
    public static bool IsReal(int itemId) => ById.ContainsKey(itemId);

    /// <summary>Whether the items were registered before the player first spawned (save loading needs that).</summary>
    public static bool RegisteredBeforeWorld { get; private set; }

    public static bool Registered { get; private set; }

    /// <summary>Log lines: when and how each step went (self-test).</summary>
    public static readonly List<string> Notes = new();

    private static GameObject? _templateRoot;
    private static IntPtr _propsDone;
    private static bool _registerFailed;

    private static void Note(string s)
    {
        Notes.Add($"[{Time.realtimeSinceStartup:0.0}s] {s}");
        RLog.Msg("Champions of the Sons: real weapons: " + s);
    }

    /// <summary>The held kind of a weapon item: its type's base.</summary>
    public static HeldKind KindOf(int itemId) => Get(itemId)?.Type?.Base switch
    {
        WeaponBase.Melee => HeldKind.Melee,
        WeaponBase.Spear => HeldKind.Spear,
        WeaponBase.Bow => HeldKind.Bow,
        _ => HeldKind.Other,
    };

    /// <summary>Registers the items as soon as the game's item database and the content exist (menu, world or server).</summary>
    public static void TryRegister(bool inWorld)
    {
        if (Registered || _registerFailed) return;
        if (ItemDatabaseManager._instance == null || ModContent.Db?.WeaponTypes == null || ModContent.Db.WeaponTypes.Count == 0) return;
        try
        {
            foreach (var w in All)
            {
                if (ItemTools.IsItemRegistered(w.Id))
                {
                    Note($"id {w.Id} is already taken: {ItemDatabaseManager.ItemById(w.Id)?.Name}");
                    continue;
                }
                var type = w.Type;
                if (type == null) { Note($"no weapon type {w.TypeId}"); continue; }
                int gameId = GameItems.Find(type.GameItem, -1);
                var baseData = gameId >= 0 ? ItemDatabaseManager.ItemById(gameId) : null;
                if (baseData?._heldPrefab == null)
                {
                    Note($"{w.TypeId}: game item '{type.GameItem}' missing or has no held prefab{(type.Base == WeaponBase.Melee ? ", using the machete" : "")}");
                    baseData = type.Base == WeaponBase.Melee ? ItemDatabaseManager.ItemById(MacheteId) : null;
                }
                if (baseData == null) { Note($"{w.TypeId}: no base item data"); continue; }
                var data = UnityEngine.Object.Instantiate(baseData);
                data.name = "Cots_" + w.TypeId;
                data._id = w.Id;
                data._name = "Cots_" + w.TypeId;
                data._editorName = data._name;
                data._excludeFromSpawnAll = true;
                // The slot owns the item: nothing to drop (no pickup to leave behind), not on death, not on unequip.
                data._pickupPrefab = null;
                data._pickupBundlePrefab = null;
                data._pickupCanBeBundled = false;
                data._dropsOnDeath = false;
                data._alwaysDropOnUnequip = false;
                data._uiData._itemId = w.Id;
                data._uiData._title = w.TypeId;
                data._uiData._translationKey = null;
                ItemTools.RegisterItem(data);
                w.BaseId = baseData._id;
                w.Item = data;
                Note($"{w.TypeId}: registered {w.Id} on {baseData.Name} ({baseData._id}){(inWorld ? " in world" : "")}");
            }
            Registered = true;
            RegisteredBeforeWorld = !inWorld;
        }
        catch (Exception e)
        {
            _registerFailed = true;
            Note("register failed: " + e);
        }
    }

    /// <summary>In the world: builds the held templates and adds the hand locators for this player body.</summary>
    public static void EnsureWorld()
    {
        if (!Registered) return;
        try
        {
            if (_templateRoot == null) BuildTemplates();
            var props = LocalPlayer.Inventory?.InventoryProps;
            if (props == null || props.Pointer == _propsDone) return;
            _propsDone = props.Pointer;
            foreach (var w in All)
            {
                if (w.Item == null) continue;
                InventoryProps.PropDefinition? src = null;
                foreach (var d in props._propDefinitions)
                    if (d != null && d._itemId == w.BaseId) { src = d; break; }
                if (src?._heldLocator == null)
                {
                    Note($"{w.TypeId}: no prop definition for base {w.BaseId} on this player");
                    continue;
                }
                var at = src._heldLocator;
                var loc = new GameObject($"Cots_{w.TypeId}_HeldLocator").transform;
                loc.SetParent(at.parent, false);
                loc.localPosition = at.localPosition;
                loc.localRotation = at.localRotation;
                loc.localScale = at.localScale;
                props._propDefinitions.Add(new InventoryProps.PropDefinition { _heldLocator = loc, _itemId = w.Id });
            }
        }
        catch (Exception e)
        {
            Note("world setup failed: " + e);
        }
    }

    private static void BuildTemplates()
    {
        _templateRoot = new GameObject("cots-real-weapon-templates");
        _templateRoot.SetActive(false); // nothing under it wakes up (no Awake/Start on the clones)
        UnityEngine.Object.DontDestroyOnLoad(_templateRoot);
        foreach (var w in All)
        {
            var src = w.Item != null ? ItemDatabaseManager.ItemById(w.BaseId)?._heldPrefab : null;
            if (src == null) { Note($"{w.TypeId}: no held prefab on base {w.BaseId}"); continue; }
            var held = UnityEngine.Object.Instantiate(src.gameObject, _templateRoot.transform);
            held.name = $"Cots_{w.TypeId}_Held";
            foreach (var id in held.GetComponentsInChildren<HeldItemIdentifier>(true)) id._itemId = w.Id;
            w.Template = held.transform;
            w.Item!._heldPrefab = held.transform;
        }
        Note($"held templates built: {All.Count(w => w.Template != null)}/{All.Count}");
    }

    private static readonly int RightIdParam = Animator.StringToHash("RightHandHeldItemId");
    private static readonly int RightIdFloatParam = Animator.StringToHash("RightHandHeldItemIdFloat");

    /// <summary>Times an animator was moved onto a base weapon's id (self-test).</summary>
    public static int AnimatorFixes { get; private set; }

    private static readonly List<Animator> RemoteAnimators = new();

    /// <summary>Co-op: a remote player's body holds one of our weapons; its animator gets the same fix.</summary>
    public static void TrackRemote(Transform holder)
    {
        var anim = holder != null ? holder.GetComponentInParent<Animator>() : null;
        if (anim == null) return;
        RemoteAnimators.RemoveAll(a => a == null);
        if (!RemoteAnimators.Any(a => a.Pointer == anim.Pointer)) RemoteAnimators.Add(anim);
    }

    /// <summary>
    /// Every frame: the player animator chooses weapon states by the held item's id and knows nothing of ours, so while one
    /// of our weapons is in the right hand it is told the base weapon's id (the base's animations play). Remote bodies too.
    /// </summary>
    public static void KeepAnimatorsOnBase()
    {
        Fix(LocalPlayer.Animator);
        for (int i = RemoteAnimators.Count - 1; i >= 0; i--)
        {
            var a = RemoteAnimators[i];
            if (a == null) { RemoteAnimators.RemoveAt(i); continue; }
            Fix(a);
        }
    }

    private static void Fix(Animator? anim)
    {
        if (anim == null) return;
        var w = Get(anim.GetInteger(RightIdParam));
        if (w == null || w.BaseId <= 0) return;
        anim.SetInteger(RightIdParam, w.BaseId);
        anim.SetFloat(RightIdFloatParam, w.BaseId);
        AnimatorFixes++;
    }

    /// <summary>Swing events of our held weapons (self-test).</summary>
    public static int Swings { get; private set; }

    public static void OnSwing(Sons.Weapon.MeleeWeaponController controller)
    {
        var id = controller.GetComponent<HeldItemIdentifier>();
        if (id != null && IsReal(id._itemId)) Swings++;
    }
}

/// <summary>Wiring of the real weapon items: registration, world setup, the animator fix and the slot sync.</summary>
public sealed class RealWeaponsFeature : IModFeature
{
    private float _sync;

    public void Update(float dt, bool inWorld)
    {
        RealWeapons.TryRegister(inWorld);
        if (!inWorld) return;
        RealWeapons.EnsureWorld();
        RealWeapons.KeepAnimatorsOnBase();
        _sync -= dt;
        if (_sync > 0) return;
        _sync = 0.2f;
        WeaponSlotSync.Tick();
    }

    public void OnGameStart() => WeaponSlotSync.Reset();
}
