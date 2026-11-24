using ChampionsOfTheSons.Core.Rules;
using HarmonyLib;
using Sons.Ai.Vail;
using Sons.Weapon;
using SonsSdk;
using TheForest.Utils;
using UnityEngine;

namespace ChampionsOfTheSons.Mod.Weapons;

/// <summary>Self-tests of the polearm throw (COS-206): the game's own spear first, as the reference, then the polearm.</summary>
public sealed class PolearmThrowFeature : IModFeature
{
    public void RegisterSelfTests() => PolearmThrowSelfTest.Register();
}

/// <summary>The order of the game's throw events (self-test log).</summary>
[HarmonyPatch]
internal static class ThrowEvents
{
    public static readonly List<string> Seen = new();

    private static void Note(string what) => Seen.Add($"{Time.time:0.00} {what}");

    [HarmonyPatch(typeof(SpearWeaponController), nameof(SpearWeaponController.OnItemLeavesHandEvent))]
    [HarmonyPostfix]
    private static void LeavesHand() => Note("spear.OnItemLeavesHandEvent");

    [HarmonyPatch(typeof(RangedWeaponController), nameof(RangedWeaponController.SetItemThrown))]
    [HarmonyPostfix]
    private static void Thrown() => Note("ranged.SetItemThrown");

    [HarmonyPatch(typeof(RangedWeaponController), nameof(RangedWeaponController.OnItemLeavesHandCallback))]
    [HarmonyPostfix]
    private static void LeavesHandCallback() => Note("ranged.OnItemLeavesHandCallback");

    [HarmonyPatch(typeof(RangedWeaponController), nameof(RangedWeaponController.CheckConsumeThrownItem))]
    [HarmonyPostfix]
    private static void Consume() => Note("ranged.CheckConsumeThrownItem");

    [HarmonyPatch(typeof(SpearWeaponController), nameof(SpearWeaponController.BlockStart))]
    [HarmonyPostfix]
    private static void Aim() => Note("spear.BlockStart");

    [HarmonyPatch(typeof(SpearWeaponController), nameof(SpearWeaponController.AttackButtonDown))]
    [HarmonyPostfix]
    private static void Attack() => Note("spear.AttackButtonDown");
}

internal static class PolearmThrowSelfTest
{
    private const int SpearId = 474;
    private static readonly HashSet<IntPtr> Before = new();
    private static readonly List<Transform> Fresh = new();
    private static PlayerCharacter Pc => PlayerState.Character;
    private static int _spears, _key, _rangedHits;
    private static bool _listening, _flying;
    private static double _expected;
    private static Core.ItemInstance? _thrown;
    private static Vector3 _dropAt;
    private static Vector3? _view, _start;
    private static Sons.Ai.Vail.VailActor? _enemy;

    public static void Register()
    {
        SelfTest.Add(1f, "throw: game spear", () =>
        {
            TestKit.Unpause();
            _start = LocalPlayer.Transform.position;
            TestKit.FaceOpenGround(new[] { (8f, 0f) });
            PlayerState.Character.Inventory.Remove(WeaponTypes.WeaponSlot);
            WeaponSlotSync.Tick();
            var inv = LocalPlayer.Inventory;
            if (inv.AmountOf(SpearId, true, false) < 2) inv.AddItem(SpearId, 2, true, false, null);
            SelfTest.Check("throw: game spear equipped", TestKit.EquipGameItem(SpearId));
        });
        SelfTest.Add(5f, "throw: aim", () =>
        {
            LookLevel();
            Snapshot();
            ThrowEvents.Seen.Clear();
            Spear("game spear, before aim");
            TestKit.HoldAim(3f);
        });
        SelfTest.Add(6.5f, "throw: attack", () => { Spear("game spear, aiming"); TestKit.TapAttack(); });
        for (int i = 0; i < 8; i++)
        {
            int n = i;
            SelfTest.Add(7f + i * 0.5f, $"throw: flight {i}", () => Flight($"game spear +{0.5f + n * 0.5f:0.0}s"));
        }
        SelfTest.Add(12f, "throw: landed", () =>
        {
            Spear("game spear, after");
            SelfTest.Log("throw: events " + string.Join(" | ", ThrowEvents.Seen));
            SelfTest.Log($"throw: game spears owned {LocalPlayer.Inventory.AmountOf(SpearId, true, false)}");
            Flight("game spear landed", full: true);
        });

        // ---- the polearm: thrown into open ground, it leaves the slot, lands as a stuck drop and is fetched by walking onto it.
        SelfTest.Add(13f, "throw: polearm", () =>
        {
            LocalPlayer.Inventory.StashHeldItems(false, true);
            TestKit.FaceOpenGround(new[] { (8f, 0f) });
            WeaponsSelfTest.Wield("item.rusty_polearm", "polearm");
            _spears = LocalPlayer.Inventory.AmountOf(SpearId, true, false);
            _thrown = Pc.Inventory.Get(WeaponTypes.WeaponSlot);
            _key = Pc.WeaponBar.KeyOf(WeaponTypes.WeaponSlot);
            if (!_listening)
            {
                _listening = true;
                Bridge.EnemyPatches.LocalWeaponHit += (_, weapon) => { if (weapon == WeaponClass.Ranged) _rangedHits++; };
            }
        });
        SelfTest.Add(17f, "throw: polearm aim", () => { LookLevel(); Snapshot(); Spear("polearm, before aim"); TestKit.HoldAim(3f); });
        SelfTest.Add(18.5f, "throw: polearm attack", () => { Spear("polearm, aiming"); TestKit.TapAttack(); });
        SelfTest.Add(20f, "throw: polearm left the hand", () =>
        {
            int own = LocalPlayer.Inventory.AmountOf(RealWeapons.ByType("polearm")!.Id, true, false);
            SelfTest.Check("throw: the thrown polearm left the weapon slot", PolearmThrow.Out != null && Pc.Inventory.Get(WeaponTypes.WeaponSlot) == null && own == 0,
                $"{PolearmThrow.Last}; slot {(Pc.Inventory.Get(WeaponTypes.WeaponSlot)?.BaseId ?? "empty")}, game item owned {own}");
            Flight("polearm +1.5s");
        });
        SelfTest.Add(24f, "throw: polearm landed", () =>
        {
            WeaponSlotSync.Tick();
            var drop = Loot.LootFeature.Ground.Get(PolearmThrow.DropId);
            int own = LocalPlayer.Inventory.AmountOf(RealWeapons.ByType("polearm")!.Id, true, false);
            SelfTest.Check("throw: it lies in the world as a mod drop of the same item", PolearmThrow.Landed && drop != null && ReferenceEquals(drop.Item, _thrown), PolearmThrow.Last);
            SelfTest.Check("throw: stuck where it landed", drop != null && Loot.LootVisuals.Stuck.Contains(drop.Id), PolearmThrow.Last);
            SelfTest.Check("throw: no game spear gained, no polearm handed back", LocalPlayer.Inventory.AmountOf(SpearId, true, false) == _spears && own == 0,
                $"game spears {_spears} -> {LocalPlayer.Inventory.AmountOf(SpearId, true, false)}, polearm items {own}");
            SelfTest.Check("throw: a thrown weapon does not expire", drop != null && drop.Remaining > 86400, $"{drop?.Remaining:0} s left");
            var save = PlayerState.ToSave();
            SelfTest.Check("throw: a save made now still owns it", save.Slots.TryGetValue(WeaponTypes.WeaponSlot, out var saved) && saved.BaseId == _thrown?.BaseId, string.Join(",", save.Slots.Keys.Where(k => k.StartsWith("equip."))));
            Flight("polearm landed");
            if (drop != null)
            {
                _dropAt = new Vector3(drop.X, drop.Y, drop.Z);
                SelfTest.Log($"throw: polearm lies {Vector3.Distance(_dropAt, LocalPlayer.Transform.position):0.0} m away");
                TestKit.Screenshot("throw-polearm-stuck");
                Spells.SpellCaster.TeleportPlayer(_dropAt + new Vector3(8, 0.3f, 0)); // away first: a thrown drop is not grabbed at once
            }
        });
        SelfTest.Add(25.5f, "throw: walk onto it", () => Spells.SpellCaster.TeleportPlayer(_dropAt + Vector3.up * 0.3f));
        SelfTest.Add(27f, "throw: polearm fetched", () =>
        {
            WeaponSlotSync.Tick();
            SelfTest.Check("throw: walking onto it puts it back in the weapon slot on its key",
                ReferenceEquals(Pc.Inventory.Get(WeaponTypes.WeaponSlot), _thrown) && Pc.WeaponBar.KeyOf(WeaponTypes.WeaponSlot) == _key && PolearmThrow.Out == null,
                $"{PolearmThrow.Last}; slot {(Pc.Inventory.Get(WeaponTypes.WeaponSlot)?.BaseId ?? "empty")}, key {Pc.WeaponBar.KeyOf(WeaponTypes.WeaponSlot)} (was {_key})");
        });

        // ---- thrown at an enemy: the hit is the weapon's damage times the throw factor.
        SelfTest.Add(28f, "throw: at an enemy", () =>
        {
            TestKit.FaceOpenGround(new[] { (5f, 0f) });
            _enemy = TestKit.Spawn(Sons.Ai.Vail.VailActorTypeId.Carl, 4f);
            WeaponSlotSync.Draw();
            _rangedHits = 0;
            _flying = false;
        });
        SelfTest.Add(31.5f, "throw: aim at the enemy", () => { LookLevel(); TestKit.HoldAim(3f); });
        SelfTest.Add(33f, "throw: attack the enemy", TestKit.TapAttack);
        // The throw itself may miss in a test; a projectile hit while it flies is the rule checked: weapon damage x throw factor.
        for (int i = 0; i < 10; i++)
            SelfTest.Add(33.3f + i * 0.3f, $"throw: projectile hit while it flies {i}", () =>
            {
                if (_flying || !PolearmThrow.Flying || _enemy == null) return;
                _flying = true;
                _expected = PolearmThrow.ThrownHit?.Damage ?? 0;
                _rangedHits = 0;
                TestKit.Hit(_enemy, 10, ImpactType.Projectile);
            });
        SelfTest.Add(36.5f, "throw: enemy hit", () =>
        {
            var type = ModContent.Db.WeaponTypes["polearm"];
            double want = _thrown != null ? WeaponTypes.WeaponDamage(type, _thrown.Level, _thrown.Rarity) * type.ThrowDamage : 0;
            SelfTest.Check("throw: a hit of the flying polearm is a ranged hit for the weapon's damage x the throw factor",
                _flying && _rangedHits >= 1 && Math.Abs(_expected - want) < 0.01 && want > 0,
                $"flying {_flying}, {_rangedHits} ranged hits, thrown damage {_expected:0.0}, want {want:0.0}; last hit {Bridge.EnemyPatches.LastHit}; {PolearmThrow.Last}");
            PolearmThrow.ReturnToBag("the self-test ends");
            bool back = Pc.Inventory.Slots.Values.Any(i => ReferenceEquals(i, _thrown));
            SelfTest.Check("throw: recalled, it is in the inventory again", back && PolearmThrow.Out == null, PolearmThrow.Last);
            if (_enemy != null) TestKit.Kill(_enemy);
            _enemy = null;
            // The view as it was: later tests in the run swing at enemies at their feet.
            if (_view is { } v && LocalPlayer.CamRotator != null) LocalPlayer.CamRotator.SetTargetAngles(v.x, v.y);
            _view = null;
            // And the player where the test began: the landing spot can be a slope where later melee tests miss.
            if (_start is { } home) Spells.SpellCaster.TeleportPlayer(home);
            _start = null;
        });
    }

    /// <summary>Turns the view level, so a throw flies instead of going into the ground at the feet.</summary>
    private static void LookLevel()
    {
        var cam = LocalPlayer.CamRotator;
        if (cam == null) return;
        _view ??= cam.GetTargetAngles();
        cam.SetTargetAngles(0, 0);
    }

    private static void Snapshot()
    {
        Before.Clear();
        Fresh.Clear();
        foreach (var t in UnityEngine.Object.FindObjectsOfType<Rigidbody>()) Before.Add(t.transform.Pointer);
    }

    /// <summary>Logs the held spear's own account of itself.</summary>
    private static void Spear(string when)
    {
        var spear = LocalPlayer.Transform.GetComponentInChildren<SpearWeaponController>(false);
        if (spear == null) { SelfTest.Log($"throw: ({when}) no spear controller in hand; right hand {LocalPlayer.Inventory.RightHandItem?._itemID}"); return; }
        var rw = spear.GetRangedWeapon();
        var last = rw != null ? rw._lastFiredProjectile : null;
        SelfTest.Log($"throw: ({when}) throwing {spear._isThrowing}, mid throw {spear._inMidThrow}, thrown {spear._itemWasThrown}, holding block {spear._isHoldingBlocking}, idle {spear.IsItemIdle()}, "
            + $"ranged weapon {(rw != null ? rw.name + " id " + rw._weaponItemId : "none")}, last projectile {(last != null ? last.name : "none")}, "
            + $"projectile transform {(rw != null && rw._projectileTransform != null ? rw._projectileTransform.name : "none")}, visual {(rw != null && rw._projectileVisualTransform != null ? rw._projectileVisualTransform.name : "none")}");
    }

    /// <summary>Logs the rigid bodies that appeared since the aim began: what flies, and what it turns into.</summary>
    private static void Flight(string when, bool full = false)
    {
        foreach (var rb in UnityEngine.Object.FindObjectsOfType<Rigidbody>())
        {
            var t = rb.transform;
            if (Before.Contains(t.Pointer) || t.IsChildOf(LocalPlayer.Transform)) continue;
            if (!Fresh.Any(f => f != null && f.Pointer == t.Pointer)) Fresh.Add(t);
        }
        foreach (var t in Fresh)
        {
            if (t == null) { SelfTest.Log($"throw: ({when}) a tracked object is gone"); continue; }
            var rb = t.GetComponent<Rigidbody>();
            float dist = Vector3.Distance(t.position, LocalPlayer.Transform.position);
            string line = $"throw: ({when}) {t.name} active {t.gameObject.activeInHierarchy} at {dist:0.0} m, speed {(rb != null ? rb.velocity.magnitude : 0):0.0}, kinematic {(rb != null && rb.isKinematic)}, parent {(t.parent != null ? t.parent.name : "none")}";
            if (full)
            {
                var comps = new List<string>();
                foreach (var c in t.GetComponentsInChildren<Component>(true))
                    if (c != null) comps.Add($"{c.gameObject.name}:{c.GetIl2CppType().Name}");
                line += "; parts " + string.Join(", ", comps.Distinct().Take(60));
            }
            SelfTest.Log(line);
        }
        if (Fresh.Count == 0) SelfTest.Log($"throw: ({when}) no new rigid body");
    }
}
