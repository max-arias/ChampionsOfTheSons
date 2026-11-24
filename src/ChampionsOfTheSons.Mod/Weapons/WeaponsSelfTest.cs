using System.Text;
using ChampionsOfTheSons.Core;
using ChampionsOfTheSons.Core.Content;
using ChampionsOfTheSons.Core.Rules;
using ChampionsOfTheSons.Mod.Audio;
using ChampionsOfTheSons.Mod.Bridge;
using Sons.Items.Core;
using Sons.Weapon;
using TheForest.Utils;
using UnityEngine;

namespace ChampionsOfTheSons.Mod.Weapons;

/// <summary>
/// COS-93, COS-186 and COS-192 checks: the weapon slot gives each type's real game item (owned once, on a weapon bar key, with its
/// own icon) without drawing it; the weapon bar key draws, holsters and swaps weapons; the type's rules hold (swing, damage,
/// trees, throw, arrow drop, draw), a real swing plays the sound and trail and hits an enemy for the weapon's own damage
/// (COS-191), a unique bow's arrows are free (COS-189), the game's inventory save never holds it, and a drop doesn't lose it.
/// </summary>
internal static class WeaponsSelfTest
{
    private static PlayerCharacter Pc => PlayerState.Character;

    private static int _spearsBefore, _swingsBefore, _hits;
    private static bool _listening;
    private static Sons.Ai.Vail.VailActor? _enemy;

    /// <summary>Game weapons the test save might own: none may be in play, the slot alone gives the weapon (COS-186).</summary>
    private static readonly int[] VanillaWeapons = { 359, 367, 356, 379, 431, 477, 380, 525, 449, 474, 443, 360 };

    /// <summary>Registers the steps; returns the time after the last one, so later tests in the phase start after it.</summary>
    public static float Register()
    {
        SelfTest.Add(1f, "weapons: real items registered", () =>
        {
            TestKit.Unpause();
            foreach (var n in RealWeapons.Notes) SelfTest.Log("weapons: " + n);
            var missing = RealWeapons.All.Where(w => w.Item == null || w.BaseId <= 0).Select(w => w.TypeId).ToList();
            SelfTest.Check("weapons: one game item per type", RealWeapons.Registered && missing.Count == 0, "missing " + string.Join(", ", missing));
            SelfTest.Check("weapons: registered before the world loaded", RealWeapons.RegisteredBeforeWorld);
            SelfTest.Log("weapons: bases " + string.Join(", ", RealWeapons.All.Select(w => $"{w.TypeId}={w.BaseId}")));
            if (!_listening)
            {
                _listening = true;
                EnemyPatches.LocalWeaponHit += (_, weapon) => { if (weapon != WeaponClass.Ranged) _hits++; };
            }
            var inv = LocalPlayer.Inventory;
            Pc.Inventory.Remove(WeaponTypes.WeaponSlot);
            foreach (int id in VanillaWeapons)
            {
                int n = inv.AmountOf(id, true, false);
                if (n > 0) inv.RemoveItem(id, n, false, false, true, null, true);
            }
            TestKit.FreeHands();
            inv.StashHeldItems(false, true);
            foreach (int k in Pc.WeaponBar.Bindings.Keys.ToList()) Pc.WeaponBar.Unbind(k);
        });

        // COS-192: equipping puts the weapon on a weapon bar key but not in the hand.
        SelfTest.Add(1.5f, "weapons: equipping does not draw", () =>
        {
            EquipItem("item.dull_longsword", "long_sword");
            TestKit.FreeHands();
            WeaponSlotSync.Tick();
            var w = RealWeapons.ByType("long_sword")!;
            int key = Pc.WeaponBar.KeyOf(WeaponTypes.WeaponSlot);
            SelfTest.Check("weapons: equipped weapon owned but not drawn", LocalPlayer.Inventory.AmountOf(w.Id, true, false) == 1 && !WeaponBarKeys.WeaponInHand(),
                $"owned {LocalPlayer.Inventory.AmountOf(w.Id, true, false)}, in hand {WeaponBarKeys.WeaponInHand()}");
            SelfTest.Check("weapons: equipped weapon gets a weapon bar key", key >= 0 && ActionBar.ActionBarConfig.WeaponHotkeys.Contains(key) && HotkeyBridge.ItemIdAt(key) == w.Id,
                $"key {key}; {HotkeyBridge.Describe()}");
            SelfTest.Check("weapons: the weapon bar keys are held back from the game", WeaponBarKeys.HeldBackKeys().Contains(HotkeyBridge.KeyLabel(Math.Max(0, key))),
                string.Join(",", WeaponBarKeys.HeldBackKeys()));
        });

        // Weapon in the slot drawn by its key: owned once, in hand, on a weapon key of the action bar with its own icon.
        SelfTest.Add(2f, "weapons: slot gives the long sword", () => Wield("item.dull_longsword", "long_sword"));
        SelfTest.Add(5f, "weapons: long sword in hand", () =>
        {
            CheckOwned("long_sword");
            var w = RealWeapons.ByType("long_sword")!;
            int key = WeaponSlotSync.HotkeyIndexOf(w.Id);
            var cell = key >= 0 ? HotkeyBridge.At(key) : default;
            SelfTest.Check("weapons: on an action bar weapon key", key >= 0 && ActionBar.ActionBarConfig.WeaponHotkeys.Contains(key), $"key index {key}; {HotkeyBridge.Describe()}");
            SelfTest.Check("weapons: action bar cell lit while in hand", cell.Held && cell.Amount == 1, $"held {cell.Held}, amount {cell.Amount}");
            var icon = HotkeyBridge.Icon(w.Id);
            var own = Icons.IconCache.GetTexture("item.dull_longsword");
            SelfTest.Check("weapons: the item's own icon", icon != null && own != null && icon.Pointer == own.Pointer, icon == null ? "no icon" : icon.name);
            TestKit.Screenshot("weapons-slot-long-sword");
        });

        // The game swallows the first attacks of a session: a warm-up round, not checked.
        SelfTest.Add(5.5f, "weapons: warm-up swing", Attack);
        SelfTest.Add(6.7f, "weapons: warm-up swing 2", Attack);
        SelfTest.Add(7.9f, "weapons: warm-up swing 3", Attack);

        float t = 9.5f;
        // Every type through the slot: the swap replaces the item, the type's rules hold, and a hit lands on an enemy.
        foreach (var (itemId, typeId) in Typed)
        {
            SelfTest.Add(t, $"weapons: swap to {typeId}", () => Wield(itemId, typeId));
            SelfTest.Add(t + 3f, $"weapons: {typeId} on", () =>
            {
                WeaponModels.Scan();
                CheckOwned(typeId);
                if (CheckActive(typeId) is not { } type) return;
                if (type.Base == WeaponBase.Bow) { CheckBowRules(type); CheckFreeArrows(); return; }
                CheckMeleeRules(type);
                _hits = 0;
                TestKit.FaceOpenGround(new[] { (1.1f, 0f) });
                _enemy = TestKit.Spawn(Sons.Ai.Vail.VailActorTypeId.Carl, 1.1f);
            });
            if (typeId == "greatbow")
            {
                SelfTest.Add(t + 3.5f, "weapons: greatbow screenshot", () => TestKit.Screenshot("weapons-greatbow"));
                t += 4.5f;
                continue;
            }
            SelfTest.Add(t + 4f, $"weapons: {typeId} swing", Attack);
            SelfTest.Add(t + 4.25f, $"weapons: {typeId} screenshot", () => TestKit.Screenshot("weapons-" + typeId));
            // COS-120: under load the game swallows early presses; keep pressing (only while no swing has registered).
            // The spear's draw takes longest: up to six retries, each only while no swing has registered yet.
            for (int r = 0; r < SwingRetries; r++)
                SelfTest.Add(t + 5.1f + r * 0.8f, $"weapons: {typeId} swing retry {r + 1}", RetryAttack);
            float after = t + 5.1f + SwingRetries * 0.8f;
            SelfTest.Add(after, $"weapons: {typeId} extra swing", TestKit.TapAttack);
            SelfTest.Add(after + 1.1f, $"weapons: {typeId} extra swing 2", TestKit.TapAttack);
            SelfTest.Add(after + 2.2f, $"weapons: {typeId} swing fx and hits", () =>
            {
                CheckSwingFx(typeId);
                SelfTest.Check($"weapons: {typeId} hits land", _hits > 0, $"{_hits} hits, animator moved to the base id {RealWeapons.AnimatorFixes}x");
                SelfTest.Log($"weapons: {typeId} last hit {EnemyPatches.LastHit}, weapon damage {WeaponModels.ModWeaponDamage:0}");
                if (_enemy != null) TestKit.Kill(_enemy);
                _enemy = null;
            });
            t = after + 3f;
        }

        // The polearm can't be thrown: aim, then attack, and the spear must still be in hand.
        SelfTest.Add(t, "weapons: polearm again", () => Wield("item.rusty_polearm", "polearm"));
        SelfTest.Add(t + 3f, "weapons: polearm try throw", () =>
        {
            WeaponModels.Scan();
            var spear = WeaponModels.Base?.TryCast<SpearWeaponController>();
            SelfTest.Check("weapons: polearm not throwable", spear != null && WeaponPatches.NoThrow(spear), spear == null ? "no spear controller" : "");
            if (spear == null) return;
            _spearsBefore = LocalPlayer.Inventory.AmountOf(RealWeapons.ByType("polearm")!.Id, true, false);
            spear.BlockStart();
            spear.UpdateBlock();
            spear.AttackButtonDown();
        });
        SelfTest.Add(t + 5.5f, "weapons: polearm still in hand", () =>
        {
            var held = HeldWeapons.Local();
            int count = LocalPlayer.Inventory.AmountOf(RealWeapons.ByType("polearm")!.Id, true, false);
            SelfTest.Check("weapons: polearm kept after aim + attack", held?.Kind == HeldKind.Spear && count == _spearsBefore,
                $"held {held?.Kind}, polearms {_spearsBefore} -> {count}");
        });
        t += 6.5f;

        // COS-192: a bag weapon on a key swaps into the slot and is drawn by the key; the key again holsters it.
        SelfTest.Add(t, "weapons: bind a bag hammer to a weapon key", () =>
        {
            var hammer = new ItemInstance { BaseId = "item.relic_hammer", Level = 10, Rarity = Rarity.Rare, Stats = Array.Empty<RolledStat>() };
            Pc.Inventory.AddToBag(hammer);
            var bag = Pc.Inventory.Slots.First(kv => kv.Key.StartsWith("bag.", StringComparison.Ordinal) && kv.Value == hammer).Key;
            _barKey = ActionBar.ActionBarConfig.WeaponHotkeys.First(k => Pc.WeaponBar.SlotOf(k) == null);
            SelfTest.Check("weapons: bag hammer bound", Pc.WeaponBar.Bind(_barKey, bag, Pc.Inventory, ModContent.Db), $"key {_barKey}");
            SelfTest.Check("weapons: key press swaps it in", WeaponBarKeys.Press(_barKey), WeaponBarKeys.LastAction);
        });
        SelfTest.Add(t + 3f, "weapons: the key drew the hammer", () =>
        {
            var held = HeldWeapons.Local();
            SelfTest.Check("weapons: hammer in the slot and in hand", Pc.WeaponBar.SlotOf(_barKey) == WeaponTypes.WeaponSlot
                && Pc.Inventory.Get(WeaponTypes.WeaponSlot)?.BaseId == "item.relic_hammer" && held?.ItemId == RealWeapons.ByType("hammer")!.Id,
                $"{WeaponBarKeys.LastAction}; holding {held?.ItemId}");
            var polearmSlot = Pc.Inventory.Slots.FirstOrDefault(kv => kv.Value.BaseId == "item.rusty_polearm").Key;
            SelfTest.Check("weapons: the polearm went to the bag and kept its key", polearmSlot != null && polearmSlot.StartsWith("bag.", StringComparison.Ordinal)
                && Pc.WeaponBar.KeyOf(polearmSlot) >= 0, $"polearm in {polearmSlot}, key {(polearmSlot == null ? -1 : Pc.WeaponBar.KeyOf(polearmSlot))}");
            TestKit.Screenshot("weapons-bar-hammer");
            WeaponBarKeys.Press(_barKey);
        });
        SelfTest.Add(t + 5f, "weapons: the key again holsters it", () =>
            SelfTest.Check("weapons: hammer holstered", !WeaponBarKeys.WeaponInHand(), WeaponBarKeys.LastAction));
        SelfTest.Add(t + 5.5f, "weapons: polearm back by its key", () =>
        {
            var slot = Pc.Inventory.Slots.First(kv => kv.Value.BaseId == "item.rusty_polearm").Key;
            SelfTest.Check("weapons: polearm key pressed", WeaponBarKeys.Press(Pc.WeaponBar.KeyOf(slot)), WeaponBarKeys.LastAction);
        });
        t += 8.5f;

        // The hotkey draws it again after it was put away; the game save never holds it; it can't be dropped.
        SelfTest.Add(t, "weapons: put away", () => LocalPlayer.Inventory.StashHeldItems(false, true));
        SelfTest.Add(t + 2f, "weapons: hotkey", () =>
        {
            int key = WeaponSlotSync.HotkeyIndexOf(RealWeapons.ByType("polearm")!.Id);
            SelfTest.Check("weapons: polearm kept its hotkey through the swaps", key >= 0, HotkeyBridge.Describe());
            if (key >= 0) HotkeyBridge.Trigger(key);
        });
        SelfTest.Add(t + 5f, "weapons: hotkey draws it", () =>
        {
            var held = HeldWeapons.Local();
            SelfTest.Check("weapons: hotkey draws the polearm", held?.ItemId == RealWeapons.ByType("polearm")!.Id, held == null ? "nothing held" : $"holding {held.Value.ItemId}");
        });
        SelfTest.Add(t + 5.5f, "weapons: save data", CheckSaveData);
        SelfTest.Add(t + 6f, "weapons: drop", () =>
        {
            try { LocalPlayer.Inventory.DropRightHandItem(false); }
            catch (Exception e) { SelfTest.Log("weapons: drop threw " + e.Message); }
        });
        SelfTest.Add(t + 7.5f, "weapons: drop leaves nothing", () =>
        {
            WeaponSlotSync.Tick();
            int id = RealWeapons.ByType("polearm")!.Id;
            int owned = LocalPlayer.Inventory.AmountOf(id, true, false);
            SelfTest.Check("weapons: still owned once after a drop", owned == 1, $"amount {owned}");
        });
        t += 9f;

        SelfTest.Add(t, "weapons: unequip", () =>
        {
            Pc.Inventory.Remove(WeaponTypes.WeaponSlot);
            WeaponSlotSync.Tick();
            var owned = RealWeapons.All.Where(w => LocalPlayer.Inventory.AmountOf(w.Id, true, false) > 0).Select(w => w.TypeId).ToList();
            SelfTest.Check("weapons: no weapon item without the slot item", owned.Count == 0, string.Join(", ", owned));
            FreeArrows.Tick();
            SelfTest.Check("weapons: granted arrows taken back", FreeArrows.Granted == 0, $"granted {FreeArrows.Granted}");
        });
        SelfTest.Add(t + 2f, "weapons: hand empty", () =>
        {
            WeaponModels.Scan();
            var held = HeldWeapons.Local();
            SelfTest.Check("weapons: no slot weapon in hand", held == null || !RealWeapons.IsReal(held.Value.ItemId), held == null ? "" : $"holding {held.Value.ItemId}");
            SelfTest.Check("weapons: no type without the item", WeaponModels.Active == null);
            SelfTest.Check("weapons: projectile drop back on", ProjectileDrop.Drop != false, $"drop {ProjectileDrop.Drop}");
        });
        return t + 3f;
    }

    /// <summary>One typed item per weapon type, from items.json.</summary>
    private static readonly (string Item, string Type)[] Typed =
    {
        ("item.rage", "great_sword"), ("item.relic_hammer", "hammer"), ("item.axe_of_swiftness", "axe"),
        ("item.rusty_polearm", "polearm"), ("item.soulstring", "greatbow"), ("item.dull_longsword", "long_sword"),
    };

    /// <summary>Puts a typed item in the weapon slot, lets the slot sync give its game item and draws it as its weapon bar key does.</summary>
    internal static void Wield(string itemId, string typeId, Rarity rarity = Rarity.Rare)
    {
        EquipItem(itemId, typeId, rarity);
        TestKit.FreeHands();
        WeaponSlotSync.Draw();
    }

    private static int _barKey;

    /// <summary>COS-189: a unique bow in hand is given the best arrow when the player has none, and a spent arrow comes back.</summary>
    private static void CheckFreeArrows()
    {
        var inv = LocalPlayer.Inventory;
        int best = FreeArrows.BestArrowId;
        SelfTest.Log($"weapons: arrows {GameItems.Matching("arrow")}; free arrow {best}");
        if (best <= 0) { SelfTest.Check("weapons: an arrow item exists", false); return; }
        int have = inv.AmountOf(best, true, false);
        if (have > 0) inv.RemoveItem(best, have, false, false, false, null, true);
        FreeArrows.Tick();
        SelfTest.Check("weapons: unique bow is given an arrow", FreeArrows.Active && inv.AmountOf(best, true, false) >= 1 && FreeArrows.Granted >= 1,
            $"active {FreeArrows.Active}, arrows {inv.AmountOf(best, true, false)}, granted {FreeArrows.Granted}");
        int before = inv.AmountOf(best, true, false), shots = FreeArrows.FreeShots;
        inv.RemoveItem(best, 1, false, false, false, null, true); // the game spending one
        bool refunded = FreeArrows.OnAmmoSpent(best);
        SelfTest.Check("weapons: a spent arrow comes back", refunded && inv.AmountOf(best, true, false) == before && FreeArrows.FreeShots == shots + 1,
            $"{before} -> {inv.AmountOf(best, true, false)}, refunded {refunded}");
    }

    /// <summary>Exactly the type's game item is owned, once, and it is in hand.</summary>
    private static void CheckOwned(string typeId)
    {
        var inv = LocalPlayer.Inventory;
        var want = RealWeapons.ByType(typeId)!;
        var owned = RealWeapons.All.Select(w => (w.TypeId, N: inv.AmountOf(w.Id, true, false))).Where(x => x.N > 0).ToList();
        SelfTest.Check($"weapons: {typeId} owned alone, once", owned.Count == 1 && owned[0].TypeId == typeId && owned[0].N == 1,
            string.Join(", ", owned.Select(x => $"{x.TypeId} x{x.N}")));
        var held = HeldWeapons.Local();
        SelfTest.Check($"weapons: {typeId} in hand from the slot", held?.ItemId == want.Id,
            held == null ? "nothing held; " + GameInventoryDiag(inv, want.Id) : $"holding {held.Value.ItemId}");
    }

    /// <summary>The inventory save data the game would write holds none of our ids; the live game keeps them.</summary>
    private static void CheckSaveData()
    {
        var inv = LocalPlayer.Inventory;
        try
        {
            var data = inv.Sons_Save_ISaveGameSerializer_TheForest_Items_Inventory_PlayerInventory_PlayerInventorySaveData__OnSerialize();
            string json = data == null ? "" : Newtonsoft.Json.JsonConvert.SerializeObject(data);
            var inSave = RealWeapons.All.Where(w => json.Contains(w.Id.ToString(), StringComparison.Ordinal)).Select(w => w.Id).ToList();
            SelfTest.Check("weapons: inventory save holds no weapon item", data != null && inSave.Count == 0, string.Join(", ", inSave));
            int id = RealWeapons.ByType("polearm")!.Id;
            SelfTest.Check("weapons: live game keeps it", inv.AmountOf(id, true, false) == 1 && WeaponSlotSync.HotkeyIndexOf(id) >= 0);
        }
        catch (Exception e)
        {
            SelfTest.Check("weapons: save data readable", false, e.Message);
        }
    }

    /// <summary>COS-129 diagnosis: what the game's inventory says about an item and the hand slots.</summary>
    internal static string GameInventoryDiag(TheForest.Items.Inventory.PlayerInventory inv, int id)
    {
        var sb = new StringBuilder();
        try
        {
            sb.Append($"amount {inv.AmountOf(id, true, false)} (max {inv.GetMaxAmountOf(id)}), right hand ok {inv.CanEquipItemInRightHand(id)}, left ok {inv.CanEquipItemInLeftHand(id)}");
            foreach (var slot in Enum.GetValues(typeof(EquipmentSlot)).Cast<EquipmentSlot>())
                if (inv.IsSlotLocked(slot)) sb.Append($", {slot} LOCKED");
            sb.Append($", view {inv.CurrentView}, enabled {inv.enabled}/{inv.gameObject.activeInHierarchy}");
            var heldOnly = inv.HeldOnlyItemController?.TryCast<Il2CppSystem.Object>();
            sb.Append($", right {inv.RightHandItem?._itemID.ToString() ?? "-"}, left {inv.LeftHandItem?._itemID.ToString() ?? "-"}, heldOnly {(heldOnly == null ? "-" : heldOnly.GetIl2CppType().Name)}");
            sb.Append($", inWater {LocalPlayer.InWater}, midAction {LocalPlayer.IsInMidAction}, inInventory {LocalPlayer.IsInInventory}, toggleBlocked {inv.IsInventoryToggleBlocked()}");
            sb.Append($", pos {LocalPlayer.Transform.position}");
        }
        catch (Exception e)
        {
            sb.Append(" diag failed: " + e.Message);
        }
        return sb.ToString();
    }

    internal static void EquipItem(string itemId, string typeId, Rarity rarity = Rarity.Rare)
    {
        Pc.Inventory.Remove(WeaponTypes.WeaponSlot);
        var item = new ItemInstance { BaseId = itemId, Level = 10, Rarity = rarity, Stats = Array.Empty<RolledStat>() };
        Pc.Inventory.AddToBag(item);
        var slot = Pc.Inventory.Slots.First(kv => kv.Key.StartsWith("bag.", StringComparison.Ordinal) && kv.Value == item).Key;
        SelfTest.Check($"weapons: {typeId} item equipped", Pc.Inventory.Equip(slot, ModContent.Db));
    }

    /// <summary>Swing speed, damage, the hit box, tree cutting, and the type's sounds being registered cues.</summary>
    private static void CheckMeleeRules(WeaponTypeDef type)
    {
        var skin = WeaponModels.LocalSkin!;
        StatBridge.Frame(0.5f);
        double wantSwing = StatBridge.AttackSpeed * type.SwingSpeed;
        SelfTest.Check($"weapons: {type.Id} swing speed", StatBridge.MeleeSwingRatio is double r && Math.Abs(r - wantSwing) < 0.01,
            $"ratio {StatBridge.MeleeSwingRatio:0.00}, want {wantSwing:0.00}");
        var data = WeaponModels.Base?._meleeWeaponData;
        var infos = skin.Holder.GetComponentsInChildren<weaponInfo>(true);
        float game = data != null ? data.Damage : WeaponModels.Base?._weaponInfo?._weaponDamage ?? 0;
        var box = infos.SelectMany(w => w.GetComponents<BoxCollider>()).FirstOrDefault();
        SelfTest.Log($"weapons: {type.Id} hit box {box?.size} center {box?.center}, game damage {game} (data {data != null}), factor {WeaponModels.MeleeDamageFactor:0.00}");
        // COS-191: a hit's base is the weapon's own damage (the slot item's level and rarity).
        var slotItem = Pc.Inventory.Get(WeaponTypes.WeaponSlot)!;
        double d = WeaponTypes.WeaponDamage(type, slotItem.Level, slotItem.Rarity);
        SelfTest.Check($"weapons: {type.Id} damage", game > 0 && Math.Abs(WeaponModels.MeleeDamageFactor * game - d) < 0.05 && WeaponModels.ModWeaponPierce > 0,
            $"{game} x {WeaponModels.MeleeDamageFactor:0.00}, want {d:0.0}; pierce {WeaponModels.ModWeaponPierce:0}");
        float tree = WeaponModels.Base != null ? WeaponModels.Base.GetTreeCutDamageMultiplier() : -1;
        bool flag = infos.Length > 0 && infos.All(i => i.noTreeCut);
        SelfTest.Check($"weapons: {type.Id} tree cutting {(type.CutsTrees ? "on" : "off")}", type.CutsTrees ? tree > 0 : tree == 0 && flag,
            $"multiplier {tree:0.00}, noTreeCut {flag}");
        CheckSounds(type);
    }

    private static void CheckBowRules(WeaponTypeDef type)
    {
        // COS-118: the global drop flag is off while the no-gravity bow is held.
        bool straight = !type.NoGravity || ProjectileDrop.Drop == false;
        SelfTest.Check($"weapons: {type.Id} arrows don't drop", straight, $"global drop {ProjectileDrop.Drop}");
        BowDraw.Tick(type);
        if (BowDraw.Current is { } cur && BowDraw.Base is { } b)
            SelfTest.Check($"weapons: {type.Id} draw animation slowed", Math.Abs(cur - b * type.DrawSpeed) < 1e-3, $"bow speed {cur:0.000}, game {b:0.000}, x{type.DrawSpeed}");
        else SelfTest.Log($"weapons: {type.Id} draw animation: no bow speed parameter on the player animator (shot interval only)");
        StatBridge.Frame(0.5f);
        double want = WeaponTypes.ShotDelayFactor(type) / StatBridge.AttackSpeed;
        SelfTest.Check($"weapons: {type.Id} slower draw", Math.Abs(StatBridge.LastShotDelayFactor - want) < 0.01,
            $"shot delay x{StatBridge.LastShotDelayFactor:0.00}, want x{want:0.00}");
        CheckSounds(type);
    }

    private static void CheckSounds(WeaponTypeDef type)
    {
        var missing = new[] { type.SwingSound, type.HitSound }.Where(c => c != null && !ModAudio.KnownCues.Contains(c)).ToList();
        SelfTest.Check($"weapons: {type.Id} sounds registered", !ModAudio.Ready || missing.Count == 0, string.Join(", ", missing));
    }

    private const int SwingRetries = 6;

    /// <summary>A real attack through the game weapon: its swing event plays the type's sound and lights the trail.</summary>
    private static void Attack()
    {
        _swingsBefore = WeaponModels.LocalSkin?.Swings ?? 0;
        TestKit.TapAttack();
    }

    /// <summary>The game drops an attack pressed while it is still busy (just after an equip): press once more.</summary>
    private static void RetryAttack()
    {
        if ((WeaponModels.LocalSkin?.Swings ?? 0) == _swingsBefore) TestKit.TapAttack();
    }

    private static void CheckSwingFx(string typeId)
    {
        var skin = WeaponModels.LocalSkin;
        int swings = (skin?.Swings ?? 0) - _swingsBefore;
        SelfTest.Check($"weapons: {typeId} swing event", swings > 0, $"{swings} swings");
        SelfTest.Check($"weapons: {typeId} swing trail", skin?.TrailLit == true, skin?.Trail == null ? "no trail" : skin.TrailLit ? "" : "never lit");
        SelfTest.Check($"weapons: {typeId} swing sound", !ModAudio.Ready || WeaponFx.LastSwingCue == skin?.Type.SwingSound, $"last swing cue {WeaponFx.LastSwingCue}");
    }

    private static WeaponTypeDef? CheckActive(string typeId)
    {
        var type = WeaponModels.Active;
        SelfTest.Check($"weapons: {typeId} active", type?.Id == typeId, type?.Id ?? "none");
        var skin = WeaponModels.LocalSkin;
        if (type == null || skin?.Model == null) return null;

        var ours = skin.Model.GetComponentsInChildren<Renderer>(true);
        // Models of earlier types are destroyed at the end of the frame: skip anything of ours.
        var game = skin.Holder.GetComponentsInChildren<Renderer>(true).Where(r => !IsOurs(r.transform, skin.Holder) && r.TryCast<ParticleSystemRenderer>() == null).ToList();
        SelfTest.Check($"weapons: {typeId} model from the bundle", skin.Source.StartsWith("bundle:", StringComparison.Ordinal), skin.Source);
        SelfTest.Check($"weapons: {typeId} model shown, game mesh hidden", ours.Any(r => r.enabled) && game.All(r => !r.enabled),
            $"{game.Count} game renderers, model size {Size(skin.Model):0.00} m, at {skin.Model.transform.localPosition} scale {skin.Model.transform.localScale.x:0.000}");
        return type;
    }

    private static bool IsOurs(Transform t, Transform holder)
    {
        for (var p = t; p != null && p != holder; p = p.parent)
            if (p.name.StartsWith("cots-", StringComparison.Ordinal) || p.name.Contains("Arrow") || p.name.Contains("Projectile")) return true;
        return false;
    }

    private static float Size(GameObject go)
    {
        var r = go.GetComponentInChildren<MeshRenderer>();
        return r != null ? r.bounds.size.magnitude : 0;
    }

    private static string Chain(Transform t)
    {
        var parts = new List<string>();
        for (var p = t; p != null; p = p.parent) parts.Add(p.name);
        parts.Reverse();
        return string.Join("/", parts);
    }

    internal static string Tree(Transform t, int depth)
    {
        var sb = new StringBuilder();
        sb.Append(' ', depth * 2).Append(t.name).Append(t.gameObject.activeSelf ? "" : " (off)")
            .Append($" pos {t.localPosition} rot {t.localEulerAngles} scale {t.localScale} :");
        foreach (var c in t.GetComponents<Component>())
        {
            if (c == null) continue;
            sb.Append(' ').Append(c.GetIl2CppType().Name);
            var r = c.TryCast<Renderer>();
            if (r != null)
            {
                var mf = t.GetComponent<MeshFilter>();
                var mesh = mf != null ? mf.sharedMesh : r.TryCast<SkinnedMeshRenderer>()?.sharedMesh;
                sb.Append($"[{(r.enabled ? "on" : "off")} mesh={mesh?.name} bounds={mesh?.bounds} world={r.bounds.size} shader={r.sharedMaterial?.shader?.name}]");
            }
        }
        sb.Append('\n');
        if (depth < 8)
            for (int i = 0; i < t.childCount; i++) sb.Append(Tree(t.GetChild(i), depth + 1));
        return sb.ToString();
    }
}
