using ChampionsOfTheSons.Core.Rules;
using ChampionsOfTheSons.Mod.Spells;
using RedLoader;
using Sons.Weapon;
using TheForest.Utils;
using UnityEngine;

namespace ChampionsOfTheSons.Mod.PerkHooks;

/// <summary>
/// Game side of the perks that react to the world every frame (COS-87): Light the Way (flashlight data), Momentum Transfer and
/// Bunny Hopping (landing burst), and the autocast Sustain Shield. Rules and numbers are in Core (<see cref="PerkRules"/>).
/// </summary>
public sealed class PerkHooksFeature : IModFeature
{
    private sealed record LightOriginal(float Intensity, float Drain);

    private static readonly Dictionary<IntPtr, LightOriginal> Lights = new();
    private static float _scan;
    private static bool _wasGrounded = true;
    private static float _airTime;

    /// <summary>The shield is running because of the autocast perk, not a cast.</summary>
    public static bool ShieldAutocasting { get; private set; }

    /// <summary>Last applied flashlight factors (self-test).</summary>
    public static (double Intensity, double Drain) AppliedLight { get; private set; } = (1, 1);

    private static PlayerCharacter Pc => PlayerState.Character;

    public void OnGameStart() => Reset();

    public void RegisterSelfTests() => PerkHooksSelfTest.Register();

    public static void Reset()
    {
        Lights.Clear();
        OwnLights.Clear();
        _lightSearch = 0;
        ShotTracker.Reset();
        ShieldAutocasting = false;
        _wasGrounded = true;
        _airTime = 0;
    }

    public void Update(float dt, bool inWorld)
    {
        if (!inWorld || LocalPlayer.Vitals == null) return;
        try
        {
            Landing(dt);
            AutocastShield(dt);
            _scan -= dt;
            if (_scan <= 0)
            {
                _scan = 0.5f;
                ApplyFlashlights();
            }
        }
        catch (Exception e)
        {
            RLog.Error($"Champions of the Sons: perk hooks failed: {e}");
        }
    }

    /// <summary>Whether a channeled or toggle spell is running in any slot.</summary>
    public static bool SpellRunning(string spellId)
    {
        var spells = Pc.Spells;
        for (int i = 0; i < SpellBook.Slots; i++)
            if (spells.Loadout[i] == spellId && spells.IsActive(i)) return true;
        return false;
    }

    // ---------------------------------------------------------------- Momentum Transfer

    private static void Landing(float dt)
    {
        var fp = LocalPlayer.FpCharacter;
        if (fp == null) return;
        bool grounded = fp.Grounded;
        if (!grounded) _airTime += dt;
        else if (!_wasGrounded && _airTime > 0.2f) OnLanded();
        if (grounded) _airTime = 0;
        _wasGrounded = grounded;
    }

    /// <summary>The player landed from a jump: Momentum Transfer gives its burst of movement speed.</summary>
    public static void OnLanded()
    {
        var (mult, seconds) = PerkRules.LandingBoost(Pc.Stats);
        if (seconds > 0) SpellWorld.Buffs.Apply("perk.momentum", seconds, SpellTuning.Speed(mult));
    }

    // ---------------------------------------------------------------- autocast shield

    private static void AutocastShield(float dt)
    {
        bool equipped = Pc.Spells.Loadout.Contains("spell.sustain_shield");
        bool manual = SpellRunning("spell.sustain_shield");
        bool want = equipped && !manual && PerkRules.ShieldAutocasts(Pc.Stats, SpellCaster.Energy, SpellCaster.MaxEnergy);
        if (want)
        {
            SpellWorld.Shield.Channeling = true;
            ShieldAutocasting = true;
            SpellCaster.Spend(PerkRules.AutocastUpkeep * dt);
        }
        else if (ShieldAutocasting)
        {
            ShieldAutocasting = false;
            if (!manual) SpellWorld.Shield.Channeling = false;
        }
    }

    // ---------------------------------------------------------------- Light the Way

    private static readonly int PerfLights = Perf.Perf.Register("  PerkHooks.ApplyFlashlights");

    private static readonly List<FlashlightController> OwnLights = new();
    private static float _lightSearch;

    private static void ApplyFlashlights()
    {
        using var perf = Perf.Perf.Scope(PerfLights);
        var (intensity, drain) = PerkRules.Flashlight(Pc.Stats);
        AppliedLight = (intensity, drain);
        // The game's flashlight (FlashlightHeld) is its own root object, not under the player, so finding it takes a scan of every
        // object (~65-80 ms). Scan once and keep it; scan again (at most every 30 s) only while none is cached or one was destroyed
        // (COS-132: was twice a second). The factors are applied to the cached list.
        _lightSearch -= 0.5f;
        if (_lightSearch <= 0 && (OwnLights.Count == 0 || OwnLights.Exists(l => l == null)))
        {
            _lightSearch = 30f;
            OwnLights.Clear();
            foreach (var l in Resources.FindObjectsOfTypeAll<FlashlightController>())
            {
                if (l == null) continue;
                if (!Lights.ContainsKey(l.Pointer)) RLog.Msg($"Champions of the Sons: flashlight {l.name} under {(l.transform.root != null ? l.transform.root.name : "-")}");
                OwnLights.Add(l);
            }
        }
        foreach (var light in OwnLights)
        {
            if (light == null) continue;
            if (!Lights.TryGetValue(light.Pointer, out var orig))
                Lights[light.Pointer] = orig = new LightOriginal(light._maxLightIntensity, light._powerDrainRate);
            light._maxLightIntensity = orig.Intensity * (float)intensity;
            light._powerDrainRate = orig.Drain * (float)drain;
        }
    }

    /// <summary>Flashlight controllers whose data now carries the perk factors (self-test).</summary>
    public static int FlashlightsSeen => Lights.Count;
}
