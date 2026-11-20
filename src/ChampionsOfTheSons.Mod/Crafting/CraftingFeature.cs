using ChampionsOfTheSons.Mod.UI;
using RedLoader;
using SonsSdk;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ChampionsOfTheSons.Mod.Crafting;

/// <summary>Creates the crafting window and wires its key.</summary>
internal sealed class CraftingFeature : IModFeature
{
    public static CraftingPanel Panel { get; private set; } = null!;
    private static KeybindConfigEntry _key = null!;

    public void Init()
    {
        _key = Config.Category.CreateKeybindEntry("crafting_key", "k", "Crafting", "Open the crafting window.");
        _key.SetScope(needsPlayerControllable: true);
        Panel = new CraftingPanel();
        Panel.Build();
        _key.Notify(Panel.Toggle);
    }

    public void Update(float dt, bool inWorld)
    {
        Panel.Tick();
        if (!Panel.IsOpen) return;
        if (Input.GetKeyDown(KeyCode.Escape)) Panel.Close();
        else if (Settings.ModKeys.PressedThisFrame(_key)) Panel.Toggle();
    }

    public void RegisterSelfTests() => CraftingSelfTest.Register();
}
