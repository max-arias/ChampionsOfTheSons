using ChampionsOfTheSons.Core;
using ChampionsOfTheSons.Core.Content;
using ChampionsOfTheSons.Core.Rules;
using ChampionsOfTheSons.Mod.Abilities;
using ChampionsOfTheSons.Mod.Bridge;
using ChampionsOfTheSons.Mod.Perks;
using ChampionsOfTheSons.Mod.Spells;
using Sons.Ai.Vail;
using Sons.Items.Core;
using TheForest.Utils;
using UnityEngine;

namespace ChampionsOfTheSons.Mod.PerkHooks;

/// <summary>COS-70/85/86/87/88 in-game checks: weapon kinds (spike), ammo refund, per-weapon multiplier, stamina on hit,
/// Infinity, Black Hole pull immunity, Warmth, unique spell items, Wide Reach pickup, and the perk panel after the path fix.</summary>
internal static class PerkHooksSelfTest
{
    private static PlayerCharacter Pc => PlayerState.Character;
    private const string Src = "test.perkhooks";

    private static void Stats(params StatModifier[] mods) => Pc.Stats.SetSource(Src, mods);

    /// <summary>No crits, so damage ratios are exact.</summary>
    private static StatModifier NoCrit => new(StatIds.CritChance, EffectOp.Add, -10);

    private static readonly Dictionary<RangedKind, List<(int Id, string Name)>> Kinds = new();
    private static VailActor? _target;

    public static void Register()
    {
        SelfTest.Add(1f, "perk hooks: weapon kinds", WeaponKinds);
        SelfTest.Add(2f, "perk hooks: ammo refund", AmmoRefund);
        SelfTest.Add(3f, "perk hooks: spawn target", () =>
        {
            _target = TestKit.Spawn(VailActorTypeId.Carl, 5);
            SelfTest.Check("perk hooks: target spawned", _target != null && EnemyRegistry.Get(_target) != null);
        });
        SelfTest.Add(4f, "perk hooks: per-weapon multiplier", WeaponMultiplier);
        SelfTest.Add(5f, "perk hooks: stamina on hit", StaminaOnHit);
        SelfTest.Add(6f, "perk hooks: infinity", Infinity);
        SelfTest.Add(7f, "perk hooks: blink items", BlinkItems);
        SelfTest.Add(8f, "perk hooks: frenzy and war cry items", BuffItems);
        SelfTest.Add(9f, "perk hooks: flashlight and landing", Utility);
        SelfTest.Add(10f, "perk hooks: black hole pull immunity", PullImmunity);
        SelfTest.Add(22f, "perk hooks: warmth", Warmth);
        SelfTest.Add(28f, "perk hooks: wide reach", WideReach);
        SelfTest.Add(30f, "perk hooks: perk panel tabs", PanelShots);
        SelfTest.Add(31f, "perk hooks: defense shot", () => TestKit.Screenshot("perkhooks-defense"));
        SelfTest.Add(31.5f, "perk hooks: magic tab", () => PerkPanel.Instance.ShowTab(PerkCategory.MagicOffense.ToString()));
        SelfTest.Add(32.5f, "perk hooks: magic shot", () => TestKit.Screenshot("perkhooks-magic"));
        SelfTest.Add(33f, "perk hooks: cleanup", () =>
        {
            PerkPanel.Instance.Close();
            Pc.Stats.RemoveSource(Src);
            if (_target != null) TestKit.Kill(_target);
            ActiveAbilitySystem.Reset();
            PlayerEffects.Reset();
            SpellWorld.Reset();
        });
    }

    // ---------------------------------------------------------------- COS-86

    private static void WeaponKinds()
    {
        Kinds.Clear();
        var list = ItemDatabaseManager.Items;
        for (int i = 0; i < list.Count; i++)
        {
            var d = list[i];
            if (d == null) continue;
            var k = RangedRules.KindOf(d.Name);
            if (k == RangedKind.Other) continue;
            if (!Kinds.TryGetValue(k, out var l)) Kinds[k] = l = new();
            l.Add((d.Id, d.Name));
        }
        foreach (var (k, l) in Kinds) SelfTest.Log($"perk hooks: {k}: {string.Join(", ", l.Select(x => $"{x.Id}:{x.Name}"))}");
        foreach (var k in new[] { RangedKind.Bow, RangedKind.Crossbow, RangedKind.Bullet, RangedKind.ThrownSpear })
            SelfTest.Check($"perk hooks: game has a {k} item", Kinds.ContainsKey(k));
    }

    private static int? AmmoId()
    {
        var list = ItemDatabaseManager.Items;
        for (int i = 0; i < list.Count; i++)
            if (list[i] != null && list[i].Name.IndexOf("arrow", StringComparison.OrdinalIgnoreCase) >= 0) return list[i].Id;
        return null;
    }

    private static void AmmoRefund()
    {
        var inv = LocalPlayer.Inventory;
        if (AmmoId() is not int ammo || inv == null) { SelfTest.Check("perk hooks: an arrow item exists", false); return; }
        Stats(new StatModifier(RangedStatIds.NoConsumeChance, EffectOp.Add, 1));
        int before = inv.AmountOf(ammo, false, false);
        bool kept = ShotTracker.TryRefund(ammo);
        int after = inv.AmountOf(ammo, false, false);
        SelfTest.Check("perk hooks: no-consume chance gives the round back", kept && after == before + 1, $"{before} -> {after}");
        Stats();
        before = after;
        kept = ShotTracker.TryRefund(ammo);
        SelfTest.Check("perk hooks: without the perk nothing comes back", !kept && inv.AmountOf(ammo, false, false) == before);
    }

    private static double HitAs(RangedKind kind, double amount)
    {
        var id = Kinds.TryGetValue(kind, out var l) ? l[0].Id : 0;
        ShotTracker.Record(id);
        EnemyRegistry.HealthOf(_target!)?.SetCurrentValue(1e9f);
        TestKit.Hit(_target!, (float)amount, ImpactType.Projectile);
        return EnemyPatches.LastHit?.Before ?? 0;
    }

    private static void WeaponMultiplier()
    {
        if (_target == null || !Kinds.ContainsKey(RangedKind.Crossbow)) { SelfTest.Check("perk hooks: crossbow test set up", false); return; }
        Stats(NoCrit);
        double plain = HitAs(RangedKind.Crossbow, 50);
        Stats(NoCrit, new StatModifier(RangedStatIds.CrossbowDamage, EffectOp.Mult, 2));
        double perk = HitAs(RangedKind.Crossbow, 50);
        double bow = HitAs(RangedKind.Bow, 50);
        SelfTest.Check("perk hooks: crossbow perk doubles crossbow hits", plain > 0 && Math.Abs(perk / plain - 2) < 0.01, $"{plain:0.#} -> {perk:0.#}");
        SelfTest.Check("perk hooks: crossbow perk leaves bow hits alone", Math.Abs(bow - plain) < 0.01, $"{bow:0.#}");
        Stats(NoCrit, new StatModifier(RangedStatIds.DamagePerDistance, EffectOp.Add, 0.02));
        double far = HitAs(RangedKind.Bow, 50);
        double metres = Vector3.Distance(LocalPlayer.Transform.position, _target.transform.position);
        SelfTest.Check("perk hooks: damage per metre", Math.Abs(far / plain - (1 + metres * 0.02)) < 0.02, $"{metres:0.0} m: {plain:0.#} -> {far:0.#}");
        Stats();
    }

    private static void StaminaOnHit()
    {
        if (_target == null) return;
        var stamina = LocalPlayer.Vitals.Stamina;
        Stats(new StatModifier(RangedStatIds.StaminaOnHit, EffectOp.Add, 30));
        stamina.SetCurrentValue(stamina.GetMax() * 0.2f);
        float before = stamina._currentValue;
        EnemyRegistry.HealthOf(_target)?.SetCurrentValue(1e9f);
        TestKit.Hit(_target, 10, ImpactType.Melee);
        SelfTest.Check("perk hooks: a melee hit restores stamina", stamina._currentValue > before + 1, $"{before:0.#} -> {stamina._currentValue:0.#}");
        Stats();
    }

    // ---------------------------------------------------------------- COS-87

    private static void Infinity()
    {
        var spells = Pc.Spells;
        var saved = spells.Loadout.ToArray();
        spells.Equip(0, "spell.bash");
        spells.Equip(1, "spell.war_cry");
        spells.Tick(1e6, Pc.Stats); // both ready
        spells.TryCast(1, double.MaxValue, Pc.Stats, out _); // War Cry cooling down
        spells.Tick(5, Pc.Stats);
        double before = spells.CooldownProgress(1);
        Stats(new StatModifier(PerkStatIds.Infinity, EffectOp.Enable, 1));
        bool cast = spells.TryCast(0, double.MaxValue, Pc.Stats, out _) == CastResult.Ok;
        double after = spells.CooldownProgress(1);
        SelfTest.Check("perk hooks: Infinity shortens other cooldowns on cast", cast && after > before && Math.Abs((1 - after) - (1 - before) * PerkRules.InfinityFactor) < 1e-6,
            $"{before:0.000} -> {after:0.000}");
        Stats();
        spells.Restore(saved);
    }

    private static void BlinkItems()
    {
        if (_target == null) return;
        Stats(new StatModifier(PerkStatIds.BlinkDamage, EffectOp.Add, 60), new StatModifier(PerkStatIds.BlinkExplosion, EffectOp.Enable, 1));
        var from = LocalPlayer.Transform.position;
        var to = _target.transform.position + LocalPlayer.Transform.forward * 3;
        SpellCaster.BlinkItems(from, to);
        SelfTest.Check("perk hooks: Blink strikes enemies passed through and explodes at the exit", SpellCaster.BlinkStruck >= 2, SpellCaster.BlinkStruck.ToString());
        Stats();
    }

    private static void BuffItems()
    {
        Stats(new StatModifier(PerkStatIds.FrenzyCrit, EffectOp.Add, 0.05));
        var mods = SpellTuning.Frenzy(Pc.Stats, 0);
        SelfTest.Check("perk hooks: Frenzy item adds crit while active", mods.Any(m => m.Stat == StatIds.CritChance && Math.Abs(m.Value - 0.05) < 1e-9));
        Stats();
    }

    private static void Utility()
    {
        Stats(new StatModifier(PerkStatIds.FlashlightIntensity, EffectOp.Add, 1), new StatModifier(PerkStatIds.FlashlightBattery, EffectOp.Add, 1));
        var (intensity, drain) = PerkRules.Flashlight(Pc.Stats);
        SelfTest.Check("perk hooks: Light the Way doubles intensity and halves drain", Math.Abs(intensity - 2) < 1e-6 && Math.Abs(drain - 0.5) < 1e-6);
        SelfTest.Log($"perk hooks: flashlight controllers seen {PerkHooksFeature.FlashlightsSeen}");
        Stats(new StatModifier(PerkStatIds.BunnyHop, EffectOp.Enable, 1));
        SpellWorld.Buffs.Remove("perk.momentum");
        double speed = Pc.Stats.Get(StatIds.MovementSpeed);
        PerkHooksFeature.OnLanded();
        double boosted = Pc.Stats.Get(StatIds.MovementSpeed);
        SelfTest.Check("perk hooks: Momentum Transfer speeds up on landing", Math.Abs(boosted / speed - PerkRules.HopBoost) < 1e-6, $"{speed:0.00} -> {boosted:0.00}");
        SpellWorld.Buffs.Remove("perk.momentum");
        Stats();
    }

    private const float Dt = 0.25f;

    private static bool Advance(Func<bool> done, float max)
    {
        for (float t = 0; t < max; t += Dt)
        {
            LocalPlayer.Vitals.SetFullHealth();
            EnemyAbilityFeature.Step(Dt);
            if (done()) return true;
        }
        return false;
    }

    private static EnemyRuntime? AbilityEnemy(EnemyAbility only, float distance)
    {
        ActiveAbilitySystem.Reset();
        foreach (var other in EnemyRegistry.Snapshot()) other.TestActive = false;
        var disabled = ((EnemyAbility[])Enum.GetValues(typeof(EnemyAbility))).Where(a => a != only).ToArray();
        var actor = TestKit.Spawn(VailActorTypeId.Carl, distance, 0, new WorldRules { Difficulty = Difficulties.Hell, AllowElites = true, DisabledAbilities = disabled });
        var enemy = EnemyRegistry.Get(actor);
        if (enemy == null || !enemy.Profile.Has(only)) return null;
        enemy.TestActive = true;
        return enemy;
    }

    private static void PullImmunity()
    {
        Pc.Stats.SetSource("test.tank", new[] { new StatModifier(StatIds.MaxHealth, EffectOp.Add, 1e8) });
        var enemy = AbilityEnemy(EnemyAbility.BlackHole, 14);
        if (enemy == null) { SelfTest.Check("perk hooks: Black Hole enemy", false); return; }
        Stats(new StatModifier(PerkStatIds.BlackHolePullImmune, EffectOp.Enable, 1));
        bool cast = Advance(() => AbilityDamage.FiredCount(enemy, EnemyAbility.BlackHole) > 0, 20);
        bool pulled = Advance(() => AbilityForces.PullOf(enemy).magnitude > 0.01f, 4);
        SelfTest.Check("perk hooks: Dense Matter: an enemy Black Hole does not pull", cast && !pulled, $"cast {cast}, pull {AbilityForces.PullOf(enemy)}");
        Stats();
        bool pulledAfter = Advance(() => AbilityForces.PullOf(enemy).magnitude > 1, 6);
        SelfTest.Check("perk hooks: without the perk it pulls", pulledAfter);
        enemy.TestActive = false;
        ActiveAbilitySystem.Reset();
    }

    private static void Warmth()
    {
        var enemy = AbilityEnemy(EnemyAbility.Blizzard, 6);
        if (enemy == null) { SelfTest.Check("perk hooks: Blizzard enemy", false); return; }
        Stats(new StatModifier(PerkStatIds.BlizzardSlowReduced, EffectOp.Enable, 1));
        bool slowed = Advance(() => AbilityForces.SlowsOf(enemy) > 0, 6);
        SelfTest.Check("perk hooks: Warmth: Blizzard slows to x0.8 only", slowed && Math.Abs(AbilityForces.SpeedMultiplier - PerkRules.WarmBlizzardSlow) < 0.01,
            AbilityForces.SpeedMultiplier.ToString("0.00"));
        Stats();
        enemy.TestActive = false;
        ActiveAbilitySystem.Reset();
        Pc.Stats.RemoveSource("test.tank");
    }

    // ---------------------------------------------------------------- COS-85

    private static void WideReach()
    {
        var (found, missing) = WideReachPickup.CheckList();
        SelfTest.Log($"perk hooks: Wide Reach game pickups found [{string.Join(", ", found)}], not game items [{string.Join(", ", missing)}]");
        SelfTest.Check("perk hooks: Wide Reach names real game items", found.Count >= 8, $"{found.Count} of {WideReachRules.GamePickups.Count}");

        var bag = Pc.Inventory.Slots.Where(kv => kv.Key.StartsWith("bag.", StringComparison.Ordinal)).ToList();
        var equipped = Pc.Inventory.Equipped.ToList();
        Pc.Inventory.Restore(Array.Empty<KeyValuePair<string, ItemInstance>>());
        Loot.LootFeature.ClearAll();
        var t = LocalPlayer.Transform;
        var item = new LootRoller(ModContent.Db).RollItem(ModContent.Db.Items.Values.First(d => d.Rarity == 1 && LootRoller.SlotsFor(d.Type).Count > 0), 10, new Core.Rng(7));
        var near = Loot.LootFeature.SpawnAt(item, t.position + t.forward * 5f);
        var far = Loot.LootFeature.SpawnAt(item with { }, t.position + t.forward * 20f);
        WideReachPickup.Cast();
        bool inBag = Pc.Inventory.Slots.Values.Any(i => i.BaseId == item.BaseId);
        SelfTest.Check("perk hooks: Wide Reach puts a drop 5 m away in the bag and leaves one 20 m away", inBag && Loot.LootFeature.Ground.Get(near.Id) == null && Loot.LootFeature.Ground.Get(far.Id) != null,
            $"drops {WideReachPickup.LastDrops}, game pickups {WideReachPickup.LastGamePickups}");

        Stats(new StatModifier(RangeRules.RangeMult, EffectOp.Mult, 3));
        WideReachPickup.Cast();
        SelfTest.Check("perk hooks: the range factor widens Wide Reach", Loot.LootFeature.Ground.Get(far.Id) == null, $"radius {WideReachPickup.Radius:0.#} m");
        Stats();
        Loot.LootFeature.ClearAll();
        Pc.Inventory.Restore(bag.Concat(equipped));
    }

    // ---------------------------------------------------------------- COS-70

    private static void PanelShots()
    {
        var panel = PerkPanel.Instance;
        panel.Open();
        panel.ShowTab(PerkCategory.Defense.ToString());
        SelfTest.Check("perk hooks: panel shows the Defense tab", panel.IsOpen && panel.CurrentTab == PerkCategory.Defense.ToString());
    }
}
