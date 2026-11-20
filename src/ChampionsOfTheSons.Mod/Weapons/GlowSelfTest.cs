using ChampionsOfTheSons.Core;
using ChampionsOfTheSons.Core.Rules;
using ChampionsOfTheSons.Mod.Loot;
using ChampionsOfTheSons.Mod.Visuals;
using TheForest.Utils;
using UnityEngine;

namespace ChampionsOfTheSons.Mod.Weapons;

/// <summary>
/// COS-108 checks: the four looks in hand (Common none, Magic small, Rare medium, Legendary strong with embers, light and a
/// flame trail), uniques always Legendary, a unique's own style, hit sparks, the reduced-effects setting, the same looks on
/// ground drops, and the frame cost. One screenshot per look.
/// </summary>
internal static class GlowSelfTest
{
    private static int _frame0;
    private static float _time0;
    private static double _baselineMs;
    private static readonly List<int> Drops = new();
    private static Sons.Ai.Vail.VailActor? _target;
    private static float _sparksBefore;

    public static void Register(float t)
    {
        SelfTest.Add(t, "glow: draw machete", () =>
        {
            PlayerState.Character.Inventory.Remove(WeaponTypes.WeaponSlot);
            int id = GameItems.Find("Machete", 359);
            var inv = LocalPlayer.Inventory;
            if (inv.AmountOf(id, true, false) == 0) inv.AddItem(id, 1, true, false, null);
            SelfTest.Check("glow: machete in hand", inv.TryEquip(id, false, true, false));
        });
        t += 6f; // the draw animation takes several seconds on a loaded machine

        SelfTest.Add(t, "glow: common", () =>
        {
            Equip("item.dull_longsword", "long_sword", Rarity.Common);
            var skin = WeaponModels.LocalSkin;
            SelfTest.Check("glow: common look has no glow", skin != null && !skin.Look.Glows && Count(skin.Model, "cots-glow") == 0, Describe(skin));
        });
        SelfTest.Add(t + 1.2f, "glow: common screenshot", () => TestKit.Screenshot("glow-common"));
        t += 1.8f;

        // Magic, Rare (rolled Epic: Epic maps onto Rare) and Legendary (rolled Primal), in hand, one screenshot each.
        foreach (var (rarity, level) in new[] { (Rarity.Magic, GlowLevel.Magic), (Rarity.Rare, GlowLevel.Rare), (Rarity.Legendary, GlowLevel.Legendary) })
        {
            SelfTest.Add(t, $"glow: {level}", () =>
            {
                Equip("item.dull_longsword", "long_sword", rarity);
                var skin = WeaponModels.LocalSkin;
                bool extras = level == GlowLevel.Legendary;
                SelfTest.Check($"glow: {rarity} shows the {level} look", skin != null && skin.Look.Level == level && Count(skin.Model, "cots-glow-edge") > 0
                    && Count(skin.Model, "cots-glow-light") == (extras ? 1 : 0) && Count(skin.Model, "cots-glow-wisp") == (extras ? 1 : 0), Describe(skin));
                if (extras)
                    SelfTest.Check("glow: legendary trail brighter", skin!.Trail != null && skin.Trail.widthMultiplier > (float)skin.Type.TrailWidth * 1.2f,
                        skin.Trail == null ? "no trail" : $"width {skin.Trail.widthMultiplier:0.000}");
            });
            SelfTest.Add(t + 1.3f, $"glow: {level} screenshot", () => TestKit.Screenshot("glow-" + level.ToString().ToLowerInvariant()));
            t += 1.8f;
        }

        // A unique rolled Common still gets the full Legendary look; swing it into an enemy for the trail and sparks.
        SelfTest.Add(t, "glow: unique", () =>
        {
            Equip("item.rage", "great_sword", Rarity.Common);
            var skin = WeaponModels.LocalSkin;
            var c = skin?.Look.Color ?? Color.black;
            SelfTest.Check("glow: a Common unique is Legendary and red-orange", skin?.Look.Level == GlowLevel.Legendary && c.r > 0.9f && c.g < 0.5f && c.b < 0.25f, $"{skin?.Look.Level} {c}");
            SelfTest.Check("glow: unique has edge, light and embers", skin != null && Count(skin.Model, "cots-glow-edge") > 0
                && Count(skin.Model, "cots-glow-light") == 1 && Count(skin.Model, "cots-glow-wisp") == 1, Describe(skin));
        });
        SelfTest.Add(t + 1.3f, "glow: unique screenshot", () => TestKit.Screenshot("glow-unique"));
        SelfTest.Add(t + 2f, "glow: unique swing", () => WeaponModels.Base?.AttackButtonDown());
        SelfTest.Add(t + 2.3f, "glow: unique swing screenshot", () => TestKit.Screenshot("glow-unique-swing"));
        SelfTest.Add(t + 2.8f, "glow: hit target", () => _target = TestKit.Spawn(distance: 4f));
        SelfTest.Add(t + 3f, "glow: hit sparks", () =>
        {
            _sparksBefore = WeaponGlowFx.LastSparks;
            if (_target != null) TestKit.Hit(_target, 5);
            SelfTest.Check("glow: legendary hit makes sparks", WeaponGlowFx.LastSparks > _sparksBefore, $"last sparks {WeaponGlowFx.LastSparks:0.0}");
        });
        SelfTest.Add(t + 3.1f, "glow: sparks screenshot", () =>
        {
            TestKit.Screenshot("glow-sparks");
            if (_target != null) TestKit.Kill(_target);
        });
        t += 4f;

        SelfTest.Add(t, "glow: unique style", () =>
        {
            Equip("item.frost_giant", "hammer", Rarity.Rare);
            var skin = WeaponModels.LocalSkin;
            var want = WeaponGlow.ParseHex(ModContent.Db.Items["item.frost_giant"].Glow?.Tint)!.Value;
            bool tinted = skin != null && Mathf.Abs(skin.Look.Color.b - want.B) < 0.02f && Mathf.Abs(skin.Look.Color.r - want.R) < 0.02f;
            SelfTest.Check("glow: unique uses its own style and tint", skin?.Look.Style == "frost" && tinted && Count(skin.Model, "cots-glow-wisp") == 1, Describe(skin));
        });
        SelfTest.Add(t + 1.3f, "glow: unique style screenshot", () => TestKit.Screenshot("glow-unique-frost"));
        t += 1.8f;

        // Ground drops, one row: three Legendary looks (a Common-rolled unique, a styled unique, a Primal), a Rare and three
        // Magic. The frame cost compares the same row of models without glow (Common, non-unique) against it with glow.
        var row = new[]
        {
            ("item.rage", Rarity.Common), ("item.frost_giant", Rarity.Rare), ("item.dull_longsword", Rarity.Legendary), ("item.relic_hammer", Rarity.Rare),
            ("item.dull_longsword", Rarity.Magic), ("item.relic_hammer", Rarity.Magic), ("item.axe_of_swiftness", Rarity.Magic),
        };
        var plain = new[] { "item.dull_longsword", "item.dull_longsword", "item.dull_longsword", "item.relic_hammer", "item.dull_longsword", "item.relic_hammer", "item.axe_of_swiftness" };
        SelfTest.Add(t, "glow: plain drops", () =>
        {
            PlayerState.Character.Inventory.Remove(WeaponTypes.WeaponSlot);
            WeaponModels.Scan();
            for (int i = 0; i < plain.Length; i++) Drops.Add(DropAt(i, plain[i], Rarity.Common));
        });
        SelfTest.Add(t + 1f, "glow: plain drops meter", StartMeter);
        SelfTest.Add(t + 4f, "glow: glowing drops", () =>
        {
            _baselineMs = StopMeter();
            foreach (var id in Drops) LootFeature.Ground.Remove(id);
            Drops.Clear();
            for (int i = 0; i < row.Length; i++) Drops.Add(DropAt(i, row[i].Item1, row[i].Item2));
        });
        SelfTest.Add(t + 5f, "glow: glowing drops meter", StartMeter);
        t += 5f;
        SelfTest.Add(t + 0.5f, "glow: drops screenshot", () => TestKit.Screenshot("glow-drops"));
        SelfTest.Add(t + 3.5f, "glow: drops and frame cost", () =>
        {
            double ms = StopMeter();
            int particles = UnityEngine.Object.FindObjectsOfType<ParticleSystem>().Where(ps => IsGlow(ps.transform)).Sum(ps => ps.particleCount);
            var systems = UnityEngine.Object.FindObjectsOfType<ParticleSystem>().Where(ps => IsGlow(ps.transform)).ToList();
            SelfTest.Log($"glow: 7 drops with glow {ms:0.00} ms/frame, same drops without {_baselineMs:0.00}; {WeaponGlowFx.Alive} glow parts, {particles} glow particles in {systems.Count} systems ("
                + string.Join(", ", systems.Select(ps => $"{ps.transform.parent?.name}:{ps.particleCount}/{ps.main.maxParticles}")) + ")");
            // Frame time on the shared test machine swings ±20% between identical runs, so it is logged, not checked;
            // the checks are the deterministic budget: glow parts and live particles.
            SelfTest.Check("glow: glow parts bounded", WeaponGlowFx.Alive <= 16, $"{WeaponGlowFx.Alive}");
            SelfTest.Check("glow: glow particles bounded", particles < 250, $"{particles}");
            var roots = Drops.Select(id => LootVisuals.All.TryGetValue(id, out var v) ? v.Root : null).ToList();
            bool ok = roots.Count == 7 && roots.All(r => r != null && Count(r, "cots-glow-edge") > 0)
                && roots.Take(3).All(r => Count(r, "cots-glow-wisp") == 1) && roots.Skip(3).All(r => Count(r, "cots-glow-wisp") == 0);
            SelfTest.Check("glow: drops show the same looks", ok,
                string.Join(", ", roots.Select(r => r == null ? "missing" : $"{Count(r, "cots-glow-edge")}e/{Count(r, "cots-glow-wisp")}w")));
        });
        t += 4f;

        bool reducedBefore = false;
        SelfTest.Add(t, "glow: reduced effects", () =>
        {
            reducedBefore = VisualsFeature.ReduceHeavyEffects;
            VisualsFeature.SetReduceHeavyEffects(true);
            WeaponModels.Reset();
            Equip("item.rage", "great_sword", Rarity.Common);
            var skin = WeaponModels.LocalSkin;
            SelfTest.Check("glow: reduced keeps the edge, drops particles and light", skin != null && Count(skin.Model, "cots-glow-edge") > 0
                && Count(skin.Model, "cots-glow-wisp") == 0 && Count(skin.Model, "cots-glow-light") == 0, Describe(skin));
        });
        SelfTest.Add(t + 1f, "glow: clean up", () =>
        {
            VisualsFeature.SetReduceHeavyEffects(reducedBefore);
            foreach (var id in Drops) LootFeature.Ground.Remove(id);
            Drops.Clear();
            PlayerState.Character.Inventory.Remove(WeaponTypes.WeaponSlot);
            WeaponModels.Scan();
            SelfTest.Check("glow: gone with the weapon", WeaponModels.LocalSkin == null);
        });
    }

    private static int DropAt(int i, string baseId, Rarity rarity)
    {
        var p = LocalPlayer.Transform.position + LocalPlayer.Transform.forward * 2.4f + LocalPlayer.Transform.right * (0.7f * (i - 3));
        var item = new ItemInstance { BaseId = baseId, Level = 10, Rarity = rarity, Stats = Array.Empty<RolledStat>() };
        return LootFeature.Ground.Add(item, p.x, p.y, p.z).Id;
    }

    private static void Equip(string itemId, string typeId, Rarity rarity)
    {
        WeaponsSelfTest.EquipItem(itemId, typeId, rarity);
        WeaponModels.Scan();
    }

    private static void StartMeter()
    {
        _frame0 = Time.frameCount;
        _time0 = Time.realtimeSinceStartup;
    }

    private static double StopMeter()
    {
        int frames = Math.Max(1, Time.frameCount - _frame0);
        return (Time.realtimeSinceStartup - _time0) * 1000.0 / frames;
    }

    private static int Count(GameObject? root, string prefix) =>
        root == null ? 0 : root.GetComponentsInChildren<Transform>(true).Count(t => t.name.StartsWith(prefix, StringComparison.Ordinal));

    private static bool IsGlow(Transform t)
    {
        for (var p = t; p != null; p = p.parent)
            if (p.name.StartsWith("cots-glow", StringComparison.Ordinal)) return true;
        return false;
    }

    private static string Describe(WeaponSkin? skin) => skin == null ? "no skin"
        : $"{skin.Look.Level} {skin.Look.Style}, parts {skin.GlowParts}: {Count(skin.Model, "cots-glow-edge")} edge, {Count(skin.Model, "cots-glow-light")} light, {Count(skin.Model, "cots-glow-wisp")} particles";
}
