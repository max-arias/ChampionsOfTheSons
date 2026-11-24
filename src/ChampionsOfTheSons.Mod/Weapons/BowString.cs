using Sons.Weapon;
using UnityEngine;

namespace ChampionsOfTheSons.Mod.Weapons;

/// <summary>
/// The string of a mod bow (COS-205). The bow model has none, and the game's own bow (whose string its animation pulls) is
/// hidden under ours. Ours is two thin rods from the limb tips to a nock point: at rest the nock lies on the straight
/// line between the tips; while the game's bow is charging it follows the drawing hand (whichever hand is away from the
/// grip), and it snaps back on release.
/// </summary>
internal sealed class BowString
{
    /// <summary>The string leaves the limbs this far in from their ends and from the string-side edge, as fractions of the model.</summary>
    private const float TipInset = 0.02f, EdgeInset = 0.08f;
    private const float Width = 0.006f;
    /// <summary>The nock moves to the hand at this rate per second, and back to rest at the faster one.</summary>
    private const float PullRate = 18f, ReleaseRate = 60f;
    /// <summary>The drawing hand must be this far (metres) behind the resting string before the string follows it.</summary>
    private const float MinPull = 0.06f;

    private readonly Transform _model;
    private readonly Transform _upper, _lower;
    private readonly Vector3 _top, _bottom, _rest;
    private readonly Vector3 _back;
    private Vector3 _nock;
    private RangedWeaponController? _bow;
    private Transform? _left, _right;
    private bool _looked;

    /// <summary>How far the nock is pulled from rest, in metres (self-test).</summary>
    public float Pull { get; private set; }

    /// <summary>The furthest the nock has been pulled since this was last set to 0 (self-test).</summary>
    public float MaxPull { get; set; }

    public bool Charging { get; private set; }

    /// <summary>How far each hand was behind the resting string when last charging, in metres (self-test).</summary>
    public float LeftBehind { get; private set; }
    public float RightBehind { get; private set; }

    private static Shader? _unlit;

    /// <summary>Adds the string to a bow model. <paramref name="bounds"/>: the model's bounds in its own space;
    /// <paramref name="gripDepth"/>: the type's grip depth, whose sign says which side the grip is on (the string is on the other).</summary>
    public static BowString? Create(GameObject model, Bounds bounds, double gripDepth)
    {
        try
        {
            int limb = bounds.size.x >= bounds.size.y ? 0 : 1, other = limb == 0 ? 1 : 0;
            int depth = bounds.size[other] >= bounds.size.z ? other : 2;
            float side = gripDepth > 0 ? -1 : 1;
            Vector3 top = bounds.center, bottom = bounds.center, back = Vector3.zero;
            top[limb] = bounds.max[limb] - TipInset * bounds.size[limb];
            bottom[limb] = bounds.min[limb] + TipInset * bounds.size[limb];
            top[depth] = bottom[depth] = bounds.center[depth] + side * (0.5f - EdgeInset) * bounds.size[depth];
            back[depth] = side;

            _unlit ??= Shader.Find("HDRP/Unlit") ?? Shader.Find("Unlit/Color");
            Material? mat = null;
            if (_unlit != null)
            {
                mat = new Material(_unlit);
                var color = new Color(0.82f, 0.76f, 0.62f, 1f);
                mat.SetColor("_UnlitColor", color);
                mat.color = color;
            }
            int layer = model.GetComponentInChildren<Renderer>(true) is { } first ? first.gameObject.layer : model.layer;
            Transform Rod(string name)
            {
                var rod = GameObject.CreatePrimitive(PrimitiveType.Cube);
                UnityEngine.Object.Destroy(rod.GetComponent<Collider>());
                rod.name = name; // "cots-": never hidden with the game's renderers
                rod.layer = layer;
                rod.transform.SetParent(model.transform, false);
                var r = rod.GetComponent<Renderer>();
                if (mat != null) r.sharedMaterial = mat;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                return rod.transform;
            }
            return new BowString(model.transform, Rod("cots-bow-string-upper"), Rod("cots-bow-string-lower"), top, bottom, back);
        }
        catch (Exception e)
        {
            RedLoader.RLog.Error($"Champions of the Sons: bow string failed: {e}");
            return null;
        }
    }

    private BowString(Transform model, Transform upper, Transform lower, Vector3 top, Vector3 bottom, Vector3 back)
    {
        _model = model;
        _upper = upper;
        _lower = lower;
        _top = top;
        _bottom = bottom;
        _back = back;
        _rest = _nock = (top + bottom) / 2;
        Draw();
    }

    /// <summary>Per frame, after the body's animation: the nock follows the drawing hand while the bow charges.</summary>
    public void Tick(Transform holder)
    {
        if (_model == null || _upper == null || _lower == null) return;
        if (!_looked)
        {
            _looked = true;
            _bow = holder.GetComponentInChildren<RangedWeaponController>(true);
            var body = holder.GetComponentInParent<Animator>();
            if (body != null)
                foreach (var t in body.GetComponentsInChildren<Transform>(true))
                {
                    if (t.name == "LeftHandMiddle1") _left = t;
                    else if (t.name == "RightHandMiddle1") _right = t;
                }
        }
        Charging = _bow != null && _bow._isCharging;
        var target = _rest;
        if (Charging)
        {
            // The drawing hand is the one away from the grip; it pulls the nock only once it is behind the resting string.
            Vector3? hand = null;
            float far = 0;
            foreach (var h in new[] { _left, _right })
            {
                if (h == null) continue;
                var p = _model.InverseTransformPoint(h.position);
                float behind = Vector3.Dot(p - _rest, _back) * _model.lossyScale.x;
                if (h == _left) LeftBehind = behind; else RightBehind = behind;
                if (behind > MinPull && behind > far) { far = behind; hand = p; }
            }
            if (hand != null) target = hand.Value;
        }
        float rate = target == _rest ? ReleaseRate : PullRate;
        _nock = Vector3.MoveTowards(_nock, target, rate * Time.deltaTime / Mathf.Max(1e-4f, _model.lossyScale.x));
        Pull = (_nock - _rest).magnitude * _model.lossyScale.x;
        if (Pull > MaxPull) MaxPull = Pull;
        Draw();
    }

    /// <summary>Stretches a rod from one point to another (model space).</summary>
    private void Lay(Transform rod, Vector3 from, Vector3 to)
    {
        var d = to - from;
        float len = d.magnitude;
        if (len < 1e-5f) return;
        float w = Width / Mathf.Max(1e-4f, _model.lossyScale.x);
        rod.localPosition = (from + to) / 2;
        rod.localRotation = Quaternion.LookRotation(d / len);
        rod.localScale = new Vector3(w, w, len);
    }

    private void Draw()
    {
        Lay(_upper, _top, _nock);
        Lay(_lower, _nock, _bottom);
    }
}
