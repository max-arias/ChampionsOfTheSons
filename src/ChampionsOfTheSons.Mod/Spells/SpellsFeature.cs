using ChampionsOfTheSons.Core.Rules;
using SonsSdk;

namespace ChampionsOfTheSons.Mod.Spells;

/// <summary>Hotkeys for the six spell slots (entries in <see cref="Config.SpellKeys"/>, rebindable in the settings overlay, COS-104).</summary>
internal static class SpellKeys
{
    public static void Init()
    {
        for (int i = 0; i < SpellBook.Slots; i++)
        {
            int slot = i;
            Config.SpellKeys[i].Notify(() => SpellCaster.Cast(slot));
        }
    }

    /// <summary>The key shown on the bar.</summary>
    public static string Label(int slot)
    {
        try
        {
            return Settings.KeyNames.Short(Config.SpellKeys[slot]?.Value ?? Config.SpellDefaults[slot]);
        }
        catch
        {
            return Settings.KeyNames.Short(Config.SpellDefaults[slot]);
        }
    }
}

/// <summary>Spell casting; the spell slots are drawn by the action bar (COS-151).</summary>
internal sealed class SpellsFeature : IModFeature
{
    public void Init()
    {
        SpellKeys.Init();
        SpellNet.Init();
        Bridge.EnemyPatches.EnemyHit += (_, enemy, damage, _) =>
        {
            if (damage > 0) SpellEffects.OnPlayerHit(enemy);
        };
        Bridge.EnemyPatches.LocalWeaponHit += SpellEffects.OnWeaponHit;
    }

    public void OnGameStart()
    {
        SpellWorld.Reset();
        SpellCaster.Refund.Clear();
    }

    public void Update(float dt, bool inWorld)
    {
        if (inWorld)
        {
            SpellWorld.Step(dt);
            SpellEffects.ItemTick(dt);
            double back = SpellCaster.Refund.Tick(dt);
            if (back > 0) SpellCaster.Gain(back);
        }
    }

    public void RegisterSelfTests()
    {
        SpellsSelfTest.Register();
        Spells2SelfTest.Register();
        SelfTest.PhaseReset += SpellWorld.Reset;
    }
}
