using ChampionsOfTheSons.Core;
using ChampionsOfTheSons.Core.Content;
using ChampionsOfTheSons.Core.Networking;
using ChampionsOfTheSons.Core.Rules;
using ChampionsOfTheSons.Mod.Bridge;
using TheForest.Utils;

namespace ChampionsOfTheSons.Mod.Admin;

/// <summary>
/// The local player's admin switches and the effect of one <see cref="AdminGrant"/> on the local character.
/// The host applies grants for itself directly; a client applies the ones its host sends (COS-99).
/// </summary>
public static class AdminState
{
    private static readonly Rng Rng = Rng.FromTime();

    /// <summary>God mode: incoming hits do no damage (PlayerBridge).</summary>
    public static bool God { get; private set; }

    /// <summary>Every slotted spell stays ready.</summary>
    public static bool NoCooldowns { get; private set; }

    /// <summary>Casts cost nothing and the energy bar stays full, whatever a spell costs against max energy (COS-173).</summary>
    public static bool InfiniteEnergy { get; private set; }

    public static void Reset()
    {
        God = false;
        NoCooldowns = false;
        InfiniteEnergy = false;
    }

    public static void Update()
    {
        if (NoCooldowns) PlayerState.Character.Spells.ReadyAll();
        var stamina = InfiniteEnergy ? LocalPlayer.Vitals?.Stamina : null;
        if (stamina != null && stamina._currentValue < stamina.GetMax()) stamina.SetCurrentValue(stamina.GetMax());
        // Hits are zeroed in PlayerBridge; some game damage (seen 1 s after a hit, fall, fire) bypasses PlayerStats.Hit, so health is held at max too.
        var health = God ? LocalPlayer.Vitals?.Health : null;
        if (health != null && health._currentValue > 0 && health._currentValue < health.GetMax()) health.SetCurrentValue(health.GetMax());
    }

    /// <summary>Applies a grant to the local character and returns what happened, for the console reply.</summary>
    public static string Apply(AdminGrant g)
    {
        var pc = PlayerState.Character;
        switch (g.Action)
        {
            case AdminAction.AddXp:
                pc.AddXp(g.Amount);
                return $"+{g.Amount:0} XP (× XP gain) -> level {pc.Progress.Level}, {pc.Progress.Xp:0} XP";
            case AdminAction.SetLevel:
                pc.SetLevel((int)g.Amount);
                return $"level set to {pc.Progress.Level}";
            case AdminAction.AddLevels:
                pc.SetLevel(pc.Progress.Level + (int)g.Amount);
                return $"level now {pc.Progress.Level}";
            case AdminAction.AddPerkPoints:
                pc.Progress.BonusPerkPoints = Math.Max(0, pc.Progress.BonusPerkPoints + (int)g.Amount);
                return $"bonus perk points now {pc.Progress.BonusPerkPoints} ({pc.Perks.AvailablePoints(pc.Progress)} to spend)";
            case AdminAction.Respec:
                pc.Respec();
                return $"perks refunded ({pc.Perks.AvailablePoints(pc.Progress)} points to spend)";
            case AdminAction.GiveItem:
                return GiveItem(g.Arg, Math.Max(1, (int)g.Amount));
            case AdminAction.Heal:
                return Heal();
            case AdminAction.God:
                God = g.Amount > 0;
                return $"god mode {(God ? "on" : "off")}";
            case AdminAction.NoCooldowns:
                NoCooldowns = g.Amount > 0;
                if (NoCooldowns) pc.Spells.ReadyAll();
                return $"spell cooldowns {(NoCooldowns ? "off" : "on")}";
            case AdminAction.LearnAllSpells:
                return LearnAllSpells();
            case AdminAction.SetMastery:
            {
                string? spell = g.Arg == "all" ? null : g.Arg;
                if (spell != null && !ModContent.Db.Spells.ContainsKey(spell)) return $"unknown spell '{g.Arg}'";
                pc.SetMastery(spell, (int)g.Amount);
                return $"mastery {(int)g.Amount} for {(spell ?? "every spell")}";
            }
            case AdminAction.InfiniteEnergy:
                InfiniteEnergy = g.Amount > 0;
                return $"infinite energy {(InfiniteEnergy ? "on" : "off")}";
            default:
                return $"unknown admin action {g.Action}";
        }
    }

    /// <summary>Arg: an item id, or "random" / "random:&lt;rarity 0-3&gt;".</summary>
    private static string GiveItem(string arg, int level)
    {
        var db = ModContent.Db;
        var loot = new LootRoller(db);
        ItemBaseDef? def;
        if (arg.StartsWith("random", StringComparison.OrdinalIgnoreCase))
        {
            int rarity = arg.Contains(':') && int.TryParse(arg.Split(':')[1], out var r) ? Rarities.Clamp(r) : (int)Rarity.Rare;
            def = loot.PickBase(rarity, level, Rng);
            if (def == null) return $"no item of rarity {rarity} at level {level}";
        }
        else if (!db.Items.TryGetValue(arg, out def))
        {
            return $"unknown item id '{arg}' (try: cots list items <filter>)";
        }
        var item = loot.RollItem(def, level, Rng);
        bool fitted = PlayerState.Character.Inventory.AddToBag(item, def.StackSize);
        return fitted ? $"gave {def.Id} (level {item.Level}, rarity {item.Rarity})" : "bag is full";
    }

    /// <summary>COS-166 playtesting: unlocks every spell, paid for with bonus perk points so the player's own points stay as they
    /// were, and fills empty spell slots. Level requirements are skipped.</summary>
    private static string LearnAllSpells()
    {
        var pc = PlayerState.Character;
        var all = ModContent.Db.Spells.Keys.OrderBy(id => id, StringComparer.Ordinal).ToList();
        var missing = all.Where(id => !pc.Perks.UnlockedSpells.Contains(id)).ToList();
        pc.Progress.BonusPerkPoints += missing.Count * PerkTree.SpellUnlockCost;
        pc.Perks.Restore(new Dictionary<string, int>(pc.Perks.Bought), pc.Perks.UnlockedSpells.Concat(missing).ToList());
        int slotted = 0;
        var unslotted = new Queue<string>(all.Where(id => !pc.Spells.Loadout.Contains(id)));
        for (int slot = 0; slot < SpellBook.Slots && unslotted.Count > 0; slot++)
            if (pc.Spells.Loadout[slot] == null && pc.Spells.Equip(slot, unslotted.Dequeue())) slotted++;
        return $"learned {missing.Count} spell(s), {all.Count} known; {slotted} put in empty slots (swap them in the perk panel's spell list)";
    }

    private static string Heal()
    {
        var vitals = LocalPlayer.Vitals;
        if (vitals == null) return "no local player";
        var health = vitals.Health;
        if (health != null && health._currentValue > 0) health.SetCurrentValue(health.GetMax());
        var stamina = vitals.Stamina;
        if (stamina != null) stamina.SetCurrentValue(stamina.GetMax());
        PlayerEffects.ClearDebuffs();
        return "healed: full health and energy, debuffs cleared";
    }
}
