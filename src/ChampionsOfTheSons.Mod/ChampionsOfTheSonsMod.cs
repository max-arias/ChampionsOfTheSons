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

    private static readonly int PlayerBridgeSlot = Perf.Perf.Register("PlayerBridge.Update", true);
    private static readonly int CharacterTickSlot = Perf.Perf.Register("Character.Tick", true);

    private static void OnUpdate()
    {
        float dt = UnityEngine.Time.deltaTime;
        bool inWorld = TheForest.Utils.LocalPlayer.IsInWorld;
        if (inWorld)
        {
            using (Perf.Perf.Scope(PlayerBridgeSlot)) Bridge.PlayerBridge.Update(dt);
            using (Perf.Perf.Scope(CharacterTickSlot)) PlayerState.Character.Tick(dt);
        }
        Features.UpdateAll(dt, inWorld);
        Perf.Perf.EndFrame(UnityEngine.Time.unscaledDeltaTime);
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
                HostRules.WorldRulesFeature.Settings?.Close();
                // Enemies from earlier phases stay in the world: switch them off so they neither cast nor take part in this phase.
                foreach (var enemy in Bridge.EnemyRegistry.Snapshot()) enemy.TestActive = false;
            };
            SelfTest.RegisterDefaultSteps();
            // --cots-selftest-only=<names>: run just those checks, comma-separated feature type names plus "bridge" and "ui".
            var onlyArg = Environment.GetCommandLineArgs().FirstOrDefault(a => a.StartsWith("--cots-selftest-only=", StringComparison.Ordinal))?.Split('=', 2)[1];
            var only = onlyArg?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToHashSet();
            if (only == null || only.Contains("bridge"))
            {
                SelfTest.BeginPhase("bridge");
                Bridge.BridgeSelfTest.Register();
            }
            if (only == null || only.Contains("ui"))
            {
                SelfTest.BeginPhase("ui");
                UI.UiSelfTest.Register();
            }
            foreach (var f in Features.Loaded)
            {
                if (only != null && !only.Contains(f.GetType().Name)) continue;
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
        RLog.Msg($"Champions of the Sons: game started (game difficulty {WorldState.Rules.GameDifficulty.Mode}, level {PlayerState.Character.Progress.Level})");
    }
}
