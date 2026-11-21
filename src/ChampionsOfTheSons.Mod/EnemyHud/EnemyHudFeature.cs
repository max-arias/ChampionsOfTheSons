using ChampionsOfTheSons.Core.Rules;
using ChampionsOfTheSons.Mod.Bridge;
using ChampionsOfTheSons.Mod.Hud;
using RedLoader;
using Sons.Ai.Vail;
using SUI;
using TheForest.Utils;
using UnityEngine;
using static SUI.SUI;

namespace ChampionsOfTheSons.Mod.EnemyHud;

/// <summary>
/// COS-67: the souls-like boss health bar. A Boss-tier enemy gets a bar at the top centre while it is alive, within
/// <see cref="EngageRange"/> of the player and aware of them (in the game's combat-aware state, or hit by the local
/// player in the last <see cref="HitMemory"/> s). It hides at once on death and <see cref="LoseTrack"/> s after the boss
/// stops being engaged. Two bars at most, nearest first. Hidden with the HUD (panels, book, menus).
/// Also hosts the self-tests for the enemy scanner, danger marks and named elites (COS-68).
/// </summary>
public sealed class EnemyHudFeature : IModFeature
{
    internal const float EngageRange = 60f, HitMemory = 10f, LoseTrack = 3f;
    private const float ScanInterval = 0.25f;
    /// <summary>One bar (Max 2026-11-21: two stacked bars crowded the screen); other engaged bosses show as "+N".</summary>
    internal const int MaxBars = 1;

    internal static EnemyHudFeature? Instance { get; private set; }

    private SPanelOptions _root = null!;
    private readonly BossBar[] _bars = new BossBar[MaxBars];
    private bool _built;
    private float _time, _nextScan;

    /// <summary>When each boss was last hit by the local player and last engaged (unscaled seconds).</summary>
    private readonly Dictionary<EnemyRuntime, float> _lastHit = new();
    private readonly Dictionary<EnemyRuntime, float> _engagedAt = new();
    private readonly List<(EnemyRuntime R, float Dist)> _order = new();
    private readonly List<EnemyRuntime> _drop = new();

    internal IReadOnlyList<BossBar> Bars => _bars;

    /// <summary>A boss newly engages the local player (its bar appears); the tier effects burst on it (COS-110).</summary>
    public static event Action<EnemyRuntime>? BossEngaged;
    internal int VisibleBars => _bars.Count(b => b.Visible);

    /// <summary>Whether this enemy has a boss bar now (its nameplate hides, Max 2026-11-21).</summary>
    internal bool HasBar(EnemyRuntime r)
    {
        if (!LayerShown) return false;
        foreach (var b in _bars) if (b.Visible && ReferenceEquals(b.Target, r)) return true;
        return false;
    }

    /// <summary>Canvas rectangles (y up) of the visible bars, for the nameplates to keep clear of; returns how many.</summary>
    internal int BarRects(Rect[] into)
    {
        if (!LayerShown) return 0;
        int n = 0;
        foreach (var b in _bars) if (b.Visible && n < into.Length) into[n++] = b.Bounds;
        return n;
    }
    /// <summary>Whether the boss bar layer is drawn this frame (it hides with the HUD).</summary>
    internal bool LayerShown => _built && _root.Root.activeSelf;
    /// <summary>The panel's place among the UI panels; later ones draw on top (self-test).</summary>
    internal int PanelIndex => _built ? _root.Root.transform.GetSiblingIndex() : -1;

    public void Init()
    {
        Instance = this;
        _root = RegisterNewPanel("cots-bossbar", enableInput: false);
        var layer = SContainer.Anchor(AnchorType.Fill);
        _root.Add(layer);
        for (int i = 0; i < MaxBars; i++) _bars[i] = new BossBar(layer);
        HudKit.NoRaycast(_root.Root);
        _root.Active(false);
        _built = true;
        EnemyPatches.EnemyHit += OnEnemyHit;
    }

    public void OnGameStart() => Clear();

    private void Clear()
    {
        _lastHit.Clear();
        _engagedAt.Clear();
        foreach (var b in _bars) b?.Hide();
    }

    private void OnEnemyHit(VailActor actor, EnemyRuntime runtime, double damage, bool crit)
    {
        if (runtime.Profile.Tier == EnemyTier.Boss && runtime.LastHitByLocalPlayer) _lastHit[runtime] = _time;
    }

    public void Update(float dt, bool inWorld)
    {
        if (!_built) return;
        dt = Time.unscaledDeltaTime;
        _time += dt;
        bool show = inWorld && Config.ShowBossBar.Value && (HudFeature.Instance?.Shown ?? false);
        if (_root.Root.activeSelf != show)
        {
            _root.Active(show);
            if (show) _root.Root.transform.SetAsLastSibling(); // over the nameplates and the other HUD panels (Max 2026-11-21)
        }
        if (!show) return;
        try
        {
            if (_time >= _nextScan)
            {
                _nextScan = _time + ScanInterval;
                Scan();
            }
            foreach (var b in _bars) b.Update(dt);
        }
        catch (Exception e)
        {
            RLog.Error($"Champions of the Sons: boss bar failed: {e}");
            _nextScan = _time + 5;
        }
    }

    /// <summary>Re-picks which bosses get a bar. Four times a second; walks the registry without copying it.</summary>
    private void Scan()
    {
        Vector3 player = LocalPlayer.Transform != null ? LocalPlayer.Transform.position : Vector3.zero;
        foreach (var r in EnemyRegistry.All)
        {
            if (r.Profile.Tier != EnemyTier.Boss) continue;
            if (!Alive(r)) continue;
            float dist = Distance(r, player);
            bool hitRecently = _lastHit.TryGetValue(r, out var hitAt) && _time - hitAt < HitMemory;
            if (dist <= EngageRange && (hitRecently || Aware(r)))
            {
                r.Engaged = true; // a story boss starts using abilities (COS-91)
                if (!_engagedAt.ContainsKey(r)) BossEngaged?.Invoke(r);
                _engagedAt[r] = _time;
            }
        }

        _order.Clear();
        _drop.Clear();
        foreach (var (r, at) in _engagedAt)
        {
            // Dead or despawned: gone at once. Lost track of the player: gone after a short grace.
            if (!Alive(r) || EnemyRegistry.Get(r.Stats) == null || _time - at > LoseTrack) _drop.Add(r);
            else _order.Add((r, Distance(r, player)));
        }
        foreach (var r in _drop)
        {
            _engagedAt.Remove(r);
            _lastHit.Remove(r);
        }
        _order.Sort((a, b) => a.Dist.CompareTo(b.Dist));

        // Keep a boss in the slot it already has so bars do not swap places when distances cross.
        for (int i = 0; i < MaxBars; i++)
        {
            var current = _bars[i].Target;
            if (current != null && !_order.Exists(o => ReferenceEquals(o.R, current))) _bars[i].Hide();
        }
        int shown = 0;
        foreach (var (r, _) in _order)
        {
            if (shown >= MaxBars) break;
            // The nearest engaged boss keeps the bar; the rest are counted on it.
            int slot = Array.FindIndex(_bars, b => ReferenceEquals(b.Target, r));
            if (slot < 0) slot = Array.FindIndex(_bars, b => b.Target == null);
            if (slot < 0) break;
            _bars[slot].Show(r, slot);
            shown++;
        }
        foreach (var b in _bars) if (b.Target != null) b.SetMore(_order.Count - shown);
    }

    private static bool Alive(EnemyRuntime r)
    {
        try
        {
            return r.Actor != null && !r.Actor.IsDead() && r.Actor.GetHealth() > 0 && r.IsActive;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>The game's own "in combat" AI state. Only the host runs the AI; on a client a hit is what engages.</summary>
    private static bool Aware(EnemyRuntime r)
    {
        if (r.TestAware is { } forced) return forced;
        if (r.NetAware is { } host) return host; // co-op client: the host's AI decides (COS-116)
        try
        {
            return r.Actor != null && r.Actor.IsInCombatAwareState();
        }
        catch
        {
            return false;
        }
    }

    private static float Distance(EnemyRuntime r, Vector3 player)
    {
        try
        {
            var sim = r.Actor?.GetWorldSimActor();
            if (sim != null) return Vector3.Distance(sim._position, player);
            if (r.Actor != null) return Vector3.Distance(r.Actor.transform.position, player);
        }
        catch
        {
            // fall through
        }
        return r.LastPosition is { } last ? Vector3.Distance(last, player) : float.MaxValue;
    }

    public void RegisterSelfTests() => EnemyHudSelfTest.Register();
}
