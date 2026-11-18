using ChampionsOfTheSons.Mod.Net;
using ChampionsOfTheSons.Mod.Saves;
using RedLoader;
using SonsSdk;
using SonsSdk.Networking;

namespace ChampionsOfTheSons.Mod;

/// <summary>RedLoader entry point. Keep this thin: wiring only.</summary>
public class ChampionsOfTheSonsMod : SonsMod
{
    public static ChampionsOfTheSonsMod? Instance { get; private set; }

    public ChampionsOfTheSonsMod()
    {
        Instance = this;
        HarmonyPatchAll = true;
        if (SelfTest.Enabled) OnUpdateCallback = SelfTest.Update;
    }

    protected override void OnInitializeMod()
    {
        Config.Init();
    }

    protected override void OnSdkInitialized()
    {
        ModContent.Load();

        SonsSaveTools.Register(new PlayerProgressSaveable());
        SonsSaveTools.Register(new WorldSettingsSaveable());

        Packets.Register(CotsNetEvent.Instance);

        RLog.Msg($"Champions of the Sons {Manifest.Version} loaded: {ModContent.Db.Summary()}");
        if (SelfTest.Enabled)
        {
            SelfTest.RegisterDefaultSteps();
            SelfTest.Log("enabled");
        }
    }

    protected override void OnGameStart()
    {
        RLog.Msg("Champions of the Sons: game started");
    }
}
