using ChampionsOfTheSons.Mod.Spells;
using TheForest.Utils;
using UnityEngine;

namespace ChampionsOfTheSons.Mod.Visuals;

/// <summary>
/// Effects that last as long as a toggle or buff (COS-102): Black Flame burns and Multishot turns a glyph at the
/// weapon hand (first person: a fixed spot low right of the camera); Berserk and Golden Skin glow on the body (COS-181).
/// Hand effects are decorative and hidden under "reduce heavy effects".
/// </summary>
internal static class PlayerStateFx
{
    private sealed class State
    {
        public string Id = "";
        public Func<bool> IsOn = null!;
        public Func<GameObject?> Make = null!;
        public GameObject? Go;
    }

    private static readonly Vector3 HandOffset = new(0.3f, -0.32f, 0.6f);
    private static readonly Color Gold = new(1f, 0.8f, 0.3f);
    private static readonly Color Violet = new(0.55f, 0.2f, 1f);

    private static readonly List<State> States = new()
    {
        // Black Flame has no hand flame (Max, 2026-11-24): its field of flames and the flame around the caster show it.
        new State { Id = "multishot", IsOn = () => ToggleOn("spell.multishot"), Make = () => HandGlyph() },
        // COS-181: self buffs glow on the body (plus a body tint, StateTints), no ring at the feet.
        new State { Id = "berserk", IsOn = () => SpellWorld.Buffs.Has("berserk"), Make = () => OnBody(SpellFx.Blood) },
        new State { Id = "golden_skin", IsOn = () => SpellWorld.Buffs.Has("golden_skin"), Make = () => OnBody(Gold) },
    };

    private static bool _reduced;

    public static void Update()
    {
        if (!FxKit.Available || LocalPlayer.Transform == null) return;
        // Re-make everything when the setting flips, so hidden hand effects come back (or go).
        if (_reduced != FxKit.Reduced)
        {
            _reduced = FxKit.Reduced;
            Clear();
        }
        foreach (var s in States)
        {
            bool on;
            try { on = s.IsOn(); }
            catch { on = false; }
            if (on && s.Go == null) s.Go = s.Make();
            else if (!on && s.Go != null)
            {
                UnityEngine.Object.Destroy(s.Go);
                s.Go = null;
            }
        }
    }

    public static void Clear()
    {
        foreach (var s in States)
        {
            if (s.Go != null) UnityEngine.Object.Destroy(s.Go);
            s.Go = null;
        }
    }

    public static bool ToggleOn(string spellId)
    {
        var book = PlayerState.Character.Spells;
        for (int i = 0; i < book.Loadout.Count; i++)
            if (book.Loadout[i] == spellId && book.IsActive(i)) return true;
        return false;
    }

    private static Transform? Cam => LocalPlayer.MainCam != null ? LocalPlayer.MainCam.transform : null;

    private static GameObject? AtHand(string prefab, float radius, Color color)
    {
        var cam = Cam;
        if (cam == null) return null;
        var go = FxKit.Spawn(prefab, cam.TransformPoint(HandOffset), radius, color, cam);
        if (go != null) FxKit.AddLight(go, color, 3, 8);
        return go;
    }

    private static GameObject? HandGlyph()
    {
        var go = AtHand(FxKit.Glyph, 0.14f, SpellFx.Arcane);
        // The glyph lies flat in its prefab; stand it up facing away from the camera.
        if (go != null && Cam != null) go.transform.rotation = Cam.rotation * Quaternion.Euler(-90, 0, 0);
        return go;
    }

    private static GameObject? OnBody(Color color)
    {
        var t = LocalPlayer.Transform;
        return t == null ? null : BodyAura.On(t, color);
    }
}
