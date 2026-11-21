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
    private static VailActor? _elite, _mini, _boss, _poisonMini, _fireBoss, _coldBoss;
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

    /// <summary>Spawns a still enemy of a tier; a miniboss or boss gets exactly <paramref name="abilities"/> (an element ability sets its aura).</summary>
    private static VailActor? Spawn(EnemyTier tier, float distance, float sideways, params EnemyAbility[] abilities)
    {
        if (tier >= EnemyTier.Miniboss)
        {
            var list = new[] { tier == EnemyTier.Boss ? EnemyAbility.BossSteadfast : EnemyAbility.EliteSteadfast, EnemyAbility.Juggernaut }.Concat(abilities).ToArray();
            EnemyRegistry.TestProfileOverride = p => p with { Tier = tier, Abilities = list, Scale = tier == EnemyTier.Boss ? 1.2 : p.Scale };
        }
        try
        {
            var v = TestKit.Spawn(VailActorTypeId.Carl, distance, sideways, Still(tier >= EnemyTier.Miniboss ? null : tier));
            var r = TestKit.Profile(v);
            if (r != null) { r.TestActive = true; r.TestSilent = true; r.Actor = v; }
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
            Loot.LootFeature.ClearAll(); // drops from earlier kills would hide the effects in the shots
            _elite = Spawn(EnemyTier.Elite, 6, -4.5f);
            _mini = Spawn(EnemyTier.Miniboss, 8, -1.5f);
            _poisonMini = Spawn(EnemyTier.Miniboss, 8, 1.5f, EnemyAbility.Poisonous);
            _boss = Spawn(EnemyTier.Boss, 11, -4.5f);
            _fireBoss = Spawn(EnemyTier.Boss, 12, 0, EnemyAbility.Meteor, EnemyAbility.ExtraHealth, EnemyAbility.ExtraDamage);
            _coldBoss = Spawn(EnemyTier.Boss, 11, 4.5f, EnemyAbility.Blizzard);
            SelfTest.Check("tierfx: spawned elite, minibosses and bosses", TestKit.Profile(_elite)?.Profile.Tier == EnemyTier.Elite
                && new[] { _mini, _poisonMini }.All(v => TestKit.Profile(v)?.Profile.Tier == EnemyTier.Miniboss)
                && new[] { _boss, _fireBoss, _coldBoss }.All(v => TestKit.Profile(v)?.Profile.Tier == EnemyTier.Boss));
        });
        SelfTest.Add(3f, "tierfx: effects attached by tier and element", () =>
        {
            TierFxFeature.ScanNow();
            var e = Of(_elite);
            SelfTest.Check("tierfx: bundle has the tier prefabs", FxKit.Available && new[] { TierFxFeature.Aura, TierFxFeature.Embers, TierFxFeature.Shimmer, TierFxFeature.Power }.All(n => Bundles.CotsBundle.Prefab(n) != null));
            SelfTest.Check("tierfx: elite gets an aura only", e != null && e.Parts.Count == 1 && e.Light == null, $"{e?.Parts.Count} parts");
            foreach (var (v, what) in new[] { (_mini, "miniboss"), (_boss, "boss") })
            {
                var a = Of(v);
                SelfTest.Check($"tierfx: a {what} without an element gets only the ring at its feet and a soft glow", a != null && a.Parts.Count == 1 && !HasPower(a) && a.Light != null,
                    $"{a?.Parts.Count} parts, light {a?.Light != null}");
            }
            var p = Of(_poisonMini);
            SelfTest.Check("tierfx: a poison miniboss gets a green aura, embers and the power aura", p != null && p.Element == EnemyElement.Poison && p.Parts.Count == 3 && HasPower(p), $"{p?.Element}, {p?.Parts.Count} parts");
            var f = Of(_fireBoss);
            SelfTest.Check("tierfx: a fire boss gets the full fire aura with the heat shimmer", f != null && f.Element == EnemyElement.Fire && f.Parts.Count == 5 && HasPower(f), $"{f?.Element}, {f?.Parts.Count} parts");
            var c = Of(_coldBoss);
            SelfTest.Check("tierfx: a cold boss gets a blue-white aura, no shimmer", c != null && c.Element == EnemyElement.Cold && c.Parts.Count == 4 && HasPower(c), $"{c?.Element}, {c?.Parts.Count} parts");
            SelfTest.Check("tierfx: more abilities, a stronger aura", f != null && c != null && f.LightBase > c.LightBase, $"{f?.LightBase:0.0} vs {c?.LightBase:0.0}");
        });
        SelfTest.Add(3.5f, "tierfx: shot all tiers", () => { Loot.LootFeature.ClearAll(); TestKit.Screenshot("tierfx-01-tiers"); });
        SelfTest.Add(3.8f, "tierfx: shot elite close", () => { Look(_elite); TestKit.Screenshot("tierfx-02-elite"); });
        SelfTest.Add(4.1f, "tierfx: shot miniboss close", () => { Look(_mini); TestKit.Screenshot("tierfx-03-miniboss"); });
        SelfTest.Add(4.4f, "tierfx: shot boss close", () => { Look(_boss); TestKit.Screenshot("tierfx-04-boss"); });
        SelfTest.Add(4.75f, "tierfx: shot fire boss close", () => { Look(_fireBoss); TestKit.Screenshot("tierfx-07-fire-boss"); });
        SelfTest.Add(5.1f, "tierfx: shot cold boss close", () => { Look(_coldBoss); TestKit.Screenshot("tierfx-08-cold-boss"); });
        SelfTest.Add(5.45f, "tierfx: shot poison miniboss close", () => { Look(_poisonMini); TestKit.Screenshot("tierfx-09-poison-miniboss"); });
        SelfTest.Add(5.8f, "tierfx: a pack of elites", () =>
        {
            for (int i = 0; i < 12; i++) { var v = Spawn(EnemyTier.Elite, 14 + (i / 4) * 2.5f, -4.5f + (i % 4) * 3); if (v != null) Pack.Add(v); }
            TierFxFeature.ScanNow();
            _frame0 = Time.frameCount;
            _time0 = Time.realtimeSinceStartup;
            SelfTest.Check("tierfx: attachments stay under the cap", TierFxFeature.All.Count <= TierFxFeature.MaxAttached, TierFxFeature.All.Count.ToString());
        });
        SelfTest.Add(7.8f, "tierfx: frame time with the pack", () =>
        {
            int frames = Time.frameCount - _frame0;
            float ms = frames > 0 ? (Time.realtimeSinceStartup - _time0) * 1000f / frames : 0;
            SelfTest.Log($"tierfx: {TierFxFeature.All.Count} enemies with effects, {frames} frames, {ms:0.0} ms per frame");
            Loot.LootFeature.ClearAll();
            TestKit.Screenshot("tierfx-05-pack");
        });
        SelfTest.Add(8.1f, "tierfx: kill the boss", () =>
        {
            int before = TierFxFeature.Bursts;
            if (_fireBoss != null) TestKit.Kill(_fireBoss);
            TierFxFeature.ScanNow();
            SelfTest.Check("tierfx: the boss's death bursts and its effects go", TierFxFeature.Bursts > before && Of(_fireBoss) == null, $"bursts {before} -> {TierFxFeature.Bursts}");
        });
        SelfTest.Add(8.4f, "tierfx: shot boss death", () => TestKit.Screenshot("tierfx-06-boss-death"));
        SelfTest.Add(9f, "tierfx: cleanup", () =>
        {
            foreach (var v in Pack.Concat(new[] { _elite, _mini, _poisonMini, _boss, _coldBoss }))
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

    /// <summary>The power-up aura (COS-110, Wave 7) is among the parts.</summary>
    private static bool HasPower(TierFxFeature.Attached a) => a.Parts.Any(p => p != null && p.name.EndsWith(TierFxFeature.Power, StringComparison.Ordinal));
}
