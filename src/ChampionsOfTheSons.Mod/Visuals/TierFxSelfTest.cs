using ChampionsOfTheSons.Core.Rules;
using ChampionsOfTheSons.Mod.Bridge;
using Sons.Ai.Vail;
using UnityEngine;

namespace ChampionsOfTheSons.Mod.Visuals;

/// <summary>
/// COS-110 in game: an elite, a miniboss and a boss in front of the player get their tier effects; a pack of elites stays under the
/// attach cap; killing the boss bursts. Windowed runs save one screenshot per tier to %TEMP%\cots-ui\ for Max's approval.
/// </summary>
internal static class TierFxSelfTest
{
    private static VailActor? _elite, _mini, _boss;
    private static readonly List<VailActor> Pack = new();
    private static int _frame0;
    private static float _time0;

    /// <summary>Affixes that never act on the player, so spawned enemies stay still.</summary>
    private static readonly EnemyAbility[] Passive =
    {
        EnemyAbility.Steadfast, EnemyAbility.EliteSteadfast, EnemyAbility.BossSteadfast, EnemyAbility.ExtraHealth,
        EnemyAbility.ExtraDamage, EnemyAbility.Juggernaut, EnemyAbility.Undead, EnemyAbility.RainEmpowerment,
    };

    private static WorldRules Still(EnemyTier? tier) => WorldState.Rules with
    {
        ForcedTier = tier,
        DisabledAbilities = Enum.GetValues<EnemyAbility>().Where(a => !Passive.Contains(a)).ToArray(),
    };

    private static VailActor? Spawn(EnemyTier tier, float distance, float sideways)
    {
        if (tier == EnemyTier.Boss)
            EnemyRegistry.TestProfileOverride = p => p with { Tier = EnemyTier.Boss, Abilities = new[] { EnemyAbility.BossSteadfast, EnemyAbility.Juggernaut }, Scale = 1.2 };
        try
        {
            var v = TestKit.Spawn(VailActorTypeId.Carl, distance, sideways, Still(tier == EnemyTier.Boss ? null : tier));
            var r = TestKit.Profile(v);
            if (r != null) { r.TestActive = true; r.Actor = v; }
            return v;
        }
        finally
        {
            EnemyRegistry.TestProfileOverride = null;
        }
    }

    private static TierFxFeature.Attached? Of(VailActor? v) =>
        TestKit.Profile(v) is { } r && TierFxFeature.All.TryGetValue(r, out var a) ? a : null;

    public static void Register()
    {
        SelfTest.Add(0.5f, "tierfx: unpause", TestKit.Unpause);
        SelfTest.Add(1f, "tierfx: spawn one of each tier", () =>
        {
            TierFxFeature.Clear();
            Loot.LootFeature.ClearAll(); // beams from earlier kills would hide the effects in the shots
            _elite = Spawn(EnemyTier.Elite, 6, -3);
            _mini = Spawn(EnemyTier.Miniboss, 8, 0);
            _boss = Spawn(EnemyTier.Boss, 11, 3);
            SelfTest.Check("tierfx: spawned elite, miniboss and boss", TestKit.Profile(_elite)?.Profile.Tier == EnemyTier.Elite
                && TestKit.Profile(_mini)?.Profile.Tier == EnemyTier.Miniboss && TestKit.Profile(_boss)?.Profile.Tier == EnemyTier.Boss,
                $"{TestKit.Profile(_elite)?.Profile.Tier} {TestKit.Profile(_mini)?.Profile.Tier} {TestKit.Profile(_boss)?.Profile.Tier}");
        });
        SelfTest.Add(3f, "tierfx: effects attached by tier", () =>
        {
            TierFxFeature.ScanNow();
            var e = Of(_elite);
            var m = Of(_mini);
            var b = Of(_boss);
            SelfTest.Check("tierfx: bundle has the tier prefabs", FxKit.Available && new[] { TierFxFeature.Aura, TierFxFeature.Embers, TierFxFeature.Shimmer }.All(n => Bundles.CotsBundle.Prefab(n) != null));
            SelfTest.Check("tierfx: elite gets an aura only", e != null && e.Parts.Count == 1 && e.Light == null, $"{e?.Parts.Count} parts");
            SelfTest.Check("tierfx: miniboss gets an aura, embers and a light", m != null && m.Parts.Count == 2 && m.Light != null, $"{m?.Parts.Count} parts, light {m?.Light != null}");
            SelfTest.Check("tierfx: boss gets an aura, ground ring, embers, shimmer and a light", b != null && b.Parts.Count == 4 && b.Light != null, $"{b?.Parts.Count} parts, light {b?.Light != null}");
        });
        SelfTest.Add(3.5f, "tierfx: shot all tiers", () => { Loot.LootFeature.ClearAll(); TestKit.Screenshot("tierfx-01-tiers"); });
        SelfTest.Add(3.8f, "tierfx: shot elite close", () => { Look(_elite); TestKit.Screenshot("tierfx-02-elite"); });
        SelfTest.Add(4.1f, "tierfx: shot miniboss close", () => { Look(_mini); TestKit.Screenshot("tierfx-03-miniboss"); });
        SelfTest.Add(4.4f, "tierfx: shot boss close", () => { Look(_boss); TestKit.Screenshot("tierfx-04-boss"); });
        SelfTest.Add(4.7f, "tierfx: a pack of elites", () =>
        {
            for (int i = 0; i < 12; i++) { var v = Spawn(EnemyTier.Elite, 14 + (i / 4) * 2.5f, -4.5f + (i % 4) * 3); if (v != null) Pack.Add(v); }
            TierFxFeature.ScanNow();
            _frame0 = Time.frameCount;
            _time0 = Time.realtimeSinceStartup;
            SelfTest.Check("tierfx: attachments stay under the cap", TierFxFeature.All.Count <= TierFxFeature.MaxAttached, TierFxFeature.All.Count.ToString());
        });
        SelfTest.Add(6.7f, "tierfx: frame time with the pack", () =>
        {
            int frames = Time.frameCount - _frame0;
            float ms = frames > 0 ? (Time.realtimeSinceStartup - _time0) * 1000f / frames : 0;
            SelfTest.Log($"tierfx: {TierFxFeature.All.Count} enemies with effects, {frames} frames, {ms:0.0} ms per frame");
            Loot.LootFeature.ClearAll();
            TestKit.Screenshot("tierfx-05-pack");
        });
        SelfTest.Add(7f, "tierfx: kill the boss", () =>
        {
            int before = TierFxFeature.Bursts;
            if (_boss != null) TestKit.Kill(_boss);
            TierFxFeature.ScanNow();
            SelfTest.Check("tierfx: the boss's death bursts and its effects go", TierFxFeature.Bursts > before && Of(_boss) == null, $"bursts {before} -> {TierFxFeature.Bursts}");
        });
        SelfTest.Add(7.3f, "tierfx: shot boss death", () => TestKit.Screenshot("tierfx-06-boss-death"));
        SelfTest.Add(8f, "tierfx: cleanup", () =>
        {
            foreach (var v in Pack.Append(_elite).Append(_mini))
                if (v != null && TestKit.Profile(v) is { } r && r.IsActive) TestKit.Kill(v);
            Pack.Clear();
            TierFxFeature.Clear();
        });
    }

    /// <summary>Turns the player to face an enemy for the close-up shots.</summary>
    private static void Look(VailActor? v)
    {
        if (v == null || TheForest.Utils.LocalPlayer.Transform == null) return;
        var t = TheForest.Utils.LocalPlayer.Transform;
        var to = v.transform.position - t.position;
        to.y = 0;
        if (to.sqrMagnitude > 0.01f) t.rotation = Quaternion.LookRotation(to);
    }
}
