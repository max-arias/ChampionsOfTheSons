using ChampionsOfTheSons.Core.Rules;
using RedLoader;
using SonsSdk;
using TheForest.Utils;

namespace ChampionsOfTheSons.Mod.Spells;

/// <summary>Hotkeys for the six spell slots, kept in this feature's own config entries.</summary>
internal static class SpellKeys
{
    private static readonly string[] Defaults = { "z", "x", "v", "b", "n", "m" };
    private static readonly KeybindConfigEntry[] Entries = new KeybindConfigEntry[SpellBook.Slots];

    public static void Init()
    {
        for (int i = 0; i < SpellBook.Slots; i++)
        {
            int slot = i;
            var e = Config.Category.CreateKeybindEntry($"spell_slot_{i + 1}", Defaults[i], $"Spell slot {i + 1}", $"Cast the spell in slot {i + 1}.");
            e.SetScope(needsPlayerControllable: true);
            e.Notify(() => SpellCaster.Cast(slot));
            Entries[i] = e;
        }
    }

    /// <summary>The key shown on the bar: the configured key in upper case.</summary>
    public static string Label(int slot)
    {
        try
        {
            return Entries[slot]?.Value?.ToUpperInvariant() ?? (slot + 1).ToString();
        }
        catch
        {
            return Defaults[slot].ToUpperInvariant();
        }
    }
}

/// <summary>The spell bar and spell casting.</summary>
internal sealed class SpellsFeature : IModFeature
{
    public void Init()
    {
        SpellKeys.Init();
        new SpellBar().Build();
        Bridge.EnemyPatches.EnemyHit += (_, enemy, damage, _) =>
        {
            if (damage > 0) SpellEffects.OnPlayerHit(enemy);
        };
        Bridge.EnemyPatches.LocalWeaponHit += SpellEffects.OnWeaponHit;
    }

    public void OnGameStart() => SpellWorld.Reset();

    public void Update(float dt, bool inWorld)
    {
        if (inWorld)
        {
            SpellWorld.Step(dt);
            SpellEffects.ItemTick(dt);
        }
        var bar = SpellBar.Instance;
        if (bar == null) return;
        bool show = inWorld && !UI.CotsUi.AnyPanelOpen;
        bar.SetVisible(show);
        if (show) bar.Refresh();
    }

    public void RegisterSelfTests()
    {
        SpellsSelfTest.Register();
        Spells2SelfTest.Register();
        SelfTest.PhaseReset += SpellWorld.Reset;
    }
}
