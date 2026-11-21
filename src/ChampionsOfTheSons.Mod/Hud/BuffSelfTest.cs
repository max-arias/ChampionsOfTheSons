using ChampionsOfTheSons.Core.Rules;
using ChampionsOfTheSons.Mod.Bridge;
using TheForest.Utils;

namespace ChampionsOfTheSons.Mod.Hud;

/// <summary>COS-89 in-game checks: debuffs and immunities through the buff system, and the HUD row that shows them.</summary>
internal static class BuffSelfTest
{
    private const float T0 = 90;

    public static void Register()
    {
        SelfTest.Add(T0, "buffs: debuffs on the player", () =>
        {
            PlayerEffects.Reset();
            Abilities.AbilityForces.AddSlow("selftest:slow", 0.5, 30);
            PlayerEffects.AddPoison(1, 30, "selftest");
            PlayerEffects.AddPoison(1, 30, "selftest2");
            PlayerDebuffs.Buffs.Apply(PlayerDebuffs.ArmorCorruption, "selftest", 30, 25);
            SelfTest.Check("buffs: two poisons and a slow slow the player", Math.Abs(PlayerDebuffs.SpeedMultiplier - 0.35f) < 0.01f, $"{PlayerDebuffs.SpeedMultiplier:0.###}");
            SelfTest.Check("buffs: armor corruption counted", Math.Abs(PlayerDebuffs.LostArmor - 25) < 1e-6);
            PlayerDebuffs.Buffs.Apply(PlayerDebuffs.StunImmune, "selftest", 30);
            SelfTest.Check("buffs: stun immunity blocks the stun", !PlayerEffects.Stun(3) && !PlayerEffects.Stunned);
            PlayerDebuffs.Buffs.Apply(PlayerDebuffs.Silence, "selftest", 30);
            SelfTest.Check("buffs: silence sets the silenced stat", PlayerState.Character.Stats.IsOn(StatIds.Silenced));
        });
        SelfTest.Add(T0 + 1, "buffs: HUD row", () =>
        {
            var shown = HudFeature.Instance?.Buffs.Shown ?? Array.Empty<string>();
            SelfTest.Check("buffs: HUD row shows the debuffs and the immunity",
                shown.Contains(PlayerDebuffs.Poison) && shown.Contains(PlayerDebuffs.MoveSlow) && shown.Contains(PlayerDebuffs.StunImmune), string.Join(",", shown));
            TestKit.Screenshot("buffs-01-hud-row");
            PlayerEffects.ClearDebuffs();
            SelfTest.Check("buffs: Purge clears the debuffs, keeps the immunity",
                !PlayerDebuffs.Buffs.Has(PlayerDebuffs.Poison) && !PlayerDebuffs.Buffs.Has(PlayerDebuffs.Silence) && PlayerDebuffs.Buffs.Has(PlayerDebuffs.StunImmune));
            PlayerEffects.Reset();
            LocalPlayer.Vitals?.Health.SetCurrentValue(LocalPlayer.Vitals.Health.GetMax());
        });
    }
}
