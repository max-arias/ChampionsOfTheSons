using SonsSdk;

namespace ChampionsOfTheSons.Mod.Settings;

/// <summary>COS-104: the mod settings overlay (keys, sound volume), opened with <see cref="Config.SettingsKey"/> or the pad.</summary>
internal sealed class SettingsFeature : IModFeature
{
    public static SettingsPanel Panel { get; private set; } = null!;

    public void Init()
    {
        Panel = new SettingsPanel();
        Panel.Build();
        Config.SettingsKey.Notify(() => { if (!Panel.IsOpen) Panel.Toggle(); });
        SelfTest.PhaseReset += () => Panel.Close();
    }

    public void OnGameStart() => ModKeys.ApplyAll();

    public void Update(float dt, bool inWorld) => Panel.Tick();

    public void RegisterSelfTests() => SettingsSelfTest.Register();
}
