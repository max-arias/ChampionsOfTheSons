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
        bool inWorld = TheForest.Utils.LocalPlayer.IsInWorld;
        if (inWorld)
        {
            Bridge.PlayerBridge.Update(dt);
            PlayerState.Character.Tick(dt);
        }
        Features.Each("Update", f => f.Update(dt, inWorld));
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
        Features.Discover();
        Features.Each("Init", f => f.Init());

        RLog.Msg($"Champions of the Sons {Manifest.Version} loaded: {ModContent.Db.Summary()}");
        if (SelfTest.Enabled)
        {
            SelfTest.RegisterDefaultSteps();
            Bridge.BridgeSelfTest.Register();
            UI.UiSelfTest.Register();
            Features.Each("RegisterSelfTests", f => f.RegisterSelfTests());
            SelfTest.Log("enabled");
        }
    }

    protected override void OnGameStart()
    {
        Bridge.PlayerBridge.Reset();
        Features.Each("OnGameStart", f => f.OnGameStart());
        RLog.Msg($"Champions of the Sons: game started (difficulty {WorldState.Rules.Difficulty.Name}, level {PlayerState.Character.Progress.Level})");
    }
}
