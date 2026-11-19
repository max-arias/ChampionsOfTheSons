using ChampionsOfTheSons.Core;
using ChampionsOfTheSons.Core.Content;
using ChampionsOfTheSons.Core.Rules;
using ChampionsOfTheSons.Mod.Bridge;
using ChampionsOfTheSons.Mod.Crafting;
using ChampionsOfTheSons.Mod.Hud;
using ChampionsOfTheSons.Mod.Loot;
using ChampionsOfTheSons.Mod.UI;
using Sons.Items.Core;
using TheForest.Utils;
using UnityEngine;

namespace ChampionsOfTheSons.Mod.Followups;

/// <summary>COS-42/43/44/46/47/48/49 in-game checks: reward items, hurt numbers, scrap and previews, event and utility perks, unique flags.</summary>
internal static class FollowupsSelfTest
{
    private static PlayerCharacter Pc => PlayerState.Character;
    private static float MaxHealth => LocalPlayer.Vitals.Health.GetMax();
    private static float Health => LocalPlayer.Vitals.Health._currentValue;

    public static void Register()
    {
        SelfTest.Add(1f, "followups: probe", Probe);
        SelfTest.Add(2f, "followups: level-up rewards", LevelRewards);
        SelfTest.Add(3f, "followups: level-up with a full bag", FullBagReward);
        SelfTest.Add(4f, "followups: hurt numbers", HurtNumbers);
        SelfTest.Add(5f, "followups: scrap", Scrap);
        SelfTest.Add(6f, "followups: previews", Previews);
        SelfTest.Add(7f, "followups: dodge perks", DodgePerks);
        SelfTest.Add(8f, "followups: adrenaline rush", Adrenaline);
        SelfTest.Add(9f, "followups: capacity perks", Capacity);
        SelfTest.Add(10f, "followups: daily perks", Daily);
        SelfTest.Add(11f, "followups: unique flags", UniqueFlags);
        SelfTest.Add(12f, "followups: cleanup", Cleanup);
    }

    private static void SetPerks(params string[] ids)
    {
        Pc.Perks.Restore(ids.GroupBy(i => i).ToDictionary(g => g.Key, g => g.Count()), Array.Empty<string>());
        Pc.RebuildAll();
    }

    private static void Flags(params string[] flags) =>
        Pc.Stats.SetSource("test.flags", flags.Select(f => new StatModifier(f, EffectOp.Enable, 1)).ToList());

    private static void FullHealth() => LocalPlayer.Vitals.Health.SetCurrentValue(MaxHealth);

    // ---------------------------------------------------------------- probe (facts for the design notes)

    private static void Probe()
    {
        var names = new List<string>();
        var list = ItemDatabaseManager.Items;
        for (int i = 0; i < list.Count; i++)
            if (list[i] != null) names.Add($"{list[i].Id}:{list[i].Name}:{list[i].MaxAmount}");
        SelfTest.Log($"followups: {names.Count} game items: {string.Join(" | ", names)}");
        try { SelfTest.Log($"followups: game day {Sons.Environment.TimeOfDayHolder.GetTimeOfDay().Days}"); }
        catch (Exception e) { SelfTest.Log("followups: day read failed " + e.Message); }
        try
        {
            var enabled = new Il2CppSystem.Collections.Generic.List<UnityEngine.InputSystem.InputAction>();
            UnityEngine.InputSystem.InputSystem.ListEnabledActions(enabled);
            var onK = new List<string>();
            for (int i = 0; i < enabled.Count; i++)
                foreach (var b in enabled[i].bindings)
                    if (enabled[i].name != "crafting_key" && b.effectivePath != null && b.effectivePath.ToLowerInvariant().EndsWith("/k")) onK.Add(enabled[i].name + " " + b.effectivePath);
            SelfTest.Log($"followups: {enabled.Count} enabled input actions, bound to K: [{string.Join(", ", onK)}]");
            SelfTest.Check("followups: crafting key K is free in the game's bindings", onK.Count == 0, string.Join(", ", onK));
        }
        catch (Exception e) { SelfTest.Log("followups: input scan failed " + e.Message); }
    }

    // ---------------------------------------------------------------- COS-43

    private static void LevelRewards()
    {
        LootFeature.ClearAll();
        Pc.Inventory.Restore(Array.Empty<KeyValuePair<string, ItemInstance>>());
        int hearts(PlayerCharacter p) => p.Inventory.Slots.Where(kv => kv.Value.BaseId == Progress.HeartOfPurity).Sum(kv => kv.Value.Amount);
        double need = 0;
        for (int l = Pc.Progress.Level; l < 20; l++) need += XpCurve.Goal(l);
        Pc.AddXp(need / Math.Max(0.01, Pc.Stats.Get(StatIds.ExpGain)));
        SelfTest.Check("followups: reached level 20", Pc.Progress.Level >= 20, Pc.Progress.Level.ToString());
        SelfTest.Check("followups: level 20 put a Heart of Purity in the bag", hearts(Pc) >= 1, $"{hearts(Pc)} hearts");
    }

    private static void FullBagReward()
    {
        LootFeature.ClearAll();
        Pc.Inventory.Restore(Array.Empty<KeyValuePair<string, ItemInstance>>());
        int i = 0;
        while (Pc.Inventory.AddToBag(new ItemInstance { BaseId = "item.filler" + i++, Level = 1 })) { }
        int groundBefore = LootFeature.Ground.Count;
        LootFeature.GiveReward(Progress.GreaterMutatedHeart);
        var onGround = LootFeature.Ground.All.FirstOrDefault(d => d.Item.BaseId == Progress.GreaterMutatedHeart);
        SelfTest.Check("followups: full bag reward lands on the ground", onGround != null && LootFeature.Ground.Count == groundBefore + 1, $"{LootFeature.Ground.Count} drops");
        SelfTest.Check("followups: full bag reward is not lost from the bag either", Pc.Inventory.Slots.All(kv => kv.Value.BaseId != Progress.GreaterMutatedHeart));
        LootFeature.ClearAll();
        Pc.Inventory.Restore(Array.Empty<KeyValuePair<string, ItemInstance>>());
    }

    // ---------------------------------------------------------------- COS-44

    private static void HurtNumbers()
    {
        var hud = HudFeature.Instance!;
        FullHealth();
        int n = hud.Numbers.Spawned;
        LocalPlayer.Stats.Hit(7f);
        SelfTest.Check("followups: taking damage shows a number", hud.Numbers.Spawned == n + 1, $"'{hud.Numbers.LastText}', incoming {PlayerBridge.LastIncoming}");
        SelfTest.Check("followups: hurt number is red", hud.Numbers.LastColor == DamageNumbers.HurtColor);

        Pc.Stats.SetSource("test.alwaysdodge", new[] { new StatModifier(StatIds.GetHitChance, EffectOp.Mult, 0) });
        n = hud.Numbers.Spawned;
        LocalPlayer.Stats.Hit(7f);
        Pc.Stats.RemoveSource("test.alwaysdodge");
        SelfTest.Check("followups: a dodge shows 'Dodge'", hud.Numbers.Spawned == n + 1 && hud.Numbers.LastText == UiText.T("ui.followups.dodge", "Dodge"), hud.Numbers.LastText);

        hud.OnPlayerHurt(30, false, DamageKind.Magic);
        var magic = hud.Numbers.LastColor;
        hud.OnPlayerHurt(30, false, DamageKind.Physical);
        SelfTest.Check("followups: magic damage is colored differently from physical", magic != hud.Numbers.LastColor && magic == DamageNumbers.MagicColor);
    }

    // ---------------------------------------------------------------- COS-47 / COS-48

    private static void Scrap()
    {
        var panel = CraftingFeature.Panel;
        panel.Open();
        Pc.Inventory.Restore(Array.Empty<KeyValuePair<string, ItemInstance>>());
        var item = TestKit.Give(null, 5, 90);
        string key = Inventory.Bag(0);
        panel.SetMode(CraftMode.Scrap);
        panel.Clear();
        SelfTest.Check("followups: Scrap mode is in the panel", CraftingPanel.Modes.Contains(CraftMode.Scrap) && panel.ModeButtonVisible(CraftMode.Scrap));
        panel.ClickBag(key);
        SelfTest.Check("followups: scrap needs only the item", panel.Blocker() == null, panel.Blocker() ?? "");
        var result = panel.DoCraft();
        var mat = panel.LastScrap;
        SelfTest.Check("followups: scrap succeeded", result is { Ok: true } && mat != null, result?.Error.ToString() ?? "blocked");
        SelfTest.Check("followups: scrapped item is gone", Pc.Inventory.Slots.All(kv => !(kv.Value.BaseId == item.BaseId && kv.Value.Level == item.Level && kv.Value.Stats.SequenceEqual(item.Stats))));
        var inBag = mat == null ? null : Pc.Inventory.Slots.Values.FirstOrDefault(v => v.BaseId == mat.BaseId);
        SelfTest.Check("followups: materials are in the bag in the rules' amount", inBag != null && mat != null && inBag.Amount == Core.Rules.Crafting.ScrapYield(item.Rarity, item.Level),
            $"{inBag?.Amount} of {mat?.BaseId}, expected {Core.Rules.Crafting.ScrapYield(item.Rarity, item.Level)}");
        SelfTest.Check("followups: scrap shows what was got", panel.BeforeAfter.Length > 0, panel.BeforeAfter);
        panel.SetMode(CraftMode.Reroll);
    }

    private static void Previews()
    {
        var panel = CraftingFeature.Panel;
        panel.Open();
        Pc.Inventory.Restore(Array.Empty<KeyValuePair<string, ItemInstance>>());
        var item = TestKit.Give(null, 4, 40);
        string key = Inventory.Bag(0);
        panel.Clear();
        panel.ClickBag(key);
        var texts = new Dictionary<CraftMode, string?>();
        foreach (var mode in CraftingPanel.Modes)
        {
            panel.SetMode(mode);
            if (panel.Target == null) panel.ClickBag(key);
            if (mode is CraftMode.Polish or CraftMode.IndividualReroll) panel.PickStat(0);
            texts[mode] = panel.PreviewText();
            SelfTest.Check($"followups: preview text for {mode}", !string.IsNullOrWhiteSpace(texts[mode]), texts[mode] ?? "null");
        }
        SelfTest.Check("followups: Empower preview names the player level", texts[CraftMode.Empower]!.Contains(Pc.Progress.Level.ToString()), texts[CraftMode.Empower]);
        SelfTest.Check("followups: Reroll preview says the result is random", texts[CraftMode.Reroll]!.Contains("random"), texts[CraftMode.Reroll]);

        // Before/after after a real craft.
        panel.SetMode(CraftMode.Polish);
        if (panel.Target == null) panel.ClickBag(key);
        var current = Pc.Inventory.Get(key)!;
        for (int i = 0; i < current.Stats.Count && panel.BeforeAfter.Length == 0; i++)
        {
            panel.Clear();
            panel.ClickBag(key);
            var ingredient = TestKit.Give(null, (int)current.Rarity, current.Level); // ingredients must be at least the target's rarity
            // The bag may hold earlier materials: click the slot the ingredient actually went to.
            var slot = Enumerable.Range(0, LootRoller.BagSlots).Select(Inventory.Bag).FirstOrDefault(k => ReferenceEquals(Pc.Inventory.Get(k), ingredient));
            if (slot != null) panel.ClickBag(slot);
            panel.PickStat(i);
            panel.DoCraft();
        }
        SelfTest.Check("followups: before/after shown after a craft", panel.BeforeAfter.Contains("->"), panel.BeforeAfter);
        panel.Close();
    }

    // ---------------------------------------------------------------- COS-49

    private static void DodgePerks()
    {
        SetPerks(PerkEffectIds.ImprovedDodges, PerkEffectIds.DeployableShield);
        FollowupsFeature.Shield.Reset();
        LocalPlayer.Vitals.Health.SetCurrentValue(MaxHealth * 0.5f);
        float before = Health;
        Pc.Stats.SetSource("test.alwaysdodge", new[] { new StatModifier(StatIds.GetHitChance, EffectOp.Mult, 0) });
        LocalPlayer.Stats.Hit(5f);
        Pc.Stats.RemoveSource("test.alwaysdodge");
        SelfTest.Check("followups: Improved Dodges heals 5% of max health on a dodge", Health - before >= MaxHealth * 0.045f, $"{before:0} -> {Health:0} of {MaxHealth:0}");
        double shield = FollowupsFeature.Shield.Amount;
        SelfTest.Check("followups: Deployable Shield grants 15% of max health", Math.Abs(shield - MaxHealth * PerkTuning.ShieldFraction) < 1, $"{shield:0.#} of {MaxHealth:0}");

        FullHealth();
        float hp = Health;
        LocalPlayer.Stats.Hit(3f);
        SelfTest.Check("followups: the shield soaks the next hit", Health >= hp - 0.01f && FollowupsFeature.Shield.Amount < shield, $"health {hp:0} -> {Health:0}, shield {FollowupsFeature.Shield.Amount:0.#}");

        SetPerks(PerkEffectIds.DeployableShield, PerkEffectIds.DeployableShield2);
        FollowupsFeature.Shield.Reset();
        Pc.Stats.SetSource("test.alwaysdodge", new[] { new StatModifier(StatIds.GetHitChance, EffectOp.Mult, 0) });
        LocalPlayer.Stats.Hit(5f);
        Pc.Stats.RemoveSource("test.alwaysdodge");
        double s2 = FollowupsFeature.Shield.Amount;
        SelfTest.Check("followups: Bulwark Reflex strengthens the shield to 25%", Math.Abs(s2 - MaxHealth * 0.25) < 1, $"{s2:0.#} of {MaxHealth:0}");
        FollowupsFeature.Shield.Reset();
        SetPerks();
    }

    private static void Adrenaline()
    {
        SetPerks(PerkEffectIds.AdrenalineRush);
        FollowupsFeature.Rush.Reset();
        FullHealth();
        double speed = Pc.Stats.Get(StatIds.MovementSpeed);
        LocalPlayer.Stats.Hit(2f);
        SelfTest.Check("followups: no rush at full health", !FollowupsFeature.Rush.IsActive);
        LocalPlayer.Vitals.Health.SetCurrentValue(MaxHealth * 0.2f);
        LocalPlayer.Stats.Hit(1f);
        SelfTest.Check("followups: Adrenaline Rush starts at low health", FollowupsFeature.Rush.IsActive);
        double fast = Pc.Stats.Get(StatIds.MovementSpeed);
        SelfTest.Check("followups: rush raises movement speed by 25%", Math.Abs(fast / speed - 1.25) < 0.01, $"{speed:0.00} -> {fast:0.00}");
        FollowupsFeature.Frame(7f);
        SelfTest.Check("followups: the rush ends", !FollowupsFeature.Rush.IsActive && Math.Abs(Pc.Stats.Get(StatIds.MovementSpeed) - speed) < 0.001);
        SetPerks(PerkEffectIds.AdrenalineRush, PerkEffectIds.AdrenalineRush2);
        FollowupsFeature.Rush.Reset();
        LocalPlayer.Vitals.Health.SetCurrentValue(MaxHealth * 0.2f);
        LocalPlayer.Stats.Hit(1f);
        SelfTest.Check("followups: Adrenaline Rush II makes it stronger", Math.Abs(Pc.Stats.Get(StatIds.MovementSpeed) / speed - 1.5) < 0.01, $"{Pc.Stats.Get(StatIds.MovementSpeed) / speed:0.00}");
        FollowupsFeature.Rush.Reset();
        FollowupsFeature.SyncSources();
        SetPerks();
        FullHealth();
    }

    private static void Capacity()
    {
        var all = UtilityPerks.All.Where(e => e.Kind == UtilityKind.Capacity).ToList();
        var missing = all.Select(e => e.ItemKeyword).Distinct().Where(k => !FollowupsFeature.ItemsFor(k).Any()).ToList();
        SelfTest.Check("followups: every capacity keyword matches a game item", missing.Count == 0, string.Join(", ", missing));
        var missingDaily = UtilityPerks.All.Where(e => e.Kind == UtilityKind.DailyGrant).Select(e => e.ItemKeyword).Where(k => !FollowupsFeature.ItemsFor(k).Any()).ToList();
        SelfTest.Check("followups: every daily-grant keyword matches a game item", missingDaily.Count == 0, string.Join(", ", missingDaily));

        var medKeyword = "Pills";
        var item = FollowupsFeature.ItemsFor(medKeyword).FirstOrDefault();
        if (item == null) return;
        int baseMax = item.MaxAmount;
        SetPerks("perk.more_meds", "perk.more_meds");
        FollowupsFeature.ApplyCapacities(force: true);
        SelfTest.Check("followups: More Meds raises the carry limit by 20 per stack", item.MaxAmount == baseMax + 40, $"{baseMax} -> {item.MaxAmount}");
        SetPerks();
        FollowupsFeature.ApplyCapacities(force: true);
        SelfTest.Check("followups: the limit goes back without the perk", item.MaxAmount == baseMax, $"{item.MaxAmount} vs {baseMax}");
    }

    private static void Daily()
    {
        var item = FollowupsFeature.ItemsFor("Pills").FirstOrDefault();
        if (item == null) return;
        var inv = LocalPlayer.Inventory;
        SetPerks("perk.meds");
        int before = inv.AmountOf(item.Id, false, false);
        var given = FollowupsFeature.GrantDaily();
        int after = inv.AmountOf(item.Id, false, false);
        SelfTest.Check("followups: the Meds perk gives 3 meds on a new day", after - before == 3, $"{before} -> {after}, given [{string.Join(",", given.Select(kv => kv.Key + kv.Value))}]");
        if (after > before) inv.RemoveItem(item.Id, after - before, false, false, false, null, false);
        SetPerks();
    }

    // ---------------------------------------------------------------- COS-42 unique flags

    private static void UniqueFlags()
    {
        SetPerks();
        FollowupsFeature.Unique.Reset();
        var plain = new OutgoingHit(10, DamageKind.Physical, false, 0);

        Flags(UniqueFlagIds.KingQruie);
        FollowupsFeature.Unique.OnPhysicalDamageTaken(40);
        SelfTest.Check("followups: King Qruie's sword adds the last physical damage taken to melee", FollowupsFeature.OnLocalHit(plain, WeaponClass.Melee).Amount == 50 && FollowupsFeature.OnLocalHit(plain, WeaponClass.Ranged).Amount == 10);

        Flags(UniqueFlagIds.DeathPact);
        LocalPlayer.Vitals.Health.SetCurrentValue(MaxHealth * 0.5f);
        float before = Health;
        var pact = FollowupsFeature.OnLocalHit(plain, WeaponClass.Melee);
        SelfTest.Check("followups: Death Pact: +5% damage per 1% missing health", Math.Abs(pact.Amount - 35) < 0.01, pact.Amount.ToString("0.##"));
        SelfTest.Check("followups: Death Pact: the attack costs 7% of max health", Math.Abs((before - Health) - MaxHealth * 0.07f) < 0.5f, $"{before:0.#} -> {Health:0.#}");
        LocalPlayer.Vitals.Health.SetCurrentValue(2f);
        FollowupsFeature.OnLocalHit(plain, WeaponClass.Melee);
        SelfTest.Check("followups: Death Pact cannot kill", Health >= 1f - 0.001f, Health.ToString("0.##"));
        FullHealth();

        Flags(UniqueFlagIds.InfinityLoop);
        Pc.Spells.Equip(0, "spell.blink");
        double p0 = Pc.Spells.CooldownProgress(0);
        FollowupsFeature.OnLocalHit(plain, WeaponClass.Melee);
        SelfTest.Check("followups: Infinity Loop trims a spell cooldown on a hit", Pc.Spells.CooldownProgress(0) > p0, $"{p0:0.000} -> {Pc.Spells.CooldownProgress(0):0.000}");
        Pc.Spells.Equip(0, null);

        Flags(UniqueFlagIds.HexedPants);
        FollowupsFeature.Unique.Reset();
        double dmg = Pc.Stats.Get(StatIds.AllDamage);
        FollowupsFeature.SyncSources();
        SelfTest.Check("followups: Hexed Pants: +40% damage while moving", Math.Abs(Pc.Stats.Get(StatIds.AllDamage) / dmg - 1.4) < 0.01, $"{dmg:0.00} -> {Pc.Stats.Get(StatIds.AllDamage):0.00}");
        FullHealth();
        float h0 = Health;
        double drain = FollowupsFeature.Unique.Tick(3, false, Pc.Stats);
        SelfTest.Check("followups: Hexed Pants: standing still drains health", drain > 0 && FollowupsFeature.Unique.Modifiers(Pc.Stats).All(m => m.Stat != StatIds.AllDamage), $"drain {drain:0.####}");

        Flags(UniqueFlagIds.WindArmor);
        FollowupsFeature.Unique.Reset();
        LocalPlayer.Vitals.Health.SetCurrentValue(MaxHealth * 0.5f);
        float a0 = Health;
        double armor = Pc.Stats.Get(StatIds.Armor);
        Pc.Stats.SetSource("test.alwaysdodge", new[] { new StatModifier(StatIds.GetHitChance, EffectOp.Mult, 0) });
        LocalPlayer.Stats.Hit(4f);
        Pc.Stats.RemoveSource("test.alwaysdodge");
        SelfTest.Check("followups: Wind Armor: +2000 armor and 5% heal after a dodge", Pc.Stats.Get(StatIds.Armor) - armor >= 1999 && Health - a0 >= MaxHealth * 0.045f, $"armor +{Pc.Stats.Get(StatIds.Armor) - armor:0}, health {a0:0} -> {Health:0}");
        FollowupsFeature.Frame(6f);
        SelfTest.Check("followups: Wind Armor ends", Math.Abs(Pc.Stats.Get(StatIds.Armor) - armor) < 1);

        SpellItems();
    }

    /// <summary>The unique items that drive spells (they waited on the spells, COS-26).</summary>
    private static void SpellItems()
    {
        Flags(UniqueFlagIds.Greed);
        int greed = Spells.SpellEffects.GreedCasts;
        Spells.SpellEffects.ItemTick(0.6f);
        Spells.SpellEffects.ItemTick(0.6f);
        SelfTest.Check("followups: Greed casts Wide Reach once a second", Spells.SpellEffects.GreedCasts == greed + 1, $"{Spells.SpellEffects.GreedCasts - greed}");

        Flags(UniqueFlagIds.WitheredCrown);
        int arrows = Spells.SpellEffects.BloodArrowsFired;
        Pc.Spells.Equip(0, "spell.blood_infused_arrow");
        Pc.Spells.Tick(1e6, Pc.Stats);
        LocalPlayer.Vitals.Stamina.SetCurrentValue(LocalPlayer.Vitals.Stamina.GetMax());
        FullHealth();
        var outcome = Spells.SpellCaster.Cast(0);
        SelfTest.Check("followups: Withered Crown: one Blood Infused Arrow cast fires 5 arrows", Spells.SpellEffects.BloodArrowsFired - arrows == 1 + UniqueTuning.CrownExtraArrows,
            $"{outcome}, {Spells.SpellEffects.BloodArrowsFired - arrows} arrows");
        Pc.Spells.Equip(0, null);
        FullHealth();

        Flags(UniqueFlagIds.Sora);
        double withSora = Pc.Spells.CostOf("spell.multishot", Pc.Stats).Energy;
        Flags();
        double without = Pc.Spells.CostOf("spell.multishot", Pc.Stats).Energy;
        SelfTest.Check("followups: Grip of Sora: Multishot costs 20% less", without > 0 && Math.Abs(withSora / without - UniqueTuning.SoraCostScale) < 0.001, $"{without:0.##} -> {withSora:0.##}");

        Flags(UniqueFlagIds.Crossfire);
        var target = TestKit.Spawn(distance: 8, rules: new WorldRules { Difficulty = Difficulties.Easy, AllowElites = false });
        var rt = TestKit.Profile(target);
        int fired = Spells.SpellEffects.CrossfireArrows;
        if (rt != null)
        {
            Spells.SpellEffects.OnWeaponHit(rt, WeaponClass.Ranged);
            Spells.SpellEffects.OnWeaponHit(rt, WeaponClass.Ranged);
            Spells.SpellEffects.OnWeaponHit(rt, WeaponClass.Melee);
        }
        SelfTest.Check("followups: Crossfire: a projectile hit fires one free Magic Arrow, then waits", rt != null && Spells.SpellEffects.CrossfireArrows - fired == 1,
            $"{Spells.SpellEffects.CrossfireArrows - fired} arrows");
        if (target != null) TestKit.Kill(target);
        Flags();
    }

    private static void Cleanup()
    {
        Pc.Stats.RemoveSource("test.flags");
        FollowupsFeature.Unique.Reset();
        FollowupsFeature.Shield.Reset();
        FollowupsFeature.Rush.Reset();
        FollowupsFeature.SyncSources();
        FullHealth();
        LootFeature.ClearAll();
    }
}
