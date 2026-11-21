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
internal readonly record struct PlateInfo(bool Visible, string Name, int Level, EnemyTier Tier, float Fraction, string Affixes, DangerRating Danger);

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

    /// <summary>At most this many plates are drawn; the rest of a crowd shows none (COS-54).</summary>
    internal const int MaxPlates = 8;
    /// <summary>Only the highest-priority plates show their affix line.</summary>
    internal const int MaxAffixLines = 3;
    /// <summary>Seconds a hit keeps an enemy at the front of the queue.</summary>
    private const float HitPriorityTime = 6f;

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
        /// <summary>Far out of range: not looked at again before this time (COS-132: thousands of world-sim enemies per frame).</summary>
        public float NextRangeCheck;
        public string Why = "new";
        public float LastHit = -100f;
    }

    /// <summary>A plate that could be drawn this frame. Filled into a reused array: no per-frame allocations.</summary>
    private struct Candidate
    {
        public Entry Entry;
        public Vector2 Pos;
        /// <summary>Screen pixels: the middle of the body and half its projected height (for the look-at test).</summary>
        public Vector2 Chest;
        public float HalfHeight;
        public float Dist, Range, Priority;
    }

    /// <summary>Tally marks on the plate: one more per danger rating (COS-68).</summary>
    private const int Marks = 5;
    /// <summary>Seconds the crosshair has to stay on an enemy before the scanner card opens, and how long it lingers after.</summary>
    private const float FocusDelay = 0.25f, FocusLinger = 0.6f;
    /// <summary>The scanner only reads enemies this close.</summary>
    private const float ScanRange = 40f;

    private sealed class ByPriority : IComparer<Candidate>
    {
        public int Compare(Candidate a, Candidate b) => b.Priority.CompareTo(a.Priority);
    }

    private static readonly ByPriority PriorityOrder = new();
    private Candidate[] _candidates = new Candidate[64];
    private readonly Rect[] _placed = new Rect[MaxPlates];
    private readonly Rect[] _barRects = new Rect[4];

    private sealed class Plate
    {
        public SContainerOptions Root = null!, BarBack = null!, Fill = null!;
        public SLabelOptions Name = null!, Affix = null!;
        public readonly Image[] Marks = new Image[Nameplates.Marks];
        public DangerRating Danger;
        public int DangerForLevel = -1;
        public RectTransform Rt = null!;
        public CanvasGroup Group = null!;
        public Image FillImage = null!;
        public string NameText = "", AffixText = "";
        public int Level;
        public EnemyTier Tier;
        public float Fraction = -1;
        public float Scale = 1;
        public bool Bound;
        public bool AffixShown;
        public Rect Last;
        /// <summary>Covered by a plate placed first or by the boss bar: faded out (Max 2026-11-21) instead of stacked upward.</summary>
        public bool Hidden;
        public float Shown = 1;
    }

    private readonly List<Entry> _entries = new();
    private readonly Dictionary<IntPtr, Entry> _bySim = new();
    private readonly List<Plate> _free = new();
    private readonly List<Plate> _all = new();
    private Camera? _cam;
    private float _time;
    private Entry? _focus, _scanned;
    private float _focusHeld, _focusLastSeen;

    /// <summary>The scanner card (COS-68), drawn beside the plate of the enemy under the crosshair.</summary>
    public ScanCard Card { get; } = new();
    /// <summary>The enemy the scanner card is showing, if any.</summary>
    public EnemyRuntime? Scanned => _scanned?.Runtime;

    public int Tracked => _entries.Count;

    /// <summary>Whether this enemy shows a plate now (self-test).</summary>
    public bool HasPlate(EnemyRuntime r)
    {
        foreach (var e in _entries) if (ReferenceEquals(e.Runtime, r)) return e.Plate != null && !e.Plate.Hidden;
        return false;
    }

    public int Visible
    {
        get
        {
            int n = 0;
            foreach (var p in _all) if (p.Bound && !p.Hidden) n++;
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
        Card.Build(layer);
    }

    private static Plate BuildPlate()
    {
        var p = new Plate();
        p.Root = SContainer.TopLeft(0, 0).Size(PlateW, 74).Background(HudKit.C("#0A0D12AA"), EBackground.Round10);
        p.Name = HudKit.Label("", 22, UiTheme.Text, TextAlignmentOptions.Center).TopLeft(36, 4).Size(PlateW - 72, 28);
        p.Root.Add(p.Name);
        // Long personal names shrink instead of spilling out of the plate.
        p.Name.TextObject.enableAutoSizing = true;
        p.Name.TextObject.fontSizeMin = 13;
        p.Name.TextObject.fontSizeMax = 22;
        p.Name.TextObject.enableWordWrapping = false;
        p.Name.TextObject.overflowMode = TextOverflowModes.Ellipsis;
        // Danger: tally marks scratched in at the left of the name row, the last one struck across.
        for (int i = 0; i < Marks; i++)
        {
            var mark = i == Marks - 1
                ? SContainer.TopLeft(9, 17).Size(24, 3)
                : SContainer.TopLeft(11 + i * 5, 9 + (i % 2)).Size(3, 17);
            mark.Background(HudKit.C("#FFFFFF"), EBackground.None);
            p.Root.Add(mark);
            p.Marks[i] = mark.Root.GetComponent<Image>();
            mark.RectTransform.localRotation = Quaternion.Euler(0, 0, i == Marks - 1 ? 28 : -6);
        }
        p.BarBack = SContainer.TopLeft(20, 36).Size(PlateW - 40, 12).Background(HudKit.C(UiTheme.BarBack), EBackground.None);
        p.Root.Add(p.BarBack);
        p.Fill = SContainer.Background(HudKit.C("#C0392B"), EBackground.None);
        p.BarBack.Add(p.Fill);
        p.Affix = HudKit.Label("", 15, UiTheme.TextDim, TextAlignmentOptions.Center).TopLeft(8, 51).Size(PlateW - 16, 20);
        p.Root.Add(p.Affix);
        // Long affix lists shrink, then end in "...", so the line never leaves the plate (COS-54).
        p.Affix.TextObject.enableAutoSizing = true;
        p.Affix.TextObject.fontSizeMin = 11;
        p.Affix.TextObject.fontSizeMax = 15;
        p.Affix.TextObject.enableWordWrapping = false;
        p.Affix.TextObject.overflowMode = TextOverflowModes.Ellipsis;

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
        if (sim == null) return;
        Add(sim);
        if (_bySim.TryGetValue(sim.Pointer, out var e)) e.LastHit = _time;
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
        _focus = _scanned = null;
        _forced = IntPtr.Zero;
        TestCrosshair = null;
        Card.Hide();
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
        int n = 0;

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
            if (e.Runtime.TestActive == false) { e.Why = "test-inactive"; Release(e); continue; } // switched off between self-test phases
            // A boss with the boss bar needs no plate: the bar shows its name, level, health and affixes (Max 2026-11-21).
            if (EnemyHud.EnemyHudFeature.Instance?.HasBar(e.Runtime) == true) { e.Why = "boss bar"; Release(e); continue; }
            if (_time < e.NextRangeCheck) continue; // far away at the last look (no plate)
            var pos = e.Sim._position;

            var profile = e.Runtime.Profile;
            float scale = Mathf.Max(0.5f, (float)profile.Scale);
            Vector3 head = pos + new Vector3(0, 2.1f * scale, 0);
            float range = profile.Tier == EnemyTier.Normal ? 40f : profile.Tier == EnemyTier.Elite ? 60f : 90f;
            float sqr = (head - camPos).sqrMagnitude;
            if (sqr > range * range)
            {
                e.Why = "range";
                Release(e);
                // Well beyond range: skip it for a while (staggered so the checks spread over frames). 30 m of margin covers
                // anything the player can move in that time.
                float far = Mathf.Sqrt(sqr) - range;
                if (far > 30f) e.NextRangeCheck = _time + Mathf.Min(2f, 0.4f + far / 200f) + (e.Ptr.ToInt64() & 15) * 0.01f;
                continue;
            }

            Vector3 sp = cam!.WorldToScreenPoint(head);
            if (sp.z <= 0.1f || sp.x < -40 || sp.x > screenW + 40 || sp.y < -40 || sp.y > screenH + 40) { e.Why = "offscreen"; Release(e); continue; }

            if (_time >= e.NextOcclusion)
            {
                e.NextOcclusion = _time + OcclusionInterval + (e.Ptr.ToInt64() & 7) * 0.01f;
                e.Occluded = IsOccluded(camPos, head);
            }
            if (e.Occluded) { e.Why = "occluded"; Release(e); continue; }
            e.Why = "ok";

            if (n == _candidates.Length) Array.Resize(ref _candidates, n * 2); // grows once to the crowd size, then reused
            float dist = Mathf.Sqrt(sqr);
            Vector3 feet = cam.WorldToScreenPoint(pos);
            // Tiers count as metres closer, so a far elite never hides the plate of a normal enemy next to the player.
            float tierBonus = profile.Tier switch { EnemyTier.Boss => 25, EnemyTier.Miniboss => 18, EnemyTier.Elite => 10, _ => 0 };
            _candidates[n++] = new Candidate
            {
                Entry = e,
                Pos = new Vector2(sp.x * k, sp.y * k),
                Chest = new Vector2((sp.x + feet.x) * 0.5f, feet.y + (sp.y - feet.y) * 0.55f),
                HalfHeight = Mathf.Abs(sp.y - feet.y) * 0.5f,
                Dist = dist,
                Range = range,
                // The looked-at enemy always keeps its plate; then recently hit, then near.
                Priority = tierBonus + (_time - e.LastHit < HitPriorityTime ? 1000 : 0) + (e == _focus || e == _scanned || e == ForcedFocus ? 5000 : 0) - dist,
            };
        }

        // Highest priority first: recently hit, then the nearest (elites, minibosses and bosses count as 10/18/25 m closer).
        Array.Sort(_candidates, 0, n, PriorityOrder);
        int placed = 0;
        Entry? look = null;
        float lookScore = float.MaxValue;
        Vector2 centre = TestCrosshair ?? new Vector2(screenW * 0.5f, screenH * 0.5f);
        int playerLevel = PlayerState.Character.Progress.Level;
        int bars = EnemyHud.EnemyHudFeature.Instance?.BarRects(_barRects) ?? 0;
        for (int i = 0; i < n; i++)
        {
            var c = _candidates[i];
            var e = c.Entry;
            _candidates[i].Entry = null!;
            if (placed >= MaxPlates) { e.Why = "crowd"; Release(e); continue; }

            var profile = e.Runtime!.Profile;
            var plate = e.Plate;
            if (plate == null)
            {
                if (_free.Count == 0) { e.Why = "pool"; continue; }
                plate = _free[_free.Count - 1];
                _free.RemoveAt(_free.Count - 1);
                e.Plate = plate;
                Bind(plate, profile);
            }

            e.Health ??= EnemyRegistry.Health(e.Stats!);
            float frac = e.Health == null ? 1f : Mathf.Clamp01(e.Health._currentValue / (float)profile.MaxHealth);
            if (!Mathf.Approximately(frac, plate.Fraction))
            {
                plate.Fraction = frac;
                HudKit.SetFill(plate.Fill, frac);
            }
            if (plate.DangerForLevel != playerLevel) SetDanger(plate, profile, playerLevel);
            SetAffixShown(plate, placed < MaxAffixLines);

            // Look-at: the crosshair is on the body (a box around its middle, never smaller than 30 px).
            if (c.Dist <= ScanRange)
            {
                float dx = Mathf.Abs(c.Chest.x - centre.x), dy = Mathf.Abs(c.Chest.y - centre.y);
                float bx = Mathf.Max(30f, c.HalfHeight * 0.45f), by = Mathf.Max(30f, c.HalfHeight * 1.05f);
                if (dx <= bx && dy <= by)
                {
                    float score = dx / bx + dy / by + c.Dist * 0.01f;
                    if (score < lookScore) { lookScore = score; look = e; }
                }
            }
            float s = Mathf.Clamp(16f / Mathf.Max(c.Dist, 1f), 0.65f, 1f) * plate.Scale;
            plate.Rt.localScale = new Vector3(s, s, 1);

            // A plate that would cover one placed before it (higher priority) or the boss bar fades out instead of stacking
            // upward (Max 2026-11-21: stacks crowded the top of the screen); the looked-at enemy's plate was placed first.
            float w = PlateW * s, h = plate.Rt.sizeDelta.y * s;
            var rect = new Rect(c.Pos.x - w / 2, c.Pos.y, w, h);
            bool covered = false;
            for (int j = 0; j < placed && !covered; j++) covered = rect.Overlaps(_placed[j]);
            for (int j = 0; j < bars && !covered; j++) covered = rect.Overlaps(_barRects[j]);
            plate.Hidden = covered;
            plate.Shown = Mathf.MoveTowards(plate.Shown, covered ? 0 : 1, dt * 6f);
            plate.Group.alpha = Mathf.Clamp01((c.Range - c.Dist) / (c.Range * 0.3f)) * plate.Shown;
            plate.Last = rect;
            plate.Rt.anchoredPosition = new Vector2(rect.x + w / 2, rect.y);
            if (!covered) _placed[placed++] = rect;
        }
        var forced = ForcedFocus;
        UpdateScan(forced != null && forced.Plate != null ? forced : look, dt);
    }

    /// <summary>Self-tests only: treat this enemy as looked at (the headless camera does not aim). Null = the real look-at test.</summary>
    private Entry? ForcedFocus => _forced != IntPtr.Zero && _bySim.TryGetValue(_forced, out var e) ? e : null;
    private IntPtr _forced;

    /// <summary>Self-tests only: where the crosshair is, in screen pixels (the headless camera cannot be aimed). Null = screen centre.</summary>
    public Vector2? TestCrosshair { get; set; }

    public void ForceFocus(VailActor? actor) => _forced = actor?.GetWorldSimActor()?.Pointer ?? IntPtr.Zero;

    /// <summary>The enemy under the crosshair this frame (before the scanner delay), for the self-test.</summary>
    public EnemyRuntime? LookedAt => _focus?.Runtime;

    /// <summary>The scanner opens after the crosshair rests on an enemy for <see cref="FocusDelay"/> and lingers a moment after it leaves.</summary>
    private void UpdateScan(Entry? look, float dt)
    {
        if (!Config.ShowScanner.Value) look = null;
        if (look != _focus) { _focus = look; _focusHeld = 0; }
        else if (look != null) _focusHeld += dt;
        if (look != null && _focusHeld >= FocusDelay) _scanned = look;
        if (look != null && look == _scanned) _focusLastSeen = _time;
        if (_scanned != null && (_scanned.Plate == null || _scanned.Runtime == null || (_scanned != look && _time - _focusLastSeen > FocusLinger)))
            _scanned = null;
        Card.Update(_scanned?.Runtime, _scanned?.Plate?.Last ?? default, dt);
    }

    private static void SetDanger(Plate p, EnemyProfile profile, int playerLevel)
    {
        p.DangerForLevel = playerLevel;
        p.Danger = EnemyInfo.Danger(profile.Tier, profile.Level, playerLevel);
        int lit = (int)p.Danger + 1;
        var on = HudKit.C(HudText.DangerHex(p.Danger));
        var off = HudKit.C("#FFFFFF18");
        for (int i = 0; i < Marks; i++) p.Marks[i].color = i < lit ? on : off;
    }

    private static void SetAffixShown(Plate p, bool allowed)
    {
        bool show = allowed && p.AffixText.Length > 0;
        if (show == p.AffixShown) return;
        p.AffixShown = show;
        p.Affix.Active(show);
        p.Rt.sizeDelta = new Vector2(PlateW, show ? 74 : 54);
    }

    /// <summary>Number of pairs of drawn plates whose screen rectangles overlap (self-test, COS-54).</summary>
    public int OverlappingPairs()
    {
        int pairs = 0;
        for (int i = 0; i < _all.Count; i++)
        {
            if (!_all[i].Bound || _all[i].Hidden) continue;
            for (int j = i + 1; j < _all.Count; j++)
                if (_all[j].Bound && !_all[j].Hidden && _all[i].Last.Overlaps(_all[j].Last)) pairs++;
        }
        return pairs;
    }

    /// <summary>Drawn plates that show an affix line.</summary>
    public int AffixLinesShown
    {
        get
        {
            int n = 0;
            foreach (var p in _all) if (p.Bound && !p.Hidden && p.AffixShown) n++;
            return n;
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
        // Named elites show their own name; kind and tier are in the scanner card (and the color says the tier).
        string title = HudText.PersonalName(profile) ?? HudText.KindTitle(profile);
        p.NameText = $"{title}  <color=#9AA3AF>{HudText.Level} {profile.Level}</color>";
        p.Name.Text(p.NameText).FontColor(HudKit.TierHex(profile.Tier));
        p.AffixText = HudText.Affixes(profile.Abilities);
        p.Affix.Text(p.AffixText);
        p.AffixShown = !(p.AffixText.Length > 0); // force SetAffixShown to apply
        SetAffixShown(p, true);
        p.FillImage.color = HudKit.C(HudKit.TierBarHex(profile.Tier));
        p.Scale = profile.Tier == EnemyTier.Boss ? 1.3f : profile.Tier == EnemyTier.Miniboss ? 1.15f : 1f;
        p.Fraction = -1;
        p.DangerForLevel = -1;
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
        if (p == null) return new PlateInfo(false, e.Why, e.Runtime?.Profile.Level ?? 0, e.Runtime?.Profile.Tier ?? EnemyTier.Normal, 0, "", DangerRating.Trivial);
        return new PlateInfo(p.Bound && p.Root.Root.activeSelf, p.NameText, p.Level, p.Tier, p.Fraction, p.AffixText, p.Danger);
    }
}
