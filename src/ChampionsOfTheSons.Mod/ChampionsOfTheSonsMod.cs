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

    private static bool _initialized;

    protected override void OnSdkInitialized() => EnsureInitialized();

    /// <summary>
    /// Content, saves, networking and features. Normally runs when the SDK is ready; a dedicated server never raises that event,
    /// so OnGameStart calls it too (idempotent).
    /// </summary>
    private static void EnsureInitialized()
    {
        if (_initialized) return;
        _initialized = true;
        bool server = Net.ServerConfig.IsDedicatedServer;
        ModContent.Load();

        SonsSaveTools.Register(new PlayerProgressSaveable());
        SonsSaveTools.Register(new WorldSettingsSaveable());

        Packets.Register(CotsNetEvent.Instance);
        Bridge.EnemyRegistry.Init();
        if (!server) UI.CotsUi.Init();
        Features.Discover(server);
        Features.Each("Init", f => f.Init());

        RLog.Msg($"Champions of the Sons {Instance!.Manifest.Version} loaded{(server ? " (dedicated server)" : "")}: {ModContent.Db.Summary()}");
        if (SelfTest.Enabled && !server)
        {
            SelfTest.PhaseReset += () =>
            {
                UI.CotsUi.Inventory?.Close();
                Perks.PerksFeature.Panel?.Close();
                Crafting.CraftingFeature.Panel?.Close();
                HostRules.WorldRulesFeature.Settings?.Close();
            };
            SelfTest.RegisterDefaultSteps();
            SelfTest.BeginPhase("bridge");
            Bridge.BridgeSelfTest.Register();
            SelfTest.BeginPhase("ui");
            UI.UiSelfTest.Register();
            foreach (var f in Features.Loaded)
            {
                SelfTest.BeginPhase(f.GetType().Name);
                Features.Each("RegisterSelfTests", x => { if (ReferenceEquals(x, f)) x.RegisterSelfTests(); });
            }
            SelfTest.Log("enabled");
        }
    }

    protected override void OnGameStart()
    {
        EnsureInitialized();
        Bridge.PlayerBridge.Reset();
        Features.Each("OnGameStart", f => f.OnGameStart());
        RLog.Msg($"Champions of the Sons: game started (difficulty {WorldState.Rules.Difficulty.Name}, level {PlayerState.Character.Progress.Level})");
    }
}
