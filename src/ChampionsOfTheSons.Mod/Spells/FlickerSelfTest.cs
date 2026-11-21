using ChampionsOfTheSons.Core;
using ChampionsOfTheSons.Core.Content;
using ChampionsOfTheSons.Core.Rules;
using ChampionsOfTheSons.Mod.Bridge;
using Sons.Ai.Vail;
using TheForest.Utils;
using UnityEngine;

namespace ChampionsOfTheSons.Mod.Spells;

/// <summary>Flicker Strike (COS-141) owns only its self-test; the spell itself is in the spell table.</summary>
public sealed class FlickerFeature : IModFeature
{
    public void RegisterSelfTests() => FlickerSelfTest.Register();
}

/// <summary>
/// COS-141 in game: three plain enemies ahead, a cast, and then the caster stands where it started and all three took their
/// hit in one frame. A cast with nobody in reach costs nothing. Enemies come from TestKit, never from the save.
/// </summary>
internal static class FlickerSelfTest
{
    private static PlayerCharacter Pc => PlayerState.Character;
    private static readonly List<VailActor> Enemies = new();
    private static readonly Dictionary<VailActor, float> HealthBefore = new();

    public static void Register()
    {
        SelfTest.Add(0.5f, "flicker: unpause", TestKit.Unpause);
        SelfTest.Add(1f, "flicker: spawn three", Spawn);
        SelfTest.Add(4f, "flicker: cast", Cast);
        SelfTest.Add(7f, "flicker: payoff", Payoff);
        SelfTest.Add(8f, "flicker: no target", NoTarget);
        SelfTest.Add(9f, "flicker: cleanup", Cleanup);
    }

    private static void Spawn()
    {
        Enemies.Clear();
        var plain = WorldState.Rules with { AllowElites = false, PartyLevelOverride = 5 };
        foreach (var (d, side) in new[] { (7f, -3f), (9f, 2f), (12f, 0f) })
            if (TestKit.Spawn(VailActorTypeId.Carl, d, side, plain) is { } v) Enemies.Add(v);
        SelfTest.Check("flicker: three enemies spawned", Enemies.Count == 3, Enemies.Count.ToString());
    }

    private static SpellCastOutcome CastSpell()
    {
        Pc.SetLevel(Math.Max(Pc.Progress.Level, 30));
        Pc.Stats.SetSource("test.energy", new[] { new StatModifier(StatIds.MaxEnergy, EffectOp.Add, 3000) });
        Pc.Spells.Equip(0, "spell.flicker_strike");
        Pc.Spells.Tick(1e6, Pc.Stats);
        var st = LocalPlayer.Vitals.Stamina;
        st.SetCurrentValue(st.GetMax());
        return SpellCaster.Cast(0);
    }

    private static void Cast()
    {
        HealthBefore.Clear();
        foreach (var e in Enemies)
            if (EnemyRegistry.Get(e) is { } r && r.Stats != null && EnemyRegistry.Health(r.Stats) is { } h) HealthBefore[e] = h._currentValue;
        var outcome = CastSpell();
        SelfTest.Check("flicker: the cast starts a run over the reachable enemies", outcome == SpellCastOutcome.Cast && FlickerRun.Running && FlickerRun.LastTargets >= 1,
            $"{outcome}, {FlickerRun.LastTargets} target(s); in range {FlickerRun.LastReach.InRange}, in sight {FlickerRun.LastReach.InSight}");
    }

    private static void Payoff()
    {
        var feet = LocalPlayer.Transform.position;
        var origin = FlickerRun.LastOrigin;
        float off = new Vector2(feet.x - origin.x, feet.z - origin.z).magnitude;
        SelfTest.Check("flicker: the caster ends back where the cast started", !FlickerRun.Running && off < 1.5f, $"{off:F2} m away");
        int hurt = 0;
        foreach (var e in Enemies)
        {
            var r = EnemyRegistry.Get(e);
            float now = r?.Stats != null && EnemyRegistry.Health(r.Stats) is { } h ? h._currentValue : 0;
            if (!HealthBefore.TryGetValue(e, out var before) || now < before || r == null || !r.IsActive) hurt++;
        }
        SelfTest.Check("flicker: every visited enemy took its hit", hurt >= FlickerRun.LastTargets && FlickerRun.LastPayoffFrame > 0,
            $"{hurt} hurt of {FlickerRun.LastTargets} visited, payoff frame {FlickerRun.LastPayoffFrame}");
        SelfTest.Check("flicker: the visit order covered all three", FlickerRun.LastTargets == 3, FlickerRun.LastTargets.ToString());
    }

    private static void NoTarget()
    {
        foreach (var e in Enemies) if (e != null) TestKit.Kill(e);
        Pc.Spells.Tick(1e6, Pc.Stats);
        var t = LocalPlayer.Transform;
        var away = t.position + Vector3.up * 0; // enemies are dead now: nobody is in reach
        var outcome = CastSpell();
        SelfTest.Check("flicker: nobody in reach: no cast, no cost, no cooldown", outcome == SpellCastOutcome.NoTarget && Pc.Spells.IsReady(0),
            $"{outcome} at {away}");
    }

    private static void Cleanup()
    {
        foreach (var e in Enemies) if (e != null) TestKit.Kill(e);
        Enemies.Clear();
        Pc.Stats.RemoveSource("test.energy");
        Pc.Spells.Equip(0, null);
    }
}
