using ChampionsOfTheSons.Core.Rules;
using ChampionsOfTheSons.Mod.Bridge;
using ChampionsOfTheSons.Mod.UI;
using Sons.Ai.Vail;
using Sons.StatSystem;
using SUI;
using TMPro;
using UnityEngine;
using static SUI.SUI;
using UnityEngine.UI;

namespace ChampionsOfTheSons.Mod.Hud;

/// <summary>What a nameplate shows right now, for the self-test.</summary>
internal readonly record struct PlateInfo(bool Visible, string Name, int Level, EnemyTier Tier, float Fraction, string Affixes);

/// <summary>
/// Nameplates over scaled enemies, drawn as pooled uGUI elements placed from the camera projection.
/// Every enemy that got a profile is tracked; a plate is bound only while it is in range, on screen and not behind scenery.
/// No per-frame allocations: text is set when it changes, positions are plain RectTransform writes.
/// </summary>
internal sealed class Nameplates
{
    private const int PoolSize = 24;
    private const float PlateW = 260;
    private const float OcclusionInterval = 0.2f;

    private sealed class Entry
    {
        public WorldSimActor Sim = null!;
        public IntPtr Ptr;
        public StatsManager? Stats;
        public HealthStat? Health;
        public EnemyRuntime? Runtime;
        public Plate? Plate;
        public bool Occluded;
        public float NextOcclusion;
        public float NextProfileCheck;
        public string Why = "new";
    }

    private sealed class Plate
    {
        public SContainerOptions Root = null!, BarBack = null!, Fill = null!;
        public SLabelOptions Name = null!, Affix = null!;
        public RectTransform Rt = null!;
        public CanvasGroup Group = null!;
        public Image FillImage = null!;
        public string NameText = "", AffixText = "";
        public int Level;
        public EnemyTier Tier;
        public float Fraction = -1;
        public float Scale = 1;
        public bool Bound;
    }

    private readonly List<Entry> _entries = new();
    private readonly Dictionary<IntPtr, Entry> _bySim = new();
    private readonly List<Plate> _free = new();
    private readonly List<Plate> _all = new();
    private Camera? _cam;
    private float _time;

    public int Tracked => _entries.Count;

    public int Visible
    {
        get
        {
            int n = 0;
            foreach (var p in _all) if (p.Bound) n++;
            return n;
        }
    }

    public void Build(SContainerOptions layer)
    {
        for (int i = 0; i < PoolSize; i++)
        {
            var p = BuildPlate();
            layer.Add(p.Root);
            p.Root.Active(false);
            _free.Add(p);
            _all.Add(p);
        }
    }

    private static Plate BuildPlate()
    {
        var p = new Plate();
        p.Root = SContainer.TopLeft(0, 0).Size(PlateW, 74).Background(HudKit.C("#0A0D12AA"), EBackground.Round10);
        p.Name = HudKit.Label("", 22, UiTheme.Text, TextAlignmentOptions.Center).TopLeft(8, 4).Size(PlateW - 16, 28);
        p.Root.Add(p.Name);
        p.BarBack = SContainer.TopLeft(20, 36).Size(PlateW - 40, 12).Background(HudKit.C(UiTheme.BarBack), EBackground.None);
        p.Root.Add(p.BarBack);
        p.Fill = SContainer.Background(HudKit.C("#C0392B"), EBackground.None);
        p.BarBack.Add(p.Fill);
        p.Affix = HudKit.Label("", 15, UiTheme.TextDim, TextAlignmentOptions.Center).TopLeft(8, 51).Size(PlateW - 16, 20);
        p.Root.Add(p.Affix);

        p.Rt = p.Root.RectTransform;
        p.Group = HudKit.Group(p.Root.Root);
        p.FillImage = p.Fill.Root.GetComponent<Image>();
        HudKit.NoRaycast(p.Root.Root);
        return p;
    }

    // ------------------------------------------------------------------ tracking

    /// <summary>Tracks a world-simulation actor (every spawned creature gets one, loaded in the scene or not). Non-enemies drop out by themselves.</summary>
    public void Add(WorldSimActor sim)
    {
        if (sim == null || _bySim.ContainsKey(sim.Pointer)) return;
        var e = new Entry { Sim = sim, Ptr = sim.Pointer };
        _bySim[e.Ptr] = e;
        _entries.Add(e);
    }

    public void OnSimRemoved(WorldSimActor? sim)
    {
        if (sim == null || !_bySim.TryGetValue(sim.Pointer, out var e)) return;
        Remove(_entries.IndexOf(e));
    }

    /// <summary>A hit proves the enemy is ours: make sure it is tracked even if the added event was missed.</summary>
    public void OnHit(VailActor actor)
    {
        var sim = actor.GetWorldSimActor();
        if (sim != null) Add(sim);
    }

    private static bool Resolve(Entry e)
    {
        e.Stats ??= e.Sim.StatManager;
        if (e.Stats == null) return false;
        e.Runtime ??= EnemyRegistry.Get(e.Stats);
        if (e.Runtime == null) return false;
        e.Health ??= EnemyRegistry.Health(e.Stats);
        return true;
    }

    private void Remove(int index)
    {
        if (index < 0) return;
        var e = _entries[index];
        Release(e);
        _bySim.Remove(e.Ptr);
        int last = _entries.Count - 1;
        _entries[index] = _entries[last];
        _entries.RemoveAt(last);
    }

    private void Release(Entry e)
    {
        var p = e.Plate;
        if (p == null) return;
        e.Plate = null;
        p.Bound = false;
        p.Root.Active(false);
        _free.Add(p);
    }

    public void Clear()
    {
        for (int i = _entries.Count - 1; i >= 0; i--) Remove(i);
    }

    // ------------------------------------------------------------------ per frame

    public void Update(float dt, bool enabled)
    {
        _time += dt;
        if (_cam == null || !_cam.isActiveAndEnabled) _cam = HudKit.PlayerCamera();
        var cam = _cam;
        bool show = enabled && cam != null;
        float k = HudKit.CanvasPerPixel;
        Vector3 camPos = cam != null ? cam.transform.position : Vector3.zero;
        float screenW = Screen.width, screenH = Screen.height;

        for (int i = _entries.Count - 1; i >= 0; i--)
        {
            var e = _entries[i];
            if (e.Runtime == null)
            {
                // Not (yet) one of our enemies: the profile is rolled when the game gives the actor health, so look again now and then.
                if (_time < e.NextProfileCheck) continue;
                e.NextProfileCheck = _time + 1f;
                if (!Resolve(e)) continue;
            }
            else if (EnemyRegistry.Get(e.Stats) == null) { Remove(i); continue; } // died or despawned
            if (!show) { e.Why = cam == null ? "no camera" : "disabled"; Release(e); continue; }
            var pos = e.Sim._position;

            var profile = e.Runtime.Profile;
            float scale = Mathf.Max(0.5f, (float)profile.Scale);
            Vector3 head = pos + new Vector3(0, 2.1f * scale, 0);
            float range = profile.Tier == EnemyTier.Normal ? 40f : profile.Tier == EnemyTier.Elite ? 60f : 90f;
            float sqr = (head - camPos).sqrMagnitude;
            if (sqr > range * range) { e.Why = $"range {Mathf.Sqrt(sqr):0} cam {camPos} {cam!.name} head {head} scale {scale}"; Release(e); continue; }

            Vector3 sp = cam!.WorldToScreenPoint(head);
            if (sp.z <= 0.1f || sp.x < -40 || sp.x > screenW + 40 || sp.y < -40 || sp.y > screenH + 40) { e.Why = $"offscreen {sp}"; Release(e); continue; }

            if (_time >= e.NextOcclusion)
            {
                e.NextOcclusion = _time + OcclusionInterval + (e.Ptr.ToInt64() & 7) * 0.01f;
                e.Occluded = IsOccluded(camPos, head);
            }
            if (e.Occluded) { e.Why = "occluded"; Release(e); continue; }
            e.Why = "ok";

            var plate = e.Plate;
            if (plate == null)
            {
                if (_free.Count == 0) continue;
                plate = _free[_free.Count - 1];
                _free.RemoveAt(_free.Count - 1);
                e.Plate = plate;
                Bind(plate, profile);
            }

            float dist = Mathf.Sqrt(sqr);
            e.Health ??= EnemyRegistry.Health(e.Stats!);
            float frac = e.Health == null ? 1f : Mathf.Clamp01(e.Health._currentValue / (float)profile.MaxHealth);
            if (!Mathf.Approximately(frac, plate.Fraction))
            {
                plate.Fraction = frac;
                HudKit.SetFill(plate.Fill, frac);
            }
            plate.Group.alpha = Mathf.Clamp01((range - dist) / (range * 0.3f));
            float s = Mathf.Clamp(16f / Mathf.Max(dist, 1f), 0.65f, 1f) * plate.Scale;
            plate.Rt.localScale = new Vector3(s, s, 1);
            plate.Rt.anchoredPosition = new Vector2(sp.x * k, sp.y * k);
        }
    }

    private static bool IsOccluded(Vector3 from, Vector3 to)
    {
        var dir = to - from;
        float len = dir.magnitude;
        if (len < 0.5f) return false;
        if (!Physics.Raycast(from, dir / len, out var hit, len, ~0, QueryTriggerInteraction.Ignore)) return false;
        // A hit right at the head is the enemy's own collider; only something clearly in front hides the plate.
        return hit.distance < len - 1.5f;
    }

    private void Bind(Plate p, EnemyProfile profile)
    {
        p.Bound = true;
        p.Level = profile.Level;
        p.Tier = profile.Tier;
        string kindName = profile.KindId;
        foreach (var kind in ModContent.Db.Enemies.Values)
        {
            if (kind.Id != profile.KindId) continue;
            kindName = kind.Name;
            break;
        }
        kindName = UiText.T(profile.KindId + ".name", kindName);
        string tier = profile.Tier == EnemyTier.Normal ? "" : HudText.Tier(profile.Tier) + " ";
        p.NameText = $"{tier}{kindName}  <color=#9AA3AF>{HudText.Level} {profile.Level}</color>";
        p.Name.Text(p.NameText).FontColor(HudKit.TierHex(profile.Tier));
        p.AffixText = HudText.Affixes(profile.Abilities);
        p.Affix.Text(p.AffixText);
        p.Affix.Active(p.AffixText.Length > 0);
        p.Rt.sizeDelta = new Vector2(PlateW, p.AffixText.Length > 0 ? 74 : 54);
        p.FillImage.color = HudKit.C(HudKit.TierBarHex(profile.Tier));
        p.Scale = profile.Tier == EnemyTier.Boss ? 1.3f : profile.Tier == EnemyTier.Miniboss ? 1.15f : 1f;
        p.Fraction = -1;
        HudKit.Place(p.Rt, Vector2.zero, new Vector2(0.5f, 0), 0, 0);
        p.Root.Active(true);
    }

    // ------------------------------------------------------------------ test access

    public PlateInfo? Info(VailActor actor)
    {
        var sim = actor?.GetWorldSimActor();
        if (sim == null || !_bySim.TryGetValue(sim.Pointer, out var e)) return null;
        Resolve(e);
        var p = e.Plate;
        if (p == null) return new PlateInfo(false, e.Why, e.Runtime?.Profile.Level ?? 0, e.Runtime?.Profile.Tier ?? EnemyTier.Normal, 0, "");
        return new PlateInfo(p.Bound && p.Root.Root.activeSelf, p.NameText, p.Level, p.Tier, p.Fraction, p.AffixText);
    }
}
