using ChampionsOfTheSons.Core.Rules;
using ChampionsOfTheSons.Mod.Bridge;
using Sons.Weapon;
using TheForest.Items.Inventory;
using TheForest.Utils;
using UnityEngine;

namespace ChampionsOfTheSons.Mod.Weapons;

/// <summary>
/// COS-185 spike checks: the long sword as a registered game item. Own it with no vanilla weapon, hold it, swing it at an
/// enemy, put it on a hotkey, see whether it ends up in the game's save data, then remove it.
/// </summary>
internal static class RealWeaponsSelfTest
{
    private const int Id = RealWeapons.LongSwordId;
    private static int _hits, _swings;
    private static bool _listening;

    public static void Register()
    {
        SelfTest.Add(1f, "real weapons: registered", () =>
        {
            TestKit.Unpause();
            foreach (var n in RealWeapons.Notes) SelfTest.Log("real weapons: " + n);
            SelfTest.Check("real weapons: item registered", RealWeapons.Item != null);
            SelfTest.Check("real weapons: registered before the world loaded", RealWeapons.RegisteredBeforeWorld);
            SelfTest.Check("real weapons: held template built", RealWeapons.HeldTemplate != null);
        });

        SelfTest.Add(2f, "real weapons: own it, no vanilla weapons", () =>
        {
            var inv = LocalPlayer.Inventory;
            PlayerState.Character.Inventory.Remove(WeaponTypes.WeaponSlot); // no overlay type in play
            foreach (int vanilla in new[] { 359, 367, 356, 379, 431, 477, 380, 525, 474, 443, 360 })
            {
                int n = inv.AmountOf(vanilla, true, false);
                if (n > 0) inv.RemoveItem(vanilla, n, false, false, true, null, true);
            }
            bool added = inv.AddItem(Id, 1, true, false, null);
            SelfTest.Check("real weapons: game inventory takes it", added && inv.AmountOf(Id, true, false) == 1,
                $"added {added}, amount {inv.AmountOf(Id, true, false)}, max {inv.GetMaxAmountOf(Id)}");
            TestKit.FreeHands();
            bool equipped = inv.TryEquip(Id, false, true, false);
            SelfTest.Check("real weapons: equips", equipped, equipped ? "" : WeaponsSelfTest.GameInventoryDiag(inv, Id));
        });

        SelfTest.Add(5f, "real weapons: in hand", () =>
        {
            var held = HeldWeapons.Local();
            SelfTest.Check("real weapons: held in hand", held?.ItemId == Id, held == null ? "nothing held" : $"holding {held.Value.ItemId}");
            if (held is not { } h || h.ItemId != Id) return;
            var ctl = h.Holder.GetComponent<MeleeWeaponController>();
            var data = ctl?._meleeWeaponData;
            SelfTest.Check("real weapons: melee controller", ctl != null);
            SelfTest.Check("real weapons: long sword damage on its own data", data != null && Mathf.Approximately(data.Damage, 20f),
                (data == null ? "no data" : $"dmg {data.Damage} swing {data.SwingSpeed:0.00} stamina {data.StaminaCost:0.00}") + $"; spawned with {RealWeapons.DataSource}");
            var renderers = h.Holder.GetComponentsInChildren<Renderer>(true).Where(r => r.TryCast<ParticleSystemRenderer>() == null).ToList();
            var ours = renderers.Where(r => RealWeapons.IsOurs(r.transform, h.Holder)).ToList();
            var game = renderers.Except(ours).ToList();
            SelfTest.Check("real weapons: our model shown, game mesh off", ours.Any(r => r.enabled) && game.All(r => !r.enabled),
                $"ours {ours.Count(r => r.enabled)}/{ours.Count} on, game {game.Count(r => r.enabled)}/{game.Count} on, late meshes switched off {RealWeapons.LateMeshes}");
            SelfTest.Check("real weapons: overlay not applied on top", WeaponModels.LocalSkin == null);
            SelfTest.Log("real weapons: held tree\n" + WeaponsSelfTest.Tree(h.Holder, 0));
            TestKit.Screenshot("real-weapons-held");
        });

        // The game swallows the first attacks of a session (WeaponsSelfTest): warm up before the counted swings.
        for (int i = 0; i < 3; i++) SelfTest.Add(6f + i * 1.2f, $"real weapons: warm-up swing {i + 1}", TestKit.TapAttack);
        SelfTest.Add(10f, "real weapons: enemy", () =>
        {
            if (!_listening)
            {
                _listening = true;
                EnemyPatches.LocalWeaponHit += (_, weapon) => { if (weapon == WeaponClass.Melee) _hits++; };
            }
            _hits = 0;
            _swings = RealWeapons.Swings;
            SelfTest.Log($"real weapons: warm-up swings {RealWeapons.Swings}");
            TestKit.FaceOpenGround(new[] { (1.1f, 0f) });
            _enemy = TestKit.Spawn(Sons.Ai.Vail.VailActorTypeId.Carl, 1.1f);
            SelfTest.Check("real weapons: enemy spawned", _enemy != null);
        });
        for (int i = 0; i < 8; i++) SelfTest.Add(12f + i * 1.1f, $"real weapons: swing {i + 1}", TestKit.TapAttack);
        SelfTest.Add(12.4f, "real weapons: swing screenshot", () => TestKit.Screenshot("real-weapons-swing"));
        SelfTest.Add(12.3f, "real weapons: attack state", () => SelfTest.Log("real weapons: after a tap: " + AttackState()));
        SelfTest.Add(21.5f, "real weapons: hits landed", () =>
        {
            SelfTest.Log("real weapons: " + AttackState());
            SelfTest.Check("real weapons: swing events", RealWeapons.Swings > _swings, $"{RealWeapons.Swings - _swings} swings, all melee swings {RealWeapons.AllSwings}, animator moved to the base id {RealWeapons.AnimatorFixes}x");
            SelfTest.Check("real weapons: melee hits land", _hits > 0, $"{_hits} hits, enemy at {EnemyDistance()}");
            if (_enemy != null) TestKit.Kill(_enemy);
        });

        SelfTest.Add(22f, "real weapons: hotkey", () =>
        {
            LocalPlayer.Inventory.StashHeldItems(false, true);
            HotkeyBridge.SetForTest(5, Id);
            var slot = HotkeyBridge.At(5);
            SelfTest.Log($"real weapons: hotkey 6 holds {slot.ItemId} x{slot.Amount}, icon {(HotkeyBridge.Icon(Id) != null ? HotkeyBridge.Icon(Id)!.name : "none")}, name '{HotkeyBridge.Name(Id)}'");
        });
        SelfTest.Add(25f, "real weapons: hotkey press", () =>
        {
            var held = HeldWeapons.Local();
            SelfTest.Log($"real weapons: before the hotkey, holding {held?.ItemId.ToString() ?? "nothing"}");
            HotkeyBridge.Trigger(5);
        });
        SelfTest.Add(28f, "real weapons: hotkey equips", () =>
        {
            var held = HeldWeapons.Local();
            SelfTest.Check("real weapons: hotkey 6 draws it", held?.ItemId == Id, held == null ? "nothing held" : $"holding {held.Value.ItemId}");
            SelfTest.Log("real weapons: " + HotkeyBridge.Describe());
        });

        SelfTest.Add(29f, "real weapons: save data", () =>
        {
            var inv = LocalPlayer.Inventory;
            try
            {
                var data = inv.Sons_Save_ISaveGameSerializer_TheForest_Items_Inventory_PlayerInventory_PlayerInventorySaveData__OnSerialize();
                string json = data == null ? "" : Newtonsoft.Json.JsonConvert.SerializeObject(data);
                int at = json.IndexOf(Id.ToString(), StringComparison.Ordinal);
                SelfTest.Log($"real weapons: inventory save data mentions {Id}: {at >= 0}{(at >= 0 ? " ..." + json.Substring(Math.Max(0, at - 120), Math.Min(240, json.Length - Math.Max(0, at - 120))) + "..." : "")}");
                bool removed = data != null && data.RemoveItem(Id);
                string after = data == null ? "" : Newtonsoft.Json.JsonConvert.SerializeObject(data);
                SelfTest.Log($"real weapons: PlayerInventorySaveData.RemoveItem -> {removed}, still mentioned after: {after.Contains(Id.ToString(), StringComparison.Ordinal)}");
            }
            catch (Exception e)
            {
                SelfTest.Log("real weapons: save data check failed: " + e.Message);
            }
            HotkeyBridge.SetForTest(5, 0);
        });

        SelfTest.Add(30f, "real weapons: remove", () =>
        {
            var inv = LocalPlayer.Inventory;
            bool removed = inv.RemoveItem(Id, 1, false, false, true, null, true);
            SelfTest.Check("real weapons: removed from the game inventory", removed && inv.AmountOf(Id, true, false) == 0, $"removed {removed}, amount {inv.AmountOf(Id, true, false)}");
        });
        SelfTest.Add(32f, "real weapons: hand empty", () =>
        {
            var held = HeldWeapons.Local();
            SelfTest.Check("real weapons: no longer in hand", held?.ItemId != Id, held == null ? "" : $"holding {held.Value.ItemId}");
        });

        // Control: the same warm-up, enemy and taps with the game's own machete.
        SelfTest.Add(33f, "real weapons: control machete", () =>
            SelfTest.Log($"real weapons: control machete equipped {TestKit.EquipGameItem(RealWeapons.MacheteId)}"));
        for (int i = 0; i < 3; i++) SelfTest.Add(36f + i * 1.2f, $"real weapons: control warm-up {i + 1}", TestKit.TapAttack);
        SelfTest.Add(40f, "real weapons: control enemy", () =>
        {
            _hits = 0;
            _swings = RealWeapons.AllSwings;
            TestKit.FaceOpenGround(new[] { (1.1f, 0f) });
            _enemy = TestKit.Spawn(Sons.Ai.Vail.VailActorTypeId.Carl, 1.1f);
        });
        for (int i = 0; i < 8; i++) SelfTest.Add(42f + i * 1.1f, $"real weapons: control swing {i + 1}", TestKit.TapAttack);
        SelfTest.Add(51.5f, "real weapons: control hits", () =>
            SelfTest.Log($"real weapons: control machete: {RealWeapons.AllSwings - _swings} swings, {_hits} hits, enemy at {EnemyDistance()}"));
    }

    private static Sons.Ai.Vail.VailActor? _enemy;

    private static string EnemyDistance() =>
        _enemy == null || LocalPlayer.Transform == null ? "-" : $"{Vector3.Distance(_enemy.transform.position, LocalPlayer.Transform.position):0.0} m";

    /// <summary>The player animator's non-default int and bool parameters, and the current state of each layer.</summary>
    private static string AnimatorState()
    {
        var anim = LocalPlayer.Animator;
        if (anim == null) return "none";
        var parts = new List<string>();
        foreach (var p in anim.parameters)
        {
            if (p.type == AnimatorControllerParameterType.Int)
            {
                int v = anim.GetInteger(p.nameHash);
                if (v != 0) parts.Add($"{p.name}={v}");
            }
            else if (p.type == AnimatorControllerParameterType.Bool && anim.GetBool(p.nameHash)) parts.Add(p.name);
            else if (p.type == AnimatorControllerParameterType.Float)
            {
                float f = anim.GetFloat(p.nameHash);
                if (Mathf.Abs(f) > 1e-3f && p.name.Contains("Item", StringComparison.OrdinalIgnoreCase) || p.name.Contains("Weapon", StringComparison.OrdinalIgnoreCase)) parts.Add($"{p.name}={f:0.##}");
            }
        }
        return string.Join(" ", parts);
    }

    private static string AttackState()
    {
        var ctl = HeldWeapons.LocalMelee();
        if (ctl == null) return "no held melee controller";
        try
        {
            var machete = ctl.TryCast<MacheteWeaponController>();
            return $"held {HeldWeapons.Local()?.ItemId}, time {Time.time:0.00}, last attack {ctl._lastAttackTime:0.00} (frame {ctl._lastAttackFrame}, now {Time.frameCount}), "
                   + $"can attack {ctl.CanAttackOrBlock()}, item idle {machete?.IsItemIdle().ToString() ?? "-"}, enabled {ctl.enabled}/{ctl.gameObject.activeInHierarchy}; animator: {AnimatorState()}";
        }
        catch (Exception e)
        {
            return "attack state failed: " + e.Message;
        }
    }
}
