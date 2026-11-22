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

    public void RegisterSelfTests()
    {
        SelfTest.Add(2f, "gun: hold the shotgun", () =>
        {
            TestKit.Unpause();
            LocalPlayer.Inventory?.AddItem(Buckshot, 10, true, false, null);
            SelfTest.Check("gun: the shotgun is in hand", TestKit.EquipGameItem(Shotgun), $"right hand {LocalPlayer.Inventory?.RightHandItem?._itemID}");
        });
        SelfTest.Add(4f, "gun: fire", () =>
        {
            var w = LocalPlayer.Transform != null ? LocalPlayer.Transform.GetComponentInChildren<RangedWeapon>(false) : null;
            SelfTest.Check("gun: the shotgun's weapon is up", w != null);
            w?.Shoot();
        });
        // The bullets fly and hit for a while after the shot: the game must still be running two seconds later.
        SelfTest.Add(6f, "gun: the game survived the shot", () => SelfTest.Check("gun: the game is still running after a shotgun shot", true));
        SelfTest.Add(6.5f, "gun: put it away", () => LocalPlayer.Inventory?.StashHeldItems(false, false));
    }
}
