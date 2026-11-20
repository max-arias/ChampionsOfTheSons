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
/// COS-93 checks: put each weapon type's model on its game weapon, check the type's rules (swing, damage, trees, throw,
/// arrow drop, draw), swing it for real to check the sound and trail, and take screenshots.
/// </summary>
internal static class WeaponsSelfTest
{
    private static PlayerCharacter Pc => PlayerState.Character;

    /// <summary>One typed item per melee type, from items.json.</summary>
    private static readonly (string Item, string Type)[] MeleeItems =
    {
        ("item.dull_longsword", "long_sword"), ("item.rage", "great_sword"), ("item.relic_hammer", "hammer"), ("item.axe_of_swiftness", "axe"),
    };

    private static int _spearsBefore, _swingsBefore;

    public static void Register()
    {
        SelfTest.Add(1f, "weapons: equip machete", () =>
        {
            TestKit.Unpause();
            int id = GameItems.Find("Machete", 359);
            var inv = LocalPlayer.Inventory;
            bool equipped = TestKit.EquipGameItem(id);
            if (!equipped) SelfTest.Log("weapons: equip refused; " + GameInventoryDiag(inv, id));
            SelfTest.Check("weapons: machete equipped", equipped, $"item {id}");
        });
        SelfTest.Add(5f, "weapons: game weapon", () =>
        {
            var held = HeldWeapons.LocalMelee();
            SelfTest.Check("weapons: held melee controller found", held != null);
            if (held == null) return;
            var d = held._meleeWeaponData;
            SelfTest.Log($"weapons: held {Chain(held.transform)} data damage={d?.Damage} swing={d?.SwingSpeed} stamina={d?.StaminaCost} weight={d?.Weight}");
        });

        // The game swallows the first attacks of a session: a warm-up round with a typed weapon, not checked.
        SelfTest.Add(5.5f, "weapons: warm-up", () =>
        {
            EquipItem("item.axe_of_swiftness", "axe");
            WeaponModels.Scan();
        });
        SelfTest.Add(6.5f, "weapons: warm-up swing", Attack);
        SelfTest.Add(8f, "weapons: warm-up swing 2", Attack);
        SelfTest.Add(9.5f, "weapons: warm-up swing 3", Attack);

        float t = 11.5f;
        foreach (var (itemId, typeId) in MeleeItems)
        {
            SelfTest.Add(t, $"weapons: {typeId} on", () =>
            {
                EquipItem(itemId, typeId);
                WeaponModels.Scan();
                if (CheckActive(typeId) is { } type) CheckMeleeRules(type);
            });
            SelfTest.Add(t + 1.5f, $"weapons: {typeId} swing", Attack);
            SelfTest.Add(t + 1.75f, $"weapons: {typeId} screenshot", () => TestKit.Screenshot("weapons-" + typeId));
            SelfTest.Add(t + 2.6f, $"weapons: {typeId} swing retry", RetryAttack);
            SelfTest.Add(t + 3.8f, $"weapons: {typeId} swing fx", () => CheckSwingFx(typeId));
            t += 4f;
        }

        // Spear and bow: the draw key path (equip the type's base game weapon), then the same checks.
        foreach (var (itemId, typeId, gameName) in new[] { ("item.rusty_polearm", "polearm", "CraftedSpear"), ("item.soulstring", "greatbow", "CraftedBow") })
        {
            SelfTest.Add(t, $"weapons: draw {gameName}", () =>
            {
                Pc.Inventory.Remove(WeaponTypes.WeaponSlot);
                EquipItem(itemId, typeId);
                int id = GameItems.Find(gameName, -1);
                TestKit.EnsureGameItem(id);
                TestKit.FreeHands();
                SelfTest.Check($"weapons: draw key equips {gameName}", WeaponsFeature.DrawBase(), $"item {id}; " + GameInventoryDiag(LocalPlayer.Inventory, id));
            });
            SelfTest.Add(t + 4f, $"weapons: {typeId} on", () =>
            {
                WeaponModels.Scan();
                var type = CheckActive(typeId);
                if (type?.Base == WeaponBase.Spear) CheckMeleeRules(type);
                if (type?.Base == WeaponBase.Bow) CheckBowRules(type);
            });
            SelfTest.Add(t + 5f, $"weapons: {typeId} screenshot", () => TestKit.Screenshot("weapons-" + typeId));
            t += 6f;
        }

        // The polearm can't be thrown: aim, then attack, and the spear must still be in hand. Then a real stab.
        SelfTest.Add(t, "weapons: re-draw polearm", () =>
        {
            Pc.Inventory.Remove(WeaponTypes.WeaponSlot);
            EquipItem("item.rusty_polearm", "polearm");
            TestKit.EnsureGameItem(GameItems.Find("CraftedSpear", 474));
            TestKit.FreeHands();
            SelfTest.Check("weapons: draw key equips CraftedSpear again", WeaponsFeature.DrawBase());
        });
        SelfTest.Add(t + 3f, "weapons: polearm try throw", () =>
        {
            WeaponModels.Scan();
            var spear = WeaponModels.Base?.TryCast<SpearWeaponController>();
            SelfTest.Check("weapons: polearm not throwable", spear != null && WeaponPatches.NoThrow(spear), spear == null ? "no spear controller" : "");
            if (spear == null) return;
            _spearsBefore = LocalPlayer.Inventory.AmountOf(GameItems.Find("CraftedSpear", 474), true, false);
            spear.BlockStart();
            spear.UpdateBlock();
            spear.AttackButtonDown();
        });
        SelfTest.Add(t + 5.5f, "weapons: polearm still in hand", () =>
        {
            var held = HeldWeapons.Local();
            int count = LocalPlayer.Inventory.AmountOf(GameItems.Find("CraftedSpear", 474), true, false);
            SelfTest.Check("weapons: polearm kept after aim + attack", held?.Kind == HeldKind.Spear && count == _spearsBefore,
                $"held {held?.Kind}, spears {_spearsBefore} -> {count}");
        });
        SelfTest.Add(t + 6.5f, "weapons: polearm swing", Attack);
        SelfTest.Add(t + 6.75f, "weapons: polearm swing screenshot", () => TestKit.Screenshot("weapons-polearm-swing"));
        SelfTest.Add(t + 7.6f, "weapons: polearm swing retry", RetryAttack);
        SelfTest.Add(t + 8.8f, "weapons: polearm swing fx", () => CheckSwingFx("polearm"));
        t += 10f;

        SelfTest.Add(t, "weapons: unequip", () =>
        {
            var skin = WeaponModels.LocalSkin;
            var holder = skin?.Holder;
            Pc.Inventory.Remove(WeaponTypes.WeaponSlot);
            WeaponModels.Scan();
            SelfTest.Check("weapons: no type without the item", WeaponModels.Active == null);
            bool restored = holder != null && holder.Find(WeaponSkin.ModelName) == null
                && holder.GetComponentsInChildren<Renderer>(true).Any(r => r.enabled);
            SelfTest.Check("weapons: game model restored", restored);
            SelfTest.Check("weapons: projectile drop back on", ProjectileDrop.Drop != false, $"drop {ProjectileDrop.Drop}");
        });
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
        if (type.Damage is double d)
            SelfTest.Check($"weapons: {type.Id} damage", game > 0 && Math.Abs(WeaponModels.MeleeDamageFactor * game - d) < 0.05,
                $"{game} x {WeaponModels.MeleeDamageFactor:0.00}, want {d}");
        float tree = WeaponModels.Base != null ? WeaponModels.Base.GetTreeCutDamageMultiplier() : -1;
        bool flag = infos.Length > 0 && infos.All(i => i.noTreeCut);
        SelfTest.Check($"weapons: {type.Id} tree cutting {(type.CutsTrees ? "on" : "off")}", type.CutsTrees ? tree > 0 : tree == 0 && flag,
            $"multiplier {tree:0.00}, noTreeCut {flag}");
        CheckSounds(type);
    }

    private static void CheckBowRules(WeaponTypeDef type)
    {
        SelfTest.Check($"weapons: {type.Id} arrows don't drop", !type.NoGravity || ProjectileDrop.Drop == false, $"drop {ProjectileDrop.Drop}");
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

    /// <summary>A real attack through the game weapon: its swing event plays the type's sound and lights the trail.</summary>
    private static void Attack()
    {
        _swingsBefore = WeaponModels.LocalSkin?.Swings ?? 0;
        WeaponModels.Base?.AttackButtonDown();
    }

    /// <summary>The game drops an attack pressed while it is still busy (just after an equip): press once more.</summary>
    private static void RetryAttack()
    {
        if ((WeaponModels.LocalSkin?.Swings ?? 0) == _swingsBefore) WeaponModels.Base?.AttackButtonDown();
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
