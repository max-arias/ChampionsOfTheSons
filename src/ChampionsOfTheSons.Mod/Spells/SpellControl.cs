using ChampionsOfTheSons.Core.Rules;
using RedLoader;
using SonsSdk;
using TheForest.Utils;
using UnityEngine;
using static ChampionsOfTheSons.Mod.Spells.SpellWorld;

namespace ChampionsOfTheSons.Mod.Spells;

/// <summary>
/// COS-82: reads each spell slot's key and pad binding every frame and turns key down / held / up into casts through
/// <see cref="SpellInput"/>: instant spells cast on press (and again while held once ready), aimed spells show an aim aid while held and
/// cast on release, channels run while held, toggles flip on press. Silence blocks casting and ends a running channel.
/// </summary>
internal static class SpellControl
{
    public static readonly SpellInput Input = new();

    /// <summary>Self-tests hold a slot's key with this instead of the keyboard.</summary>
    public static readonly bool[] TestHeld = new bool[SpellBook.Slots];

    /// <summary>Outcome of the last cast this layer started (self-tests).</summary>
    public static SpellCastOutcome LastOutcome { get; private set; }

    private static GameObject? _sphere, _line, _dot;

    public static void Update(bool inWorld)
    {
        var pc = PlayerState.Character;
        bool controllable = inWorld && LocalPlayer.IsInWorld && Controllable();
        bool silenced = pc.Stats.IsOn(StatIds.Silenced);
        for (int slot = 0; slot < SpellBook.Slots; slot++)
        {
            if (pc.Spells.Loadout[slot] is not string id || !ModContent.Db.Spells.TryGetValue(id, out var def))
            {
                Input.Step(slot, false, CastMode.Instant, false, false);
                Input.Cancel(slot);
                continue;
            }
            var mode = SpellInput.ModeOf(def);
            bool running = pc.Spells.IsActive(slot);
            if (silenced && mode == CastMode.Channel && running) StopSlot(slot);
            bool held = controllable && (TestHeld[slot] || KeyHeld(slot) || ChampionsOfTheSons.Mod.Pad.GamepadFeature.SpellHeld(slot));
            switch (Input.Step(slot, held, mode, pc.Spells.IsReady(slot), running))
            {
                case SpellInputAction.Cast:
                    LastOutcome = SpellCaster.Cast(slot);
                    break;
                case SpellInputAction.StopChannel:
                    StopSlot(slot);
                    break;
            }
            if (silenced) Input.Cancel(slot);
        }
        int aiming = Input.AimingSlot;
        if (aiming >= 0 && pc.Spells.Loadout[aiming] is string aimId) ShowAim(aimId);
        else HideAim();
    }

    /// <summary>Ends a running channel or toggle in <paramref name="slot"/> (key released, silence).</summary>
    public static void StopSlot(int slot)
    {
        var book = PlayerState.Character.Spells;
        if (!book.IsActive(slot)) return;
        book.Stop(slot);
        SpellEffects.Stopped(slot);
    }

    public static void Reset()
    {
        Input.Reset();
        Array.Clear(TestHeld);
        HideAim();
    }

    private static bool Controllable()
    {
        try { return GameState.IsPlayerControllable; } catch { return false; }
    }

    private static bool KeyHeld(int slot)
    {
        try
        {
            var entry = Config.SpellKeys[slot];
            return entry != null && entry.GetAction().IsPressed() && Settings.ModKeys.ModifiersOk(entry.Identifier);
        }
        catch
        {
            return false;
        }
    }

    // ---------------------------------------------------------------- aim aids

    /// <summary>Where an aimed spell will land and how big it is, as its effect will compute it on release; null when it has no aid.</summary>
    public static (Vector3 From, Vector3 At, float Radius, bool Line)? AimOf(string spellId)
    {
        var eye = Eye;
        switch (spellId)
        {
            case "spell.black_hole": return (eye.position, AimGround((float)SpellTuning.BlackHoleRange) + Vector3.up * 1.5f, S("spell_blackhole_radius"), false);
            case "spell.cataclysm": return (eye.position, PlayerPos + Vector3.up * 0.1f, S("spell_cataclysm_radius"), false);
            case "spell.healing_dome": return (eye.position, (PlayerPos + Vector3.up * 0.1f), (float)SpellTuning.HealingDomeRadius, false);
            case "spell.magic_arrow": return (eye.position, AimPoint((float)SpellTuning.MagicArrowRange), 1f, false);
            case "spell.taunt": return (eye.position, (PlayerPos + Vector3.up * 0.1f), 30f, false);
            case "spell.portal":
            {
                var b = AimGround((float)SpellTuning.PortalMaxRange);
                return (PlayerPos + Vector3.up, b, 1.2f, true);
            }
            case "spell.blink":
            {
                var player = LocalPlayer.Transform;
                var stats = PlayerState.Character.Stats;
                float range = (float)(stats.Get(PerkStatIds.BlinkRange) > 0 ? stats.Get(PerkStatIds.BlinkRange) : SpellCaster.BlinkRange);
                var feet = SpellCaster.BlinkTarget(eye.position, eye.forward, range, eye.position.y - player.position.y);
                return (player.position + Vector3.up, feet, 0.6f, true);
            }
            default: return null;
        }
    }

    /// <summary>The aim aid on screen now (self-tests): its target and radius.</summary>
    public static (Vector3 At, float Radius)? Shown { get; private set; }

    private static void ShowAim(string spellId)
    {
        (Vector3 From, Vector3 At, float Radius, bool Line)? aim;
        try { aim = AimOf(spellId); }
        catch (Exception e) { RLog.Warning($"Champions of the Sons: aim aid for {spellId} failed: {e.Message}"); aim = null; }
        if (aim is not { } a) { HideAim(); return; }
        Shown = (a.At, a.Radius);
        var color = new Color(0.75f, 0.55f, 1f, 0.18f);
        if (a.Line)
        {
            _line ??= MakeLine();
            var lr = _line.GetComponent<LineRenderer>();
            lr.SetPosition(0, a.From);
            lr.SetPosition(1, a.At);
            _dot ??= MakeMarker(PrimitiveType.Sphere, color);
            _dot.transform.position = a.At;
            _dot.transform.localScale = Vector3.one * a.Radius * 2;
            Hide(ref _sphere);
        }
        else
        {
            _sphere ??= MakeMarker(PrimitiveType.Sphere, color);
            _sphere.transform.position = a.At;
            _sphere.transform.localScale = Vector3.one * a.Radius * 2;
            Hide(ref _line);
            Hide(ref _dot);
        }
    }

    private static void HideAim()
    {
        Shown = null;
        Hide(ref _sphere);
        Hide(ref _line);
        Hide(ref _dot);
    }

    private static void Hide(ref GameObject? go)
    {
        if (go != null) UnityEngine.Object.Destroy(go);
        go = null;
    }

    private static GameObject MakeMarker(PrimitiveType type, Color color)
    {
        var go = GameObject.CreatePrimitive(type);
        go.name = "cots-spell-aim";
        var col = go.GetComponent<Collider>();
        if (col != null) UnityEngine.Object.Destroy(col);
        var rend = go.GetComponent<Renderer>();
        if (rend != null && SpellFx.FindUnlit() is { } shader)
        {
            var mat = new Material(shader);
            mat.SetColor("_UnlitColor", color);
            mat.color = color;
            MakeTransparent(mat);
            rend.material = mat;
            rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
        return go;
    }

    /// <summary>HDRP Unlit as a see-through surface (the aim aid must not hide what it marks).</summary>
    private static void MakeTransparent(Material mat)
    {
        mat.SetFloat("_SurfaceType", 1);
        mat.SetFloat("_BlendMode", 0);
        mat.SetFloat("_ZWrite", 0);
        mat.SetFloat("_TransparentZWrite", 0);
        mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        mat.EnableKeyword("_BLENDMODE_ALPHA");
        mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
    }

    private static GameObject MakeLine()
    {
        var go = new GameObject("cots-spell-aim-line");
        var lr = go.AddComponent<LineRenderer>();
        lr.positionCount = 2;
        lr.startWidth = 0.06f;
        lr.endWidth = 0.06f;
        lr.useWorldSpace = true;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        if (SpellFx.FindUnlit() is { } shader)
        {
            var mat = new Material(shader);
            var c = new Color(0.8f, 0.6f, 1f, 0.6f);
            mat.SetColor("_UnlitColor", c);
            mat.color = c;
            MakeTransparent(mat);
            lr.material = mat;
        }
        return go;
    }
}
