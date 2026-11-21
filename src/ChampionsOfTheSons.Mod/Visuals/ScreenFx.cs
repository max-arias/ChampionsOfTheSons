using Il2CppInterop.Runtime;
using RedLoader;
using SonsSdk;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace ChampionsOfTheSons.Mod.Visuals;

/// <summary>
/// Local full-screen moments (COS-141): a global HDRP volume with a vignette and desaturation whose weight is ramped by
/// code, plus a duck of the game's sound (FMOD master bus; the mod's own sounds play on the core system and are not
/// ducked). Never touches <c>Time.timeScale</c>, so co-op is unaffected. One moment at a time; a new one replaces the old.
/// </summary>
internal static class ScreenFx
{
    private const float FadeIn = 0.06f, FadeOut = 0.18f, Duck = 0.35f;

    private static Volume? _volume;
    private static float _hold, _fade, _strength;
    private static float? _busBefore;
    private static bool _broken;

    /// <summary>Weight of the volume now (self-test).</summary>
    public static float Weight => _volume != null ? _volume.weight : 0;

    /// <summary>True while the game's sound is ducked (self-test).</summary>
    public static bool Ducked => _busBefore != null;

    /// <summary>Highest weight and whether the sound was ducked during the last beat (self-test).</summary>
    public static float LastPeak { get; private set; }
    public static bool LastDucked { get; private set; }

    /// <summary>Starts a beat: in fast, held for <paramref name="seconds"/>, out. <paramref name="strength"/> 0..1.</summary>
    public static void Beat(float seconds, float strength = 1f)
    {
        if (!Ensure()) return;
        _hold = seconds;
        _fade = 0;
        _strength = Mathf.Clamp01(FxKit.Reduced ? strength * 0.5f : strength);
        LastPeak = 0;
        DuckSound(true);
        LastDucked = Ducked;
    }

    /// <summary>Ends the hold early (the payoff), leaving the fade-out.</summary>
    public static void Release()
    {
        _hold = 0;
        DuckSound(false);
    }

    public static void Update(float dt)
    {
        if (_volume == null) return;
        if (_hold > 0)
        {
            _hold -= dt;
            _fade = Mathf.Min(1, _fade + dt / FadeIn);
            if (_hold <= 0) DuckSound(false);
        }
        else if (_fade > 0) _fade = Mathf.Max(0, _fade - dt / FadeOut);
        _volume.weight = _fade * _strength;
        LastPeak = Mathf.Max(LastPeak, _volume.weight);
    }

    /// <summary>Leaving the world or a cancelled cast: no effect, sound back.</summary>
    public static void Clear()
    {
        _hold = 0;
        _fade = 0;
        if (_volume != null) _volume.weight = 0;
        DuckSound(false);
    }

    private static bool Ensure()
    {
        if (_volume != null) return true;
        if (_broken) return false;
        try
        {
            var go = new GameObject("cots-screenfx");
            UnityEngine.Object.DontDestroyOnLoad(go);
            _volume = go.AddComponent<Volume>();
            _volume.isGlobal = true;
            _volume.priority = 500;
            _volume.weight = 0;
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            var vignette = profile.Add(Il2CppType.Of<Vignette>(), true).Cast<Vignette>();
            vignette.intensity.value = 0.42f;
            vignette.intensity.overrideState = true;
            vignette.smoothness.value = 0.6f;
            vignette.smoothness.overrideState = true;
            var color = profile.Add(Il2CppType.Of<ColorAdjustments>(), true).Cast<ColorAdjustments>();
            color.saturation.value = -55f;
            color.saturation.overrideState = true;
            _volume.sharedProfile = profile;
            return true;
        }
        catch (Exception e)
        {
            _broken = true;
            _volume = null;
            RLog.Warning($"[COTS] screen effect unavailable: {e.Message}");
            return false;
        }
    }

    private static void DuckSound(bool on)
    {
        try
        {
            var bus = SoundTools.MasterBus;
            if (on && _busBefore == null)
            {
                bus.getVolume(out float v);
                _busBefore = v;
                bus.setVolume(v * Duck);
            }
            else if (!on && _busBefore is { } before)
            {
                bus.setVolume(before);
                _busBefore = null;
            }
        }
        catch
        {
            _busBefore = null; // the bus is not there (dedicated server, early load): no duck
        }
    }
}
