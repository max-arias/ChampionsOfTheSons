using SUI;
using TMPro;
using UnityEngine;

namespace ChampionsOfTheSons.Mod.Hud;

/// <summary>
/// Floating damage numbers: a fixed pool of labels. A hit takes a free label (or recycles the oldest), sets its text once
/// and from then on only moves and fades it, so big fights allocate nothing per frame.
/// </summary>
internal sealed class DamageNumbers
{
    private const int PoolSize = 48;
    private const float Life = 1.1f, CritLife = 1.5f;
    private const float Rise = 90f, CritRise = 120f;

    private sealed class Number
    {
        public SLabelOptions Label = null!;
        public RectTransform Rt = null!;
        public CanvasGroup Group = null!;
        public TMP_Text Text = null!;
        public bool Active;
        public bool Crit;
        public float Age;
        public Vector3 World;
        public float DriftX;
    }

    private readonly Number[] _pool = new Number[PoolSize];
    private int _next;
    private Camera? _cam;

    /// <summary>Numbers spawned since start (self-test counter).</summary>
    public int Spawned { get; private set; }

    /// <summary>Numbers on screen right now.</summary>
    public int Active
    {
        get
        {
            int n = 0;
            foreach (var x in _pool) if (x.Active) n++;
            return n;
        }
    }

    public string LastText { get; private set; } = "";
    public bool LastCrit { get; private set; }

    public void Build(SContainerOptions layer)
    {
        for (int i = 0; i < PoolSize; i++)
        {
            var n = new Number();
            n.Label = HudKit.Label("", 30, "#FFFFFF", TextAlignmentOptions.Center).TopLeft(0, 0).Size(220, 64);
            layer.Add(n.Label);
            n.Rt = n.Label.RectTransform;
            n.Text = n.Label.TextObject;
            n.Text.fontStyle = FontStyles.Bold;
            n.Group = HudKit.Group(n.Label.Root);
            HudKit.Place(n.Rt, Vector2.zero, new Vector2(0.5f, 0), 0, 0);
            n.Label.Active(false);
            _pool[i] = n;
        }
    }

    public static readonly Color PhysicalColor = new(1f, 1f, 1f, 1f);
    public static readonly Color MagicColor = new(0.45f, 0.65f, 1f, 1f);
    public static readonly Color PureColor = new(1f, 0.45f, 0.9f, 1f);
    public static readonly Color CritColor = new(1f, 0.82f, 0.2f, 1f);
    public static readonly Color HurtColor = new(1f, 0.3f, 0.25f, 1f);
    public static readonly Color DodgeColor = new(0.7f, 0.9f, 0.7f, 1f);

    public static Color ColorOf(Core.Rules.DamageKind kind) => kind switch
    {
        Core.Rules.DamageKind.Magic => MagicColor,
        Core.Rules.DamageKind.Pure => PureColor,
        _ => PhysicalColor,
    };

    /// <summary>Color of the last number spawned (self-tests).</summary>
    public Color LastColor { get; private set; }

    public void Spawn(Vector3 world, double damage, bool crit, Core.Rules.DamageKind kind = Core.Rules.DamageKind.Physical)
    {
        string text = HudKit.Compact(damage);
        if (crit) text += "!";
        Spawn(world, text, crit ? CritColor : ColorOf(kind), crit);
    }

    public void Spawn(Vector3 world, string text, Color color, bool big)
    {
        // Free slot first, otherwise recycle round-robin (the oldest in practice).
        Number? n = null;
        for (int i = 0; i < PoolSize; i++)
        {
            var c = _pool[(_next + i) % PoolSize];
            if (c.Active) continue;
            n = c;
            _next = (_next + i + 1) % PoolSize;
            break;
        }
        if (n == null)
        {
            n = _pool[_next];
            _next = (_next + 1) % PoolSize;
        }

        n.Active = true;
        n.Crit = big;
        n.Age = 0;
        n.World = world;
        n.DriftX = (UnityEngine.Random.value - 0.5f) * 70f;
        n.Text.text = text;
        n.Text.fontSize = big ? 46 : 30;
        n.Text.color = color;
        n.Group.alpha = 0;
        n.Label.Active(true);
        Spawned++;
        LastText = text;
        LastCrit = big;
        LastColor = color;
    }

    public void Update(float dt, bool enabled)
    {
        if (_cam == null || !_cam.isActiveAndEnabled) _cam = HudKit.PlayerCamera();
        var cam = _cam;
        float k = HudKit.CanvasPerPixel;
        for (int i = 0; i < PoolSize; i++)
        {
            var n = _pool[i];
            if (!n.Active) continue;
            n.Age += dt;
            float life = n.Crit ? CritLife : Life;
            if (n.Age >= life || cam == null || !enabled)
            {
                n.Active = false;
                n.Label.Active(false);
                continue;
            }
            Vector3 sp = cam.WorldToScreenPoint(n.World);
            if (sp.z <= 0.1f)
            {
                n.Group.alpha = 0;
                continue;
            }
            float t = n.Age / life;
            float rise = (n.Crit ? CritRise : Rise) * (1 - (1 - t) * (1 - t)); // ease out
            n.Rt.anchoredPosition = new Vector2(sp.x * k + n.DriftX * t, sp.y * k + rise);
            n.Group.alpha = t < 0.6f ? 1f : 1f - (t - 0.6f) / 0.4f;
            float pop = n.Crit ? 1f + 0.5f * Mathf.Max(0, 1 - n.Age * 6f) : 1f;
            n.Rt.localScale = new Vector3(pop, pop, 1);
        }
    }

    public void Clear()
    {
        foreach (var n in _pool)
        {
            n.Active = false;
            n.Label.Active(false);
        }
    }
}
