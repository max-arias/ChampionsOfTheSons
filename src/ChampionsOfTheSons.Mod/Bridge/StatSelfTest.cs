using ChampionsOfTheSons.Core;
using ChampionsOfTheSons.Core.Content;
using ChampionsOfTheSons.Core.Rules;
using Sons.Ai.Vail;
using Sons.Weapon;
using TheForest.Utils;
using UnityEngine;

namespace ChampionsOfTheSons.Mod.Bridge;

/// <summary>
/// COS-16 / COS-17 in-game checks: every item stat with a data effect is classified (core, bridge or dropped with a reason) and changes the
/// stat sheet; each bridge stat is then equipped and its effect on the game (or the hook result) is measured; each unique item
/// triggers; set bonuses reach the sheet.
/// </summary>
internal static class StatSelfTest
{
    private const float T0 = 50;
    private static readonly VailActor?[] E = new VailActor?[10];
    private static float _a, _b;

    private static PlayerCharacter Pc => PlayerState.Character;
    private static ContentDb Db => ModContent.Db;
    private static string RingId => Db.Items.Values.First(i => i.Type == "ring" && !i.Manual).Id;

    private static void Clean()
    {
        PlayerState.FromSave(new Core.Saving.PlayerSave());
        Pc.Stats.SetSource("test.nocrit", new[] { new StatModifier(StatIds.CritChance, EffectOp.Add, -1) });
    }

    private static ItemInstance Item(string baseId, params (string Stat, float Value)[] stats) =>
        new() { BaseId = baseId, Level = 10, Rarity = Rarity.Rare, Stats = stats.Select(s => new RolledStat(s.Stat, s.Value)).ToArray() };

    private static void Equip(ItemInstance item)
    {
        Pc.Inventory.AddToBag(item);
        var slot = Pc.Inventory.Slots.First(kv => kv.Key.StartsWith("bag.", StringComparison.Ordinal) && kv.Value == item).Key;
        if (!Pc.Inventory.Equip(slot, Db)) SelfTest.Check("equip " + item.BaseId, false);
    }

    private static void EquipStats(params (string Stat, float Value)[] stats)
    {
        Pc.Inventory.Restore(Array.Empty<KeyValuePair<string, ItemInstance>>());
        Equip(Item(RingId, stats));
    }

    private static void Unequip() => Pc.Inventory.Restore(Array.Empty<KeyValuePair<string, ItemInstance>>());

    private static VailActor? Spawn(int i) => E[i] = TestKit.Spawn(distance: 7 + i / 5 * 3, sideways: (i % 5 - 2) * 3.5f,
        rules: new WorldRules { PartyLevelOverride = 1, AllowElites = false });

    private static double HitDamage(VailActor actor, float amount, ImpactType type = ImpactType.Melee, DamageNode? node = null)
    {
        actor.ReceivedDamage(node, type, 0, null, amount, true, false, false, Vector3.forward);
        return EnemyPatches.LastHit?.After ?? -1;
    }

    private static float Health => LocalPlayer.Vitals.Health._currentValue;
    private static float MaxHealth => LocalPlayer.Vitals.Health.GetMax();

    public static void Register()
    {
        SelfTest.Add(T0, "stats: every item stat is classified and changes the sheet", Coverage);
        SelfTest.Add(T0 + 0.5f, "stats: unique and set effect flags", FlagsAndSets);
        SelfTest.Add(T0 + 1, "stats: spawn targets", () =>
        {
            Clean();
            LocalPlayer.Vitals.SetFullHealth();
            for (int i = 0; i < E.Length; i++) Spawn(i);
            SelfTest.Check("spawned test targets", E.All(a => a != null));
        });

        SelfTest.Add(T0 + 5, "stats: life and energy on hit", () =>
        {
            AffixSelfTest.Activate(E);
            EquipStats(("stat.lifeonhit", 15f), ("stat.energyonhit", 15f));
            LocalPlayer.Vitals.Health.SetCurrentValue(MaxHealth * 0.4f);
            LocalPlayer.Vitals.SetStamina(5f);
            _a = Health;
            _b = LocalPlayer.Vitals.GetStamina();
            HitDamage(E[0]!, 10f);
            SelfTest.Check("life on hit heals", Health >= _a + 14f, $"{_a:0.0} -> {Health:0.0}");
            SelfTest.Check("energy on hit refills stamina", LocalPlayer.Vitals.GetStamina() >= _b + 10f, $"{_b:0.0} -> {LocalPlayer.Vitals.GetStamina():0.0}");
        });

        SelfTest.Add(T0 + 5.5f, "stats: slow, bleed and weaken on hit", () =>
        {
            EquipStats(("stat.chanceonhittoslow", 1f), ("stat.chanceonhittobleed", 1f), ("stat.chanceonhittoweaken", 1f));
            HitDamage(E[1]!, 10f);
            var c = EnemyRegistry.Get(E[1])!.Combat;
            SelfTest.Check("slow applied", c.HasSlow && c.SpeedMultiplier < 1);
            SelfTest.Check("bleed applied", c.DotCount > 0);
            SelfTest.Check("weaken applied", c.HasWeaken && c.DamageTakenMultiplier > 1);
        });

        SelfTest.Add(T0 + 6, "stats: stun on hit", () =>
        {
            EquipStats(("stat.stunonhit", 1f));
            Pc.Stats.SetSource("test.stun", new[] { new StatModifier(CombatStatIds.HammerStunChance, EffectOp.Mult, 4) });
            HitDamage(E[2]!, 10f);
            SelfTest.Check("hammer stun stuns", EnemyRegistry.Get(E[2])!.Combat.Stunned);
            Pc.Stats.RemoveSource("test.stun");
        });

        SelfTest.Add(T0 + 6.5f, "stats: thorns hurt the attacker", () =>
        {
            EquipStats(("stat.thorns", 40f));
            var r = EnemyRegistry.Get(E[3])!;
            _a = E[3]!.GetHealth();
            PlayerEffects.LastAttacker = (r, Time.time);
            LocalPlayer.Stats.Hit(1f);
            SelfTest.Log($"thorns diag: thorns {PlayerHitEffects.Thorns(Pc.Stats):0.0}, incoming {PlayerBridge.LastIncoming}, enemy active {r.IsActive}, attacker left {PlayerEffects.LastAttacker == null}, hit {EnemyPatches.LastHit}");
            SelfTest.Check("thorns damaged the enemy that hit us", E[3]!.GetHealth() < _a - 1f, $"{_a:0.0} -> {E[3]!.GetHealth():0.0}");
            LocalPlayer.Vitals.SetFullHealth();
        });

        SelfTest.Add(T0 + 7, "stats: headshot", () =>
        {
            Unequip();
            var nodes = E[4]!.GetComponentsInChildren<DamageNode>(true);
            var names = nodes.Select(n => n.gameObject.name).Distinct().ToList();
            SelfTest.Log($"damage nodes: {string.Join(", ", names.Take(20))}");
            var head = nodes.FirstOrDefault(n => n.gameObject.name.IndexOf("head", StringComparison.OrdinalIgnoreCase) >= 0);
            var body = nodes.FirstOrDefault(n => n.gameObject.name.IndexOf("head", StringComparison.OrdinalIgnoreCase) < 0);
            SelfTest.Check("the enemy has a head and a body damage node", head != null && body != null);
            if (head == null || body == null) return;
            double d0 = HitDamage(E[4]!, 5f, ImpactType.Projectile, body);
            double d1 = HitDamage(E[4]!, 5f, ImpactType.Projectile, head);
            SelfTest.Check("a headshot hits several times harder", d0 > 0 && d1 / d0 >= 3, $"body {d0:0.00}, head {d1:0.00}");
        });

        SelfTest.Add(T0 + 7.5f, "stats: explosion and spear damage", () =>
        {
            Unequip();
            double d0 = HitDamage(E[5]!, 5f, ImpactType.Explosion);
            EquipStats(("stat.explosiondamage", 1f));
            double d1 = HitDamage(E[5]!, 5f, ImpactType.Explosion);
            SelfTest.Check("explosion damage ×2", d0 > 0 && Math.Abs(d1 / d0 - 2) < 0.1, $"{d0:0.00} -> {d1:0.00}");
            // COS-86: the spear stat changes thrown-spear projectile hits, not explosions.
            Unequip();
            PerkHooks.ShotTracker.Record(474); // CraftedSpear
            double s0 = HitDamage(E[5]!, 5f, ImpactType.Projectile);
            EquipStats(("stat.speardamage", 0.5f));
            PerkHooks.ShotTracker.Record(474);
            double d2 = HitDamage(E[5]!, 5f, ImpactType.Projectile);
            SelfTest.Check("thrown spear damage changes a thrown spear hit", d2 > 0 && Math.Abs(d2 - s0) > 0.01 * s0, $"{s0:0.00} -> {d2:0.00}");
            double e2 = HitDamage(E[5]!, 5f, ImpactType.Explosion);
            SelfTest.Check("thrown spear damage leaves explosions alone", Math.Abs(e2 - d0) < 0.01 * d0, $"{d0:0.00} -> {e2:0.00}");
            PerkHooks.ShotTracker.Reset();
        });

        SelfTest.Add(T0 + 8, "stats: fire damage on a burning enemy", () =>
        {
            Unequip();
            E[6]!.SetBurnBodyState();
            bool burning = E[6]!.IsBodyBurning();
            SelfTest.Log($"SetBurnBodyState -> IsBodyBurning={burning}");
            if (!burning) return;
            double d0 = HitDamage(E[6]!, 5f);
            EquipStats(("stat.firedamage", 1f));
            double d1 = HitDamage(E[6]!, 5f);
            SelfTest.Check("fire damage raises hits on burning enemies", d1 > d0 * 1.5, $"{d0:0.00} -> {d1:0.00}");
        });

        SelfTest.Add(T0 + 8.5f, "stats: attack speed, projectile values, attack cost", () =>
        {
            double speed0 = StatBridge.AttackSpeed, proj0 = StatBridge.ProjectileSpeed, size0 = StatBridge.ProjectileSize, pen0 = StatBridge.ProjectilePenetration;
            EquipStats(("stat.attackspeed", 0.5f), ("stat.projectilespeed", 0.5f), ("stat.projectilesize", 0.5f), ("stat.piercechance", 0.5f), ("stat.attackcostreduction", 0.5f));
            SelfTest.Check("attack speed", StatBridge.AttackSpeed > speed0 + 0.1, $"{speed0:0.00} -> {StatBridge.AttackSpeed:0.00}");
            SelfTest.Check("projectile speed", StatBridge.ProjectileSpeed > proj0 + 0.1, $"{proj0:0.00} -> {StatBridge.ProjectileSpeed:0.00}");
            SelfTest.Check("projectile size", StatBridge.ProjectileSize > size0 + 0.1, $"{size0:0.00} -> {StatBridge.ProjectileSize:0.00}");
            SelfTest.Check("projectile pierce", StatBridge.ProjectilePenetration > pen0 + 0.1, $"{pen0:0.00} -> {StatBridge.ProjectilePenetration:0.00}");
            SelfTest.Check("attack cost changes", StatBridge.AttackCost != 1, StatBridge.AttackCost.ToString("0.00"));
        });

        SelfTest.Add(T0 + 10, "stats: weapon data follows the stats", () =>
        {
            AffixSelfTest.Advance(0.6f);
            SelfTest.Log($"weapon data seen: melee ratio {StatBridge.MeleeSwingRatio?.ToString("0.00") ?? "none held"}, bullet speed ratio {StatBridge.BulletSpeedRatio?.ToString("0.00") ?? "none held"}");
            if (StatBridge.MeleeSwingRatio is { } m)
                SelfTest.Check("the held weapon's swing speed follows attack speed", Math.Abs(m - StatBridge.AttackSpeed) < 0.02, $"{m:0.00} vs {StatBridge.AttackSpeed:0.00}");
            if (StatBridge.BulletSpeedRatio is { } b)
                SelfTest.Check("the held weapon's bullet speed follows projectile speed", Math.Abs(b - StatBridge.ProjectileSpeed) < 0.02, $"{b:0.00} vs {StatBridge.ProjectileSpeed:0.00}");
        });

        SelfTest.Add(T0 + 10.5f, "stats: stamina regeneration and maximum", () =>
        {
            Unequip();
            LocalPlayer.Vitals.SetStamina(0f);
            AffixSelfTest.Advance(1.5f);
            _a = LocalPlayer.Vitals.GetStamina();
            _b = LocalPlayer.Vitals.Stamina.GetMax();
            SelfTest.Log($"stamina regained in 1.5 s without items: {_a:0.0} (max {_b:0.0})");
            EquipStats(("stat.staminapersecond", 40f), ("stat.maximumenergy", 100f));
            LocalPlayer.Vitals.SetStamina(0f);
            AffixSelfTest.Advance(1.5f);
            float got = LocalPlayer.Vitals.GetStamina();
            SelfTest.Check("stamina per second adds regeneration", got > _a + 30f, $"{_a:0.0} -> {got:0.0} in 1.5 s");
            SelfTest.Check("maximum energy raises the stamina maximum", LocalPlayer.Vitals.Stamina.GetMax() > _b * 1.5f, $"{_b:0.0} -> {LocalPlayer.Vitals.Stamina.GetMax():0.0}");
            Unequip();
        });


        // ------------------------------------------------------------------ unique items (COS-17)

        SelfTest.Add(T0 + 15, "unique: Vampire Ring leeches", () =>
        {
            Clean();
            LocalPlayer.Vitals.SetFullHealth();
            Equip(Item(UniqueItems.VampireRing));
            LocalPlayer.Vitals.Health.SetCurrentValue(MaxHealth * 0.3f);
            _a = Health;
            double dmg = HitDamage(E[7]!, 400f);
            double expected = dmg * UniqueItems.RingLifeSteal;
            SelfTest.Check("ring returns 6% of the damage dealt", dmg > 0 && Health - _a >= expected * 0.9 && Health - _a <= expected + 3, $"dealt {dmg:0.0}, healed {Health - _a:0.00}, expected {expected:0.00}");
        });

        SelfTest.Add(T0 + 15.5f, "unique: Vampire Ring heals on kill", () =>
        {
            Unequip();
            Equip(Item(UniqueItems.VampireRing));
            LocalPlayer.Vitals.Health.SetCurrentValue(MaxHealth * 0.3f);
            _a = Health;
            TestKit.Kill(E[8]!);
            SelfTest.Check("ring heals 5% of max health on a kill", E[8]!.IsDead() && Health - _a >= MaxHealth * 0.05f * 0.95f, $"{_a:0.0} -> {Health:0.0} (max {MaxHealth:0.0})");
        });

        SelfTest.Add(T0 + 16, "unique: Blood Bag regenerates when low", () =>
        {
            Unequip();
            Equip(Item(UniqueItems.BloodBag));
            LocalPlayer.Vitals.Health.SetCurrentValue(MaxHealth * 0.2f);
            _a = Health;
            AffixSelfTest.Advance(2f);
            SelfTest.Check("regeneration of 1% max health per second while below half", Health - _a >= MaxHealth * 0.01f * 1.5f, $"{_a:0.0} -> {Health:0.0} in 2 s");
            LocalPlayer.Vitals.SetFullHealth();
        });

        SelfTest.Add(T0 + 18.5f, "unique: Undying Promise cheats death", () =>
        {
            Unequip();
            Equip(Item(UniqueItems.UndyingPromise));
            LocalPlayer.Vitals.SetFullHealth();
            LocalPlayer.Vitals.Health.SetCurrentValue(10f);
            LocalPlayer.Stats.Hit(500f);
            SelfTest.Check("lethal hit left 30% health", Health > MaxHealth * 0.25f && Health < MaxHealth * 0.4f, $"{Health:0.0} of {MaxHealth:0.0}");
            SelfTest.Check("cooldown started", PlayerEffects.Undying.CooldownLeft > 0);
            LocalPlayer.Vitals.SetFullHealth();
        });

        SelfTest.Add(T0 + 19, "unique: Untainted Heart respecs", () =>
        {
            Clean();
            var save = new Core.Saving.PlayerSave { Level = 6 };
            PlayerState.FromSave(save);
            var perk = Db.Perks.Values.First(p => p.UnlockPath.Count == 0 && p.Requires.Count == 0 && p.Cost <= 1 && p.LevelReq <= 1 && !p.Manual);
            SelfTest.Check("bought a perk", Pc.BuyPerk(perk.Id) == BuyResult.Ok);
            Pc.Inventory.AddToBag(new ItemInstance { BaseId = UniqueItems.HeartOfPurity, Amount = 2 }, 100);
            var slot = Pc.Inventory.Slots.First(kv => kv.Value.BaseId == UniqueItems.HeartOfPurity).Key;
            SelfTest.Check("used a heart", Consumables.Use(Pc, slot));
            SelfTest.Check("perks were respecced", Pc.Perks.Bought.Count == 0 && Pc.Perks.AvailablePoints(Pc.Progress) == Pc.Progress.TotalPerkPoints);
            SelfTest.Check("one heart is left", Pc.Inventory.Slots.Values.Where(i => i.BaseId == UniqueItems.HeartOfPurity).Sum(i => i.Amount) == 1);
            Clean();
            Pc.Stats.RemoveSource("test.nocrit");
        });
    }

    // ---------------------------------------------------------------------- generic checks

    private static void Coverage()
    {
        var baseline = new StatSheet(Db);
        var unclassified = new List<string>();
        var unchanged = new List<string>();
        int checkedStats = 0;
        var counts = new Dictionary<string, int>();
        foreach (var def in Db.ItemStats.Values.Where(d => d.Effects.Count > 0))
        {
            Unequip();
            double value = def.Max > 0 ? def.Max : 1;
            Equip(Item(RingId, (def.Id, (float)value)));
            foreach (var effect in def.Effects)
            {
                checkedStats++;
                var kind = StatCoverage.Classify(effect.Stat);
                if (kind == null) unclassified.Add($"{def.Id}->{effect.Stat}");
                else counts[kind] = counts.GetValueOrDefault(kind) + 1;
                if (Math.Abs(Pc.Stats.Get(effect.Stat) - baseline.Get(effect.Stat)) < 1e-9) unchanged.Add($"{def.Id}->{effect.Stat}");
            }
        }
        Unequip();
        SelfTest.Log($"item stat effects: {checkedStats} checked ({string.Join(", ", counts.Select(kv => kv.Key + " " + kv.Value))})");
        SelfTest.Check("every item stat effect is core, bridge or dropped with a reason", unclassified.Count == 0, string.Join("; ", unclassified.Take(12)));
        SelfTest.Check("every item stat effect changes the stat sheet", unchanged.Count == 0, string.Join("; ", unchanged.Take(12)));
        foreach (var (stat, why) in StatCoverage.Dropped) SelfTest.Log($"dropped: {stat} ({why})");
    }

    /// <summary>Unique-item flag effects without a handler are listed as dropped for this wave (no original behavior text to build from).</summary>
    private static void FlagsAndSets()
    {
        var flags = Db.Items.Values.SelectMany(i => i.UniqueEffects).Select(e => e.Stat).Where(s => s.StartsWith("i_", StringComparison.Ordinal)).Distinct().ToList();
        var handled = flags.Where(f => StatCoverage.Bridge.Contains(f)).ToList();
        SelfTest.Log($"unique item flags: {flags.Count}, handled {handled.Count} ({string.Join(", ", handled)}), dropped {flags.Count - handled.Count}: {string.Join(", ", flags.Except(handled))}");

        int sets = 0, bad = 0;
        foreach (var set in Db.Sets.Values)
        {
            var pieces = Db.Items.Values.Where(i => i.Set == set.Id).ToList();
            var items = pieces.Select(p => new ItemInstance { BaseId = p.Id, Level = 10 }).ToList();
            if (items.Count == 0 || set.Bonuses.Count == 0) continue;
            sets++;
            int minPieces = set.Bonuses.Min(b => b.Pieces);
            var fewest = items.Take(Math.Max(1, minPieces)).ToList();
            var mods = ItemEffects.SetBonuses(fewest, Db);
            var all = ItemEffects.SetBonuses(items, Db);
            bool atRight = fewest.Count < minPieces || mods.Count > 0;
            bool more = all.Count >= mods.Count && (items.Count < set.Bonuses.Max(b => b.Pieces) || all.Count > 0);
            var sheet = new StatSheet(Db);
            sheet.SetSource("sets", all);
            bool reaches = all.Count == 0 || all.Any(m => Math.Abs(sheet.Get(m.Stat) - new StatSheet(Db).Get(m.Stat)) > 1e-9);
            if (!atRight || !more || !reaches) { bad++; SelfTest.Log($"set {set.Id}: pieces {items.Count}, min {minPieces}, first {mods.Count} mods, all {all.Count} mods, reaches sheet {reaches}"); }
        }
        SelfTest.Check($"set bonuses apply at their piece counts and reach the sheet ({sets} sets)", bad == 0, $"{bad} sets off");

        // COS-88: every stat a unique item or a set bonus sets has a reader, or is dropped with a reason.
        var uniqueAndSet = Db.Items.Values.SelectMany(i => i.UniqueEffects.Select(e => (Who: i.Id, e.Stat)))
            .Concat(Db.Sets.Values.SelectMany(st => st.Bonuses.SelectMany(b => b.Effects.Select(e => (Who: st.Id, e.Stat)))))
            .Where(x => StatCoverage.Classify(x.Stat) == null).Select(x => $"{x.Who}->{x.Stat}").Distinct().ToList();
        SelfTest.Check("every unique-item and set-bonus stat has a reader or is dropped with a reason", uniqueAndSet.Count == 0, string.Join("; ", uniqueAndSet.Take(12)));
    }
}
