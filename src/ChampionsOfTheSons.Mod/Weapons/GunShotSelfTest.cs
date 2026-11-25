using Sons.Weapon;
using TheForest.Utils;

namespace ChampionsOfTheSons.Mod.Weapons;

/// <summary>
/// Firing one of the game's firearms with the mod on (2026-11-22: a Harmony patch on BulletHandler.updateVelocity crashed
/// the game natively on the first bullet). A crash shows up as a run that never reaches SELFTEST DONE.
/// </summary>
public sealed class GunShotFeature : IModFeature
{
    private const int Shotgun = 358, Buckshot = 364;

    private static Sons.Ai.Vail.VailActor? _target;
    private static int _ammoBefore;

    public void RegisterSelfTests()
    {
        // COS-121: a real shot, not a simulated one: the Shoot hook names the weapon and the no-consume perk gives the round
        // back. A headless held gun does not aim reliably, so whether its pellets land on the target is left to a play check.
        SelfTest.Add(1f, "gun: target", () =>
        {
            TestKit.Unpause();
            TestKit.LookLevel();
            _target = TestKit.Spawn(distance: 6, rules: new Core.Rules.WorldRules { PartyLevelOverride = 1, AllowElites = false }, inView: true);
            SelfTest.Check("gun: target spawned", _target != null);
        });
        SelfTest.Add(2f, "gun: hold the shotgun", () =>
        {
            LocalPlayer.Inventory?.AddItem(Buckshot, 10, true, false, null);
            SelfTest.Check("gun: the shotgun is in hand", TestKit.EquipGameItem(Shotgun), $"right hand {LocalPlayer.Inventory?.RightHandItem?._itemID}");
        });
        SelfTest.Add(4f, "gun: fire", () =>
        {
            TestKit.LookLevel();
            var w = LocalPlayer.Transform != null ? LocalPlayer.Transform.GetComponentInChildren<RangedWeapon>(false) : null;
            SelfTest.Check("gun: the shotgun's weapon is up", w != null);
            PlayerState.Character.Stats.SetSource("test.noconsume", new[] { new Core.Rules.StatModifier(Core.Rules.RangedStatIds.NoConsumeChance, Core.Content.EffectOp.Add, 1) });
            PerkHooks.ShotTracker.Reset();
            _ammoBefore = LocalPlayer.Inventory?.AmountOf(Buckshot, true, false) ?? -1;
            w?.Shoot();
            SelfTest.Check("gun: the Shoot hook names the weapon", PerkHooks.ShotTracker.LastKind == Core.Rules.RangedKind.Bullet, PerkHooks.ShotTracker.LastKind.ToString());
        });
        // The bullets fly and hit for a while after the shot: the game must still be running two seconds later.
        SelfTest.Add(6f, "gun: the game survived the shot", () =>
        {
            SelfTest.Check("gun: the game is still running after a shotgun shot", true);
            int after = LocalPlayer.Inventory?.AmountOf(Buckshot, true, false) ?? -1;
            SelfTest.Check("gun: the no-consume perk gives the round back", after == _ammoBefore, $"{_ammoBefore} -> {after}");
        });
        SelfTest.Add(6.5f, "gun: put it away", () =>
        {
            PlayerState.Character.Stats.RemoveSource("test.noconsume");
            LocalPlayer.Inventory?.StashHeldItems(false, false);
            if (_target != null && !_target.IsDead()) TestKit.Kill(_target);
            _target = null;
        });
    }
}
