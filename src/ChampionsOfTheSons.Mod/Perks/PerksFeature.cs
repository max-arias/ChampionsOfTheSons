using SonsSdk;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ChampionsOfTheSons.Mod.Perks;

/// <summary>Creates the perk window and wires its key (Config.PerksKey).</summary>
internal sealed class PerksFeature : IModFeature
{
    public static PerkPanel Panel { get; private set; } = null!;

    public void Init()
    {
        Panel = new PerkPanel();
        Panel.Build();
        Config.PerksKey.Notify(Panel.Toggle);
    }

    public void Update(float dt, bool inWorld)
    {
        Panel.Tick();
        if (!Panel.IsOpen) return;
        if (Input.GetKeyDown(KeyCode.Escape)) Panel.Close();
        else if (Settings.ModKeys.PressedThisFrame(Config.PerksKey)) Panel.Toggle();
    }

    public void RegisterSelfTests() => PerksSelfTest.Register();
}
