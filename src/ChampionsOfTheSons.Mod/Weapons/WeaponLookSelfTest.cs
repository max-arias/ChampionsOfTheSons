using ChampionsOfTheSons.Core.Rules;
using HarmonyLib;
using Sons.Weapon;
using TheForest.Utils;
using UnityEngine;

namespace ChampionsOfTheSons.Mod.Weapons;

/// <summary>
/// A look at every weapon type in the hand (COS-196, COS-203): each is drawn, the view is turned level and then down at
/// the hands, and a screenshot is saved (run windowed: <c>-Only WeaponLookFeature</c>). The bow also gets its ammo checks
/// (COS-204): the game sets its ammo once, not every frame, and a shot spends an arrow.
/// </summary>
public sealed class WeaponLookFeature : IModFeature
{
    public void RegisterSelfTests() => WeaponLookSelfTest.Register();
}

/// <summary>Counts the bow's "set ammo" calls (each one shows the game's "Set ammo to ..." notice).</summary>
[HarmonyPatch(typeof(BowWeaponController), nameof(BowWeaponController.SetAmmoItemInstance))]
internal static class BowAmmoCounter
{
    public static int Sets { get; private set; }

    [HarmonyPostfix]
    private static void Count() => Sets++;
}

/// <summary>Counts the bow's ammo type changes (each one shows the game's "Set ammo to ..." notice).</summary>
[HarmonyPatch(typeof(BowWeaponController), nameof(BowWeaponController.CycleAmmoTypeInternal))]
internal static class BowCycleCounter
{
    public static int Cycles { get; private set; }

    [HarmonyPostfix]
    private static void Count() => Cycles++;
}

internal static class WeaponLookSelfTest
{
    private static readonly (string Item, string Type)[] Typed =
    {
        ("item.relic_hammer", "hammer"), ("item.rage", "great_sword"), ("item.axe_of_swiftness", "axe"),
        ("item.rusty_polearm", "polearm"), ("item.dull_longsword", "long_sword"),
        ("item.sacrificial_kris", "dagger"), ("item.nine_iron_of_last_resort", "golf_club"), ("item.revving_fury", "chainsaw"),
        ("item.soulstring", "greatbow"), // last: the bow checks follow
    };

    private static int _sets, _arrows;
    private static int _sawHits;
    private static bool _sawListening;
    private static Sons.Ai.Vail.VailActor? _sawEnemy;
    private static bool _hud;
    private static float _pull;

    public static void Register()
    {
        SelfTest.Add(1f, "look: setup", () =>
        {
            TestKit.Unpause();
            TestKit.FaceOpenGround(new[] { (3f, 0f) });
            SelfTest.Log("look: game items: " + GameItems.Matching("chainsaw", "putter", "knife", "club", "cleaver", "golf", "katana", "axe", "machete", "spear", "bow", "stick", "bone", "torch", "guitar", "shovel", "pickaxe"));
            _hud = Config.ShowHud.Value;
            Config.ShowHud.Value = false; // the action bar covers the hands
        });
        // The game's own bow first, as the reference for how a bow sits in the hands, draws and shoots.
        SelfTest.Add(2f, "look: game bow", () =>
        {
            TestKit.Unwield();
            WeaponSlotSync.Tick();
            int arrow = Admin.AdminState.ArrowItemId();
            if (arrow >= 0 && LocalPlayer.Inventory.AmountOf(arrow, true, false) < 10) LocalPlayer.Inventory.AddItem(arrow, 10, true, false, null);
            Look(Level);
            SelfTest.Check("look: game bow equipped", TestKit.EquipGameItem(GameItems.Find("CraftedBow", 443)));
        });
        SelfTest.Add(5.5f, "look: game bow level", () => { AnimState("game bow idle"); BowState("game bow, idle"); Shot("look-gamebow-level"); Look(Down); });
        SelfTest.Add(7.5f, "look: game bow down", () => { Shot("look-gamebow-down"); Look(Level); TestKit.HoldAttack(0.3f); });
        SelfTest.Add(10f, "look: game bow draw", () => { BowState("game bow, before the draw"); _arrows = Arrows(); TestKit.HoldAttack(3f); });
        SelfTest.Add(11f, "look: game bow drawing", () => BowState("game bow, drawing 1 s"));
        SelfTest.Add(12.5f, "look: game bow drawn", () => { AnimState("game bow drawn"); BowState("game bow, drawing 2.5 s"); Shot("look-gamebow-drawn"); });
        SelfTest.Add(15f, "look: game bow shot", () =>
        {
            BowState("game bow, after the shot");
            SelfTest.Log($"look: game bow: arrows {_arrows} -> {Arrows()}");
            LocalPlayer.Inventory.StashHeldItems(false, true);
        });
        // The game's own axe and machete, as the reference for which way a blade faces in the hand (COS-208, COS-207).
        float g = 16f;
        foreach (var (name, id) in new[] { ("ModernAxe", 356), ("Machete2", 359) })
        {
            SelfTest.Add(g, $"look: game {name}", () => { Look(Level); SelfTest.Check($"look: game {name} equipped", TestKit.EquipGameItem(GameItems.Find(name, id))); });
            SelfTest.Add(g + 3f, $"look: game {name} level", () => { Shot($"look-game-{name}-level"); Look(Down); });
            SelfTest.Add(g + 5f, $"look: game {name} down", () => { Shot($"look-game-{name}-down"); Look(Level); LocalPlayer.Inventory.StashHeldItems(false, true); });
            g += 6f;
        }
        // The game's own chainsaw on an enemy, as the reference for the mod chainsaw's hit check (COS-209).
        SelfTest.Add(g, "look: game chainsaw", () =>
        {
            if (!_sawListening) { _sawListening = true; Bridge.EnemyPatches.LocalWeaponHit += (_, w) => { if (w == WeaponClass.Melee) _sawHits++; }; }
            Look(Level);
            SelfTest.Check("look: game chainsaw equipped", TestKit.EquipGameItem(GameItems.Find("TacticalChainsaw", 394)));
        });
        SelfTest.Add(g + 3.5f, "look: game chainsaw attack", () =>
        {
            _sawHits = 0;
            TestKit.FaceOpenGround(new[] { (1.1f, 0f) });
            _sawEnemy = TestKit.Spawn(Sons.Ai.Vail.VailActorTypeId.Carl, 0.6f);
            var saw = LocalPlayer.Transform.GetComponentInChildren<ChainsawWeaponController>(false);
            SelfTest.Log($"look: game chainsaw: controller {(saw != null)}, power {(saw != null ? saw._remainingPower : -1):0.00}, has power {(saw != null && saw.HasPower())}");
            TestKit.HoldAttack(4f);
        });
        SelfTest.Add(g + 6f, "look: game chainsaw mid", () =>
        {
            var saw = LocalPlayer.Transform.GetComponentInChildren<ChainsawWeaponController>(false);
            SelfTest.Log($"look: game chainsaw mid: attack held {(saw != null && saw._anyAttackHeld)}, hold anim {(saw != null ? saw._chainSawHoldAttackAnimActive : -1)}, rev {(saw != null ? saw._currentRevAmount : -1):0.00}, chain speed {(saw != null ? saw._chainSpeed : -1):0.0}, hits {_sawHits}");
        });
        SelfTest.Add(g + 8.5f, "look: game chainsaw hits", () =>
        {
            SelfTest.Log($"look: game chainsaw: {_sawHits} melee hits, last {Bridge.EnemyPatches.LastHit}");
            if (_sawEnemy != null) TestKit.Kill(_sawEnemy);
            _sawEnemy = null;
            LocalPlayer.Inventory.StashHeldItems(false, true);
        });
        g += 10f;
        float t = g + 1f;
        foreach (var (item, type) in Typed)
        {
            SelfTest.Add(t, $"look: {type}", () => { Look(Level); WeaponsSelfTest.Wield(item, type, Core.Rarity.Legendary); });
            SelfTest.Add(t + 3.5f, $"look: {type} level", () => { WeaponModels.Scan(); AnimState($"{type} held"); Describe(type); Shot($"look-{type}-level"); Look(Down); });
            SelfTest.Add(t + 5.5f, $"look: {type} down", () =>
            {
                Shot($"look-{type}-down");
                Look(Level);
                if (type == "chainsaw")
                {
                    // The chainsaw runs while attack is held: an enemy in front of it should be hit for the mod weapon's damage.
                    _sawHits = 0;
                    // Close: the saw's bar is under half a metre long and it does not swing.
                    TestKit.FaceOpenGround(new[] { (1.1f, 0f) });
                    _sawEnemy = TestKit.Spawn(Sons.Ai.Vail.VailActorTypeId.Carl, 0.6f);
                    TestKit.HoldAttack(4f);
                    return;
                }
                if (type != "greatbow") LocalPlayer.Inventory.StashHeldItems(false, true);
            });
            if (type == "chainsaw")
            {
                SelfTest.Add(t + 8f, "look: chainsaw running", () => { WeaponModels.Scan(); var saw = WeaponModels.Base?.TryCast<ChainsawWeaponController>();
                    SelfTest.Log($"look: mod chainsaw mid: attack held {(saw != null && saw._anyAttackHeld)}, hold anim {(saw != null ? saw._chainSawHoldAttackAnimActive : -1)}, rev {(saw != null ? saw._currentRevAmount : -1):0.00}, chain speed {(saw != null ? saw._chainSpeed : -1):0.0}, hits {_sawHits}");
                    SelfTest.Log("look: chainsaw: " + WeaponModels.SawPower); Shot("look-chainsaw-running"); });
                SelfTest.Add(t + 10.5f, "look: chainsaw hits", () =>
                {
                    SelfTest.Log("look: mod chainsaw (the game's own saw does not rev in a simulated hold either, so no check): " + $"{_sawHits} melee hits, dist {(_sawEnemy != null ? Vector3.Distance(_sawEnemy.transform.position, LocalPlayer.Transform.position) : -1):0.0} m, last {Bridge.EnemyPatches.LastHit}, weapon damage {WeaponModels.ModWeaponDamage:0}; {WeaponModels.SawPower}");
                    if (_sawEnemy != null) TestKit.Kill(_sawEnemy);
                    _sawEnemy = null;
                    LocalPlayer.Inventory.StashHeldItems(false, true);
                });
                t += 5f;
            }
            SelfTest.Add(t + 6.3f, $"look: {type} stashed", () => { if (type != "greatbow") AnimState($"{type} stashed"); });
            t += 7f;
        }
        // The bow is in hand now.
        SelfTest.Add(t, "look: bow ammo", () =>
        {
            int arrow = Admin.AdminState.ArrowItemId();
            var inv = LocalPlayer.Inventory;
            if (arrow >= 0 && inv.AmountOf(arrow, true, false) < 10) inv.AddItem(arrow, 10, true, false, null);
            BowState("with arrows");
        });
        SelfTest.Add(t + 1.5f, "look: bow ammo settles", () =>
        {
            var a = LocalPlayer.Animator;
            var stale = new[] { "axeHeld", "spearHeld", "stickHeld", "smallAxe" }.Where(n => a.GetBool(n)).ToList();
            SelfTest.Check("look: the bow's arms carry no other weapon's pose flags", a.GetBool("bowHeld") && stale.Count == 0, $"bowHeld {a.GetBool("bowHeld")}, also on: {string.Join(", ", stale)}");
            AnimState("mod bow idle"); BowState("settled"); _sets = BowAmmoCounter.Sets + BowCycleCounter.Cycles;
        });
        SelfTest.Add(t + 3.5f, "look: bow ammo quiet", () =>
        {
            int sets = BowAmmoCounter.Sets + BowCycleCounter.Cycles - _sets;
            SelfTest.Check("look: the bow's ammo is not set again and again", sets <= 1, $"{sets} sets in 2 s");
            _arrows = Arrows();
            if (WeaponModels.LocalSkin?.String is { } st) st.MaxPull = 0;
            TestKit.HoldAttack(0.3f);
        });
        SelfTest.Add(t + 6.5f, "look: bow draw", () => { BowState("before the draw"); TestKit.HoldAttack(3f); });
        SelfTest.Add(t + 9f, "look: bow drawn", () =>
        {
            var s = WeaponModels.LocalSkin?.String;
            _pull = s?.Pull ?? 0;
            SelfTest.Log($"look: bow string: charging {s?.Charging}, pulled {_pull:0.00} m (most {s?.MaxPull:0.00} m), hands behind the string: left {s?.LeftBehind:0.00} m, right {s?.RightBehind:0.00} m");
            Shot("look-greatbow-drawn");
        });
        SelfTest.Add(t + 7.5f, "look: bow drawing", () => BowState("drawing 1 s"));
        SelfTest.Add(t + 8.5f, "look: bow drawing 2", () => { AnimState("mod bow drawn"); BowState("drawing 2 s"); });
        SelfTest.Add(t + 10.5f, "look: bow draw again", () => TestKit.HoldAttack(2.5f));
        SelfTest.Add(t + 12.5f, "look: bow drawn again", () =>
        {
            var s = WeaponModels.LocalSkin?.String;
            SelfTest.Log($"look: bow string (second draw): charging {s?.Charging}, pulled {s?.Pull:0.00} m (most {s?.MaxPull:0.00} m), hands behind the string: left {s?.LeftBehind:0.00} m, right {s?.RightBehind:0.00} m");
            BowState("second draw");
            Shot("look-greatbow-drawn-2");
        });
        SelfTest.Add(t + 15f, "look: bow shot", () =>
        {
            BowState("after the shot");
            SelfTest.Check("look: shots spend arrows", Arrows() < _arrows, $"arrows {_arrows} -> {Arrows()}");
            var s = WeaponModels.LocalSkin?.String;
            SelfTest.Check("look: the bow string was drawn back and let go", s != null && s.MaxPull > 0.15f && s.Pull < 0.02f, $"drawn {s?.MaxPull:0.00} m at most, now {s?.Pull:0.00} m");
            Config.ShowHud.Value = _hud;
        });
    }

    /// <summary>Pitch for the look at the hands, in degrees.</summary>
    private const float Down = 65, Level = 0;

    private static void Shot(string name)
    {
        var cam = Camera.main;
        SelfTest.Log($"look: {name}: view pitch {(cam != null ? -Mathf.Asin(cam.transform.forward.y) * Mathf.Rad2Deg : 0):0} deg down");
        var skin = WeaponModels.LocalSkin;
        if (cam != null && skin?.Model != null && skin.Holder != null)
        {
            var hand = skin.Holder.parent != null ? skin.Holder.parent.position : skin.Holder.position;
            var rs = skin.Model.GetComponentsInChildren<Renderer>(false);
            var b = rs.Length > 0 ? rs[0].bounds : new Bounds(skin.Model.transform.position, Vector3.zero);
            foreach (var r in rs) b.Encapsulate(r.bounds);
            SelfTest.Log($"look: {name}: on screen (0..1, z metres ahead): hand {cam.WorldToViewportPoint(hand).ToString("0.00")}, model middle {cam.WorldToViewportPoint(b.center).ToString("0.00")}, "
                + $"model size {b.size.ToString("0.00")}, {rs.Count(r => r.enabled)} of {rs.Length} renderers on, model active {skin.Model.activeInHierarchy}");
        }
        TestKit.Screenshot(name);
    }

    private static int Arrows()
    {
        int arrow = Admin.AdminState.ArrowItemId();
        return arrow >= 0 ? LocalPlayer.Inventory.AmountOf(arrow, true, false) : 0;
    }

    /// <summary>Turns the view up or down (positive: down), at once.</summary>
    private static void Look(float pitch)
    {
        var cam = LocalPlayer.CamRotator;
        if (cam == null) { SelfTest.Log("look: no camera rotator"); return; }
        var was = cam.GetTargetAngles();
        var now = new Vector3(pitch * PitchSign, 0, 0);
        cam.SetTargetAngles(now.x, now.y);
        SelfTest.Log($"look: camera angles {was} -> {now}, range {cam.RotationRange}");
    }

    /// <summary>Which way the camera rotator's x angle turns the view down.</summary>
    private const float PitchSign = 1;

    private static void Describe(string type)
    {
        var skin = WeaponModels.LocalSkin;
        if (skin?.Model == null) { SelfTest.Check($"look: {type} model in hand", false, "no model"); return; }
        var t = skin.Model.transform;
        var holder = skin.Holder;
        SelfTest.Log($"look: {type}: {skin.Source}, glow {skin.Look.Key}, {skin.GlowParts} glow parts");
        if (skin.GameTint != null && skin.Look.Glows)
            SelfTest.Check($"look: {type} shown as the game's weapon still glows", skin.GlowParts > 0, $"{skin.GlowParts} parts, {skin.Look.Key}");
        var w = RealWeapons.ByType(type);
        if (w?.Item != null)
        {
            var vars = new List<string>();
            if (w.Item._equippedAnimVars != null) foreach (var v in w.Item._equippedAnimVars) vars.Add(v.ToString());
            SelfTest.Log($"look: {type}: equipped animator flags [{string.Join(", ", vars)}]");
        }
        var chain = new List<string>();
        for (var p = holder; p != null && chain.Count < 6; p = p.parent) chain.Add($"{p.name}@{holder.InverseTransformPoint(p.position).ToString("0.000")}");
        SelfTest.Log($"look: {type}: model at {t.localPosition.ToString("0.000")} rot {t.localEulerAngles} scale {t.localScale.x:0.000}; holder chain {string.Join(" < ", chain)}");
        var hands = new List<string>();
        foreach (var tr in LocalPlayer.Transform.GetComponentsInChildren<Transform>(false))
            if (tr.name.Contains("hand", StringComparison.OrdinalIgnoreCase) && tr.childCount > 0 && hands.Count < 14)
                hands.Add($"{tr.name}@{holder.InverseTransformPoint(tr.position).ToString("0.000")}");
        SelfTest.Log($"look: {type}: hand transforms in holder space: {string.Join(", ", hands)}");
        foreach (var r in holder.GetComponentsInChildren<Renderer>(true))
        {
            if (r.name.Contains("Chain", StringComparison.Ordinal) && r.name != "TacticalChainsaw") continue;
            if (r == null || r.transform.IsChildOf(t) || r.TryCast<ParticleSystemRenderer>() != null) continue;
            var lb = r.localBounds;
            var at = r.TryCast<SkinnedMeshRenderer>() is { rootBone: not null } smr ? smr.rootBone : r.transform;
            Vector3 c = holder.InverseTransformPoint(at.TransformPoint(lb.center));
            Vector3 lo = holder.InverseTransformPoint(at.TransformPoint(lb.min)), hi = holder.InverseTransformPoint(at.TransformPoint(lb.max));
            SelfTest.Log($"look: {type}: game renderer {r.name} centre {c.ToString("0.000")} corners {lo.ToString("0.000")} .. {hi.ToString("0.000")}");
        }
    }

    /// <summary>Logs the player animator's parameters and layer states, one line, for comparing the game bow with ours.</summary>
    private static void AnimState(string when)
    {
        var a = LocalPlayer.Animator;
        if (a == null) return;
        var parts = new List<string>();
        foreach (var p in a.parameters)
        {
            string v = p.type switch
            {
                AnimatorControllerParameterType.Float => a.GetFloat(p.nameHash).ToString("0.##"),
                AnimatorControllerParameterType.Int => a.GetInteger(p.nameHash).ToString(),
                AnimatorControllerParameterType.Bool => a.GetBool(p.nameHash) ? "T" : "F",
                _ => "trig",
            };
            if (v is "0" or "F" or "trig" || !(p.name.Contains("Held") || p.name.Contains("Axe") || p.name.Contains("Weapon") || p.name.Contains("Bow") || p.name.Contains("held"))) continue;
            parts.Add($"{p.name}={v}");
        }
        var layers = new List<string>();
        for (int i = 0; i < a.layerCount; i++) layers.Add($"{a.GetLayerName(i)}:{a.GetCurrentAnimatorStateInfo(i).fullPathHash}@{a.GetLayerWeight(i):0.#}");
        SelfTest.Log($"look: ANIM ({when}) params {string.Join(" ", parts)}");
        SelfTest.Log($"look: ANIM ({when}) layers {string.Join(" ", layers)}");
    }

    private static string Hand(Transform holder, string name)
    {
        foreach (var t in LocalPlayer.Transform.GetComponentsInChildren<Transform>(false))
            if (t.name == name) return holder.InverseTransformPoint(t.position).ToString("0.000");
        return "none";
    }

    private static void BowState(string when)
    {
        try
        {
            var bow = LocalPlayer.Transform.GetComponentInChildren<BowWeaponController>(false);
            var holder = bow != null ? bow.transform : null;
            if (bow == null) { SelfTest.Log($"look: bow state ({when}): no bow controller on {(holder != null ? holder.name : "no holder")}"); return; }
            var rw = bow._rangedWeapon;
            var ammo = rw != null ? rw.GetAmmo() : null;
            var inst = bow.ItemInstance;
            var cached = bow._cachedArrowItems;
            var ids = new List<int>();
            if (cached != null) foreach (int id in cached) ids.Add(id);
            SelfTest.Log($"look: bow state ({when}): sets {BowAmmoCounter.Sets}, cycles {BowCycleCounter.Cycles}, weapon id on the ranged weapon {(rw != null ? rw._weaponItemId : -1)}, "
                + $"item instance {(inst != null ? inst._itemID.ToString() : "none")}, ammo {(ammo == null ? "none" : $"type {ammo.GetItemType()} left {ammo.GetRemainingAmmo()} missing props {ammo._isMissingAmmoProperties} instance {(ammo.ItemInstance != null)} in inventory {ammo.IsAvailableInInventory()}")}, "
                + $"cached arrows [{string.Join(",", ids)}], can cycle {bow.CanCycleAmmo()}, can reload {bow.CanReload()}, reloading {bow.IsReloading()}, upgrade {bow._currentArrowUpgradeType}, "
                + $"arrows owned {Arrows()}; attack state {bow._attackState}, in attack {bow.IsInAttackState()}, charging {bow._isCharging}, charged weapon {bow._isChargedWeapon}, block firing {bow._blockFiring}, "
                + $"aiming {bow.IsAiming}, must aim {bow._mustAimToFire}, two hands to fire {bow._requiresTwoHandsToFire}, reload queued {bow._reloadQueued}, next fire delay {bow._nextFireDelay:0.00}, idle {bow.IsItemIdle()}, "
                + $"hands in the bow's space: left {Hand(holder!, "LeftHandMiddle1")}, right {Hand(holder!, "RightHandMiddle1")}, "
                + $"left hand {(LocalPlayer.Inventory.LeftHandItem != null ? LocalPlayer.Inventory.LeftHandItem._itemID.ToString() : "empty")}, controllable {SonsSdk.GameState.IsPlayerControllable}, mouse left {UnityEngine.InputSystem.Mouse.current?.leftButton.isPressed}");
        }
        catch (Exception e)
        {
            SelfTest.Log($"look: bow state ({when}) failed: {e.Message}");
        }
    }
}
