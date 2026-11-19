using ChampionsOfTheSons.Mod.Bridge;
using RedLoader;
using Sons.Ai.Vail;
using SUI;
using UnityEngine;
using static SUI.SUI;

namespace ChampionsOfTheSons.Mod.Hud;

/// <summary>The in-game HUD: enemy nameplates, damage numbers, XP bar, level-up and massacre widgets.</summary>
public sealed class HudFeature : IModFeature
{
    internal static HudFeature? Instance { get; private set; }

    private SPanelOptions _root = null!;
    private bool _built;

    internal Nameplates Plates { get; } = new();
    internal DamageNumbers Numbers { get; } = new();
    internal ProgressHud Progress { get; } = new();

    public void Init()
    {
        Instance = this;
        _root = RegisterNewPanel("cots-hud", enableInput: false);
        var layer = SContainer.Anchor(AnchorType.Fill);
        _root.Add(layer);
        Plates.Build(layer);
        Numbers.Build(layer);
        Progress.Build(layer);
        HudKit.NoRaycast(_root.Root);
        _built = true;

        SonsSdk.SdkEvents.OnWorldSimActorAdded.Subscribe(sim => Safe(() => Plates.Add(sim)));
        SonsSdk.SdkEvents.OnWorldSimActorRemoved.Subscribe(sim => Safe(() => Plates.OnSimRemoved(sim)));
        EnemyPatches.EnemyHit += OnEnemyHit;
        PlayerState.Character.LeveledUp += Progress.OnLeveledUp;
        PlayerState.Character.Massacre.Paid += Progress.OnPaid;
    }

    public void Update(float dt, bool inWorld)
    {
        if (!_built) return;
        dt = Time.unscaledDeltaTime; // fades keep running while the game is paused
        bool show = inWorld && !UI.CotsUi.Inventory.IsOpen;
        if (_root.Root.activeSelf != show) _root.Active(show);
        if (!show) return;
        Plates.Update(dt, Config.ShowNameplates.Value);
        Numbers.Update(dt, Config.ShowDamageNumbers.Value);
        Progress.Update(dt, true);
    }

    public void OnGameStart()
    {
        Plates.Clear();
        Numbers.Clear();
    }

    private void OnEnemyHit(VailActor actor, EnemyRuntime runtime, double damage, bool crit)
    {
        try
        {
            if (!_built || actor == null) return;
            Plates.OnHit(actor);
            if (!Config.ShowDamageNumbers.Value || !runtime.LastHitByLocalPlayer) return;
            var sim = actor.GetWorldSimActor();
            if (sim == null) return;
            var pos = sim._position;
            float h = 1.7f * Mathf.Max(0.5f, (float)runtime.Profile.Scale);
            Numbers.Spawn(pos + new Vector3(0, h, 0), damage, crit);
        }
        catch (Exception e)
        {
            RLog.Error($"Champions of the Sons: HUD hit handler failed: {e}");
        }
    }

    private static void Safe(Action a)
    {
        try
        {
            a();
        }
        catch (Exception e)
        {
            RLog.Error($"Champions of the Sons: HUD tracking failed: {e}");
        }
    }

    public void RegisterSelfTests() => HudSelfTest.Register();
}
