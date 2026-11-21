using ChampionsOfTheSons.Core.Content;
using TheForest.Utils;
using UnityEngine;

namespace ChampionsOfTheSons.Mod.Weapons;

/// <summary>
/// A bow type's draw speed on the draw animation itself (COS-118; greatbow: 0.7). The player animator has a bow speed
/// parameter (<c>AnimationHashes.bowSpeedHash</c>); while a bow type with a draw speed other than 1 is held, it is kept at
/// the game's own value times that speed. The game may write the parameter too: a value that differs from the one set
/// last frame is taken as the game's new base. The time between shots is lengthened separately (StatBridge).
/// </summary>
internal static class BowDraw
{
    private static float? _base;
    private static float _lastSet = float.NaN;

    /// <summary>The bow speed parameter's value now and the game's own value under it (self-test).</summary>
    public static float? Current => Animator() is { } a && Has(a) ? a.GetFloat(Hash) : null;
    public static float? Base => _base;

    private static int Hash => Sons.Animation.AnimationHashes.bowSpeedHash;

    public static void Tick(WeaponTypeDef? active)
    {
        var a = Animator();
        if (a == null || !Has(a)) return;
        float speed = active?.Base == WeaponBase.Bow ? (float)active.DrawSpeed : 1f;
        float cur = a.GetFloat(Hash);
        if (Mathf.Abs(speed - 1f) < 1e-4f)
        {
            if (_base is { } b) a.SetFloat(Hash, b); // back to the game's value once
            _base = null;
            _lastSet = float.NaN;
            return;
        }
        if (_base == null || Mathf.Abs(cur - _lastSet) > 1e-4f) _base = cur;
        _lastSet = _base.Value * speed;
        a.SetFloat(Hash, _lastSet);
    }

    private static Animator? Animator()
    {
        try { return LocalPlayer.Animator; } catch { return null; }
    }

    private static IntPtr _checked;
    private static bool _has;

    /// <summary>Whether this animator has the parameter (looked up once per animator: the parameter list allocates).</summary>
    private static bool Has(Animator a)
    {
        if (a.Pointer == _checked) return _has;
        _checked = a.Pointer;
        _has = false;
        foreach (var p in a.parameters)
            if (p.nameHash == Hash) _has = true;
        return _has;
    }
}
