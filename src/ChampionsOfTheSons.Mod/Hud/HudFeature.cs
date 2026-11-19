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

    /// <summary>Whether the HUD layer (nameplates, XP bar, banners) is drawn this frame.</summary>
    internal bool Shown => _built && _root.Root.activeSelf;

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
        PlayerBridge.PlayerHurt += OnPlayerHurt;
        PlayerState.Character.LeveledUp += Progress.OnLeveledUp;
        PlayerState.Character.Massacre.Paid += Progress.OnPaid;
    }

    public void Update(float dt, bool inWorld)
    {
        if (!_built) return;
        dt = Time.unscaledDeltaTime; // fades keep running while the game is paused
        bool show = inWorld && !UI.CotsUi.AnyPanelOpen;
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
            Numbers.Spawn(pos + new Vector3(0, h, 0), damage, crit, EnemyPatches.LastHitKind);
        }
        catch (Exception e)
        {
            RLog.Error($"Champions of the Sons: HUD hit handler failed: {e}");
        }
    }

    /// <summary>Damage the local player took (red) or a dodge, floating just ahead of the camera.</summary>
    internal void OnPlayerHurt(double damage, bool dodged, Core.Rules.DamageKind kind)
    {
        try
        {
            if (!_built || !Config.ShowDamageNumbers.Value) return;
            if (!dodged && damage < 0.5) return;
            var cam = HudKit.PlayerCamera();
            if (cam == null) return;
            var t = cam.transform;
            var pos = t.position + t.forward * 3f - t.up * 0.6f + t.right * (UnityEngine.Random.value - 0.5f) * 1.2f;
            if (dodged) Numbers.Spawn(pos, UI.UiText.T("ui.followups.dodge", "Dodge"), DamageNumbers.DodgeColor, false);
            else Numbers.Spawn(pos, HudKit.Compact(damage), kind == Core.Rules.DamageKind.Physical ? DamageNumbers.HurtColor : DamageNumbers.ColorOf(kind), false);
        }
        catch (Exception e)
        {
            RLog.Error($"Champions of the Sons: HUD hurt handler failed: {e}");
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
