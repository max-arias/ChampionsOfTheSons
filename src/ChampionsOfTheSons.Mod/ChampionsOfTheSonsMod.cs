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
        OnUpdateCallback = OnUpdate;
    }

    private static void OnUpdate()
    {
        float dt = UnityEngine.Time.deltaTime;
        if (TheForest.Utils.LocalPlayer.IsInWorld)
        {
            Bridge.PlayerBridge.Update(dt);
            PlayerState.Character.Tick(dt);
        }
        SelfTest.Update();
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
        Bridge.EnemyRegistry.Init();
        UI.CotsUi.Init();

        RLog.Msg($"Champions of the Sons {Manifest.Version} loaded: {ModContent.Db.Summary()}");
        if (SelfTest.Enabled)
        {
            SelfTest.RegisterDefaultSteps();
            Bridge.BridgeSelfTest.Register();
            UI.UiSelfTest.Register();
            SelfTest.Log("enabled");
        }
    }

    protected override void OnGameStart()
    {
        Bridge.PlayerBridge.Reset();
        RLog.Msg($"Champions of the Sons: game started (difficulty {WorldState.Rules.Difficulty.Name}, level {PlayerState.Character.Progress.Level})");
    }
}
