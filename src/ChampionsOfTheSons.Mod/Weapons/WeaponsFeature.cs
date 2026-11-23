using ChampionsOfTheSons.Core.Rules;
using RedLoader;
using SonsSdk;
using Sons.Items.Core;
using TheForest.Items.Inventory;
using Sons.Weapon;
using TheForest.Utils;
using UnityEngine;

namespace ChampionsOfTheSons.Mod.Weapons;

/// <summary>
/// Weapon types (COS-93): an equipped weapon item puts our own model in the player's hand, on top of one of the game's
/// weapons (its animations and hit logic), with per-type rules.
/// </summary>
public sealed class WeaponsFeature : IModFeature
{
    private static KeybindConfigEntry? _key;

    public void Init()
    {
        _key = Config.WeaponKey; // created in Config so the key move (COS-107) can reach it
        _key.Notify(() => DrawBase());
        Bridge.EnemyPatches.LocalWeaponHit += (_, weapon) =>
        {
            if (weapon == WeaponClass.Melee) WeaponFx.LocalHit();
        };
    }

    /// <summary>Equips the first game weapon from the equipped type's base list that the player owns. False if none.</summary>
    public static bool DrawBase()
    {
        var type = WeaponTypes.Equipped(PlayerState.Character.Inventory, ModContent.Db);
        var inv = LocalPlayer.Inventory;
        if (type == null || inv == null) return false;
        foreach (var name in type.BaseItems)
        {
            int id = GameItems.Find(name, -1);
            if (id >= 0 && inv.AmountOf(id, true, false) > 0) return inv.TryEquip(id, false, true, false);
        }
        return false;
    }

    public void Update(float dt, bool inWorld)
    {
        if (!inWorld) return;
        WeaponModels.Tick(dt);
        BowDraw.Tick(WeaponModels.Active);
    }

    public void OnGameStart() => WeaponModels.Reset();

    public void RegisterSelfTests()
    {
        float end = WeaponsSelfTest.Register();
        GlowSelfTest.Register(Math.Max(55f, end + 2f)); // never overlap the weapon steps (their retries stretch the timeline)
    }
}

/// <summary>Game item ids by name (the item database), with a fallback id when the name is not found.</summary>
internal static class GameItems
{
    public static int Find(string name, int fallback)
    {
        var list = ItemDatabaseManager.Items;
        for (int i = 0; i < list.Count; i++)
            if (list[i] != null && string.Equals(list[i].Name, name, StringComparison.OrdinalIgnoreCase)) return list[i].Id;
        return fallback;
    }

    /// <summary>"id:name" of every game item whose name contains one of the words (self-test logs).</summary>
    public static string Matching(params string[] words)
    {
        var list = ItemDatabaseManager.Items;
        var hits = new List<string>();
        for (int i = 0; i < list.Count; i++)
            if (list[i] != null && words.Any(w => list[i].Name.Contains(w, StringComparison.OrdinalIgnoreCase))) hits.Add($"{list[i].Id}:{list[i].Name}");
        return string.Join(" | ", hits);
    }
}

/// <summary>The game's held weapon objects: each held item root carries a <see cref="HeldItemIdentifier"/> with its item id.</summary>
internal static class HeldWeapons
{
    // Game item ids (logged by the weapons self-test from the item database): one-handed melee weapons, spears, bows.
    private static readonly HashSet<int> MeleeIds = new() { 359, 367, 356, 379, 431, 477, 380, 525, 449 };
    private static readonly HashSet<int> SpearIds = new() { 474 };
    private static readonly HashSet<int> BowIds = new() { 443, 360 };

    public static HeldKind Classify(int itemId) =>
        MeleeIds.Contains(itemId) || RealWeapons.IsReal(itemId) ? HeldKind.Melee : SpearIds.Contains(itemId) ? HeldKind.Spear : BowIds.Contains(itemId) ? HeldKind.Bow : HeldKind.Other;

    public readonly record struct Held(Transform Holder, int ItemId, HeldKind Kind);

    /// <summary>The local player's held weapon (melee, spear or bow), or null.</summary>
    public static Held? Local()
    {
        // Only the player's own active children (COS-132: was every HeldItemIdentifier in memory, five times a second).
        var root = LocalPlayer.Transform;
        if (root == null) return null;
        foreach (var id in root.GetComponentsInChildren<HeldItemIdentifier>(false))
        {
            if (id == null) continue;
            var kind = Classify(id._itemId);
            if (kind != HeldKind.Other) return new Held(id.transform, id._itemId, kind);
        }
        return null;
    }

    /// <summary>Weapons held by other players' bodies.</summary>
    public static IEnumerable<Held> Remote() => All().Where(h => !IsUnderLocalPlayer(h.Holder));

    private static IEnumerable<Held> All()
    {
        foreach (var id in Resources.FindObjectsOfTypeAll<HeldItemIdentifier>())
        {
            if (id == null || !id.gameObject.activeInHierarchy) continue;
            var kind = Classify(id._itemId);
            if (kind != HeldKind.Other) yield return new Held(id.transform, id._itemId, kind);
        }
    }

    /// <summary>The local player's active melee weapon controller, or null.</summary>
    public static MeleeWeaponController? LocalMelee()
    {
        var held = Local();
        return held?.Holder.GetComponent<MeleeWeaponController>();
    }

    private static bool IsUnderLocalPlayer(Transform t)
    {
        var root = LocalPlayer.Transform;
        if (root == null) return false;
        for (var p = t; p != null; p = p.parent)
            if (p == root) return true;
        return false;
    }
}
