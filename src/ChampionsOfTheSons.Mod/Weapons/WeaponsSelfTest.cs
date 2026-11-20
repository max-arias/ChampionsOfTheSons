using System.Text;
using ChampionsOfTheSons.Core;
using ChampionsOfTheSons.Core.Content;
using ChampionsOfTheSons.Core.Rules;
using ChampionsOfTheSons.Mod.Bridge;
using Sons.Items.Core;
using Sons.Weapon;
using TheForest.Utils;
using UnityEngine;

namespace ChampionsOfTheSons.Mod.Weapons;

/// <summary>COS-93 checks: equip a game weapon, put each melee weapon type's model on it, check the rules, take screenshots.</summary>
internal static class WeaponsSelfTest
{
    private static PlayerCharacter Pc => PlayerState.Character;

    /// <summary>One typed item per melee type, from items.json.</summary>
    private static readonly (string Item, string Type)[] MeleeItems =
    {
        ("item.dull_longsword", "long_sword"), ("item.rage", "great_sword"), ("item.relic_hammer", "hammer"), ("item.axe_of_swiftness", "axe"),
    };

    public static void Register()
    {
        SelfTest.Add(1f, "weapons: equip machete", () =>
        {
            TestKit.Unpause();
            int id = GameItems.Find("Machete", 359);
            var inv = LocalPlayer.Inventory;
            if (inv.AmountOf(id, true, false) == 0) inv.AddItem(id, 1, true, false, null);
            SelfTest.Check("weapons: machete equipped", inv.TryEquip(id, false, true, false), $"item {id}");
        });
        SelfTest.Add(5f, "weapons: game weapon", () =>
        {
            var held = HeldWeapons.LocalMelee();
            SelfTest.Check("weapons: held melee controller found", held != null);
            if (held == null) return;
            var d = held._meleeWeaponData;
            SelfTest.Log($"weapons: held {Chain(held.transform)} data damage={d?.Damage} swing={d?.SwingSpeed} stamina={d?.StaminaCost} weight={d?.Weight}");
            SelfTest.Log("weapons: held tree\n" + Tree(held.transform, 0));
            SelfTest.Log("weapons: game weapons " + GameItems.Matching("axe", "machete", "katana", "sword", "spear", "bow", "club", "baton", "putter", "knife", "crossbow"));
            SelfTest.Log($"weapons: stand-in folder {GltfStandIn.Folder} present={GltfStandIn.Available("sword_A")}");
        });

        float t = 6f;
        foreach (var (itemId, typeId) in MeleeItems)
        {
            SelfTest.Add(t, $"weapons: {typeId} on", () => EquipType(itemId, typeId));
            SelfTest.Add(t + 2f, $"weapons: {typeId} screenshot", () => TestKit.Screenshot("weapons-" + typeId));
            t += 3f;
        }

        // Spear and bow: the draw key path (equip the type's base game weapon), then the same checks.
        foreach (var (itemId, typeId, gameName) in new[] { ("item.rusty_polearm", "polearm", "CraftedSpear"), ("item.soulstring", "greatbow", "CraftedBow") })
        {
            SelfTest.Add(t, $"weapons: draw {gameName}", () =>
            {
                Pc.Inventory.Remove(WeaponTypes.WeaponSlot);
                EquipItem(itemId, typeId);
                int id = GameItems.Find(gameName, -1);
                var inv = LocalPlayer.Inventory;
                if (id >= 0 && inv.AmountOf(id, true, false) == 0) inv.AddItem(id, 1, true, false, null);
                SelfTest.Check($"weapons: draw key equips {gameName}", WeaponsFeature.DrawBase(), $"item {id}");
            });
            SelfTest.Add(t + 4f, $"weapons: {typeId} on", () =>
            {
                var held = HeldWeapons.Local();
                if (held != null) SelfTest.Log($"weapons: held {held.Value.Kind} {Chain(held.Value.Holder)}\n" + Tree(held.Value.Holder, 0));
                WeaponModels.Scan();
                CheckActive(typeId);
            });
            SelfTest.Add(t + 6f, $"weapons: {typeId} screenshot", () => TestKit.Screenshot("weapons-" + typeId));
            t += 7f;
        }

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
        });
    }

    private static void EquipItem(string itemId, string typeId)
    {
        Pc.Inventory.Remove(WeaponTypes.WeaponSlot);
        var item = new ItemInstance { BaseId = itemId, Level = 10, Rarity = Rarity.Rare, Stats = Array.Empty<RolledStat>() };
        Pc.Inventory.AddToBag(item);
        var slot = Pc.Inventory.Slots.First(kv => kv.Key.StartsWith("bag.", StringComparison.Ordinal) && kv.Value == item).Key;
        SelfTest.Check($"weapons: {typeId} item equipped", Pc.Inventory.Equip(slot, ModContent.Db));
    }

    private static void EquipType(string itemId, string typeId)
    {
        EquipItem(itemId, typeId);
        WeaponModels.Scan();
        var type = CheckActive(typeId);
        if (type == null || type.Base == WeaponBase.Bow) return;
        var skin = WeaponModels.LocalSkin!;

        StatBridge.Frame(0.5f);
        double wantSwing = StatBridge.AttackSpeed * type.SwingSpeed;
        SelfTest.Check($"weapons: {typeId} swing speed", StatBridge.MeleeSwingRatio is double r && Math.Abs(r - wantSwing) < 0.01,
            $"ratio {StatBridge.MeleeSwingRatio:0.00}, want {wantSwing:0.00}");
        var box = skin.Holder.GetComponentsInChildren<weaponInfo>(true).SelectMany(w => w.GetComponents<BoxCollider>()).FirstOrDefault();
        SelfTest.Log($"weapons: {typeId} hit box {box?.size} center {box?.center}, damage factor {WeaponModels.MeleeDamageFactor:0.00}");
    }

    private static Core.Content.WeaponTypeDef? CheckActive(string typeId)
    {
        var type = WeaponModels.Active;
        SelfTest.Check($"weapons: {typeId} active", type?.Id == typeId, type?.Id ?? "none");
        var skin = WeaponModels.LocalSkin;
        if (type == null || skin?.Model == null) return null;

        var ours = skin.Model.GetComponentsInChildren<Renderer>(true);
        // Models of earlier types are destroyed at the end of the frame: skip anything of ours.
        var game = skin.Holder.GetComponentsInChildren<Renderer>(true).Where(r => !IsOurs(r.transform, skin.Holder) && r.TryCast<ParticleSystemRenderer>() == null).ToList();
        SelfTest.Check($"weapons: {typeId} model shown, game mesh hidden", ours.Any(r => r.enabled) && game.All(r => !r.enabled),
            $"{skin.Source}, {game.Count} game renderers, model size {Size(skin.Model):0.00} m");
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
        var r = go.GetComponentInChildren<Renderer>();
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
