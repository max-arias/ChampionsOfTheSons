using ChampionsOfTheSons.Core.Content;
using ChampionsOfTheSons.Core.Rules;
using ChampionsOfTheSons.Mod.Spells;
using TheForest.Utils;

namespace ChampionsOfTheSons.Mod.Death;

/// <summary>COS-76 in-game checks: the death rule on the local character, then (only in a -Only run) a real death through the game.</summary>
internal static class DeathSelfTest
{
    private const float T0 = 2;
    private static double _xpBefore;
    private static int _deathsBefore, _levelBefore;

    private static PlayerCharacter Pc => PlayerState.Character;

    /// <summary>True when the run was limited with --cots-selftest-only (a real death would spoil later phases of a full run).</summary>
    private static bool OnlyRun => Environment.GetCommandLineArgs().Any(a => a.StartsWith("--cots-selftest-only=", StringComparison.Ordinal));

    public static void Register()
    {
        SelfTest.Add(T0, "death: rule on the character", () =>
        {
            Pc.SetLevel(12);
            Pc.AddXp(XpCurve.Goal(12) * 0.6);
            Pc.OnKill(5000);
            SpellWorld.Buffs.Apply("selftest_buff", 60, new[] { new StatModifier(StatIds.AllDamage, EffectOp.Mult, 2) });
            double before = Pc.Progress.Xp;
            double expect = DeathPenalty.XpLost(12, before);
            double lost = DeathFeature.HandleDeath();
            SelfTest.Check("death: lost the penalty share of the level's XP", Math.Abs(lost - expect) < 1e-6 && Math.Abs(Pc.Progress.Xp - (before - expect)) < 1e-6,
                $"{before:0} -> {Pc.Progress.Xp:0} (lost {lost:0}, expected {expect:0})");
            SelfTest.Check("death: level kept", Pc.Progress.Level == 12);
            SelfTest.Check("death: massacre streak cleared", Pc.Massacre.PendingXp == 0);
            SelfTest.Check("death: spell buffs cleared", SpellWorld.Buffs.Count == 0);
            SelfTest.Check("death: stat buff removed from the sheet", Math.Abs(Pc.Stats.Get(StatIds.AllDamage) - new StatSheet(ModContent.Db).Get(StatIds.AllDamage)) < 1e-9,
                $"all damage {Pc.Stats.Get(StatIds.AllDamage):0.###}");
        });

        SelfTest.Add(T0 + 1, "death: a real death runs the rule", () =>
        {
            if (!OnlyRun)
            {
                SelfTest.Log("death: real death skipped in a full run (it would end the world for later phases)");
                return;
            }
            TestKit.Unpause();
            Pc.SetLevel(8);
            Pc.AddXp(XpCurve.Goal(8) * 0.5);
            _xpBefore = Pc.Progress.Xp;
            _levelBefore = Pc.Progress.Level;
            _deathsBefore = DeathFeature.Deaths;
            // A lethal hit through the game's damage path (KillPlayer's routine does not advance at time scale 0).
            LocalPlayer.Stats.Hit(LocalPlayer.Vitals.Health.GetMax() * 10);
            SelfTest.Log($"death: lethal hit; health {LocalPlayer.Vitals.Health._currentValue:0.0}, game dead flag {LocalPlayer.Stats.IsDead()}, downed {LocalPlayer.Stats.PlayerIsDowned}");
        });

        SelfTest.Add(T0 + 4, "death: penalty after the game's death", () =>
        {
            if (!OnlyRun) return;
            SelfTest.Check("death: the game's death was seen once", DeathFeature.Deaths == _deathsBefore + 1, $"deaths {_deathsBefore} -> {DeathFeature.Deaths}");
            double expect = DeathPenalty.XpLost(_levelBefore, _xpBefore);
            SelfTest.Check("death: XP penalty applied", Math.Abs(Pc.Progress.Xp - (_xpBefore - expect)) < 1e-3, $"{_xpBefore:0} -> {Pc.Progress.Xp:0}");
            SelfTest.Check("death: level kept after the game's death", Pc.Progress.Level == _levelBefore);
            TestKit.Screenshot("death-01-after");
        });
    }
}
