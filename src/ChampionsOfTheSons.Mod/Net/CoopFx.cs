using ChampionsOfTheSons.Core.Networking;
using ChampionsOfTheSons.Mod.Hud;
using RedLoader;
using UnityEngine;

namespace ChampionsOfTheSons.Mod.Net;

/// <summary>
/// What other players do, shown locally: damage numbers for their hits (COS-44) and the visuals of their spell casts (COS-51).
/// A cast carries the visuals the caster drew (COS-80); the spells feature draws them (<see cref="DrawCast"/>, unset on a
/// dedicated server, which draws nothing). A cast with no visuals gets a short flash at the caster.
/// </summary>
public static class CoopFx
{

    /// <summary>Last remote spell shown (self-tests).</summary>
    public static SpellCast? LastSpell { get; private set; }

    /// <summary>Remote damage numbers shown so far (self-tests).</summary>
    public static int DamageShownCount { get; private set; }

    /// <summary>Draws a teammate's cast from its shapes (set by the spells feature).</summary>
    public static Action<SpellCast>? DrawCast { get; set; }

    /// <summary>Draws a teammate's later visuals or stops (set by the spells feature).</summary>
    public static Action<SpellFxEvent>? DrawUpdate { get; set; }

    public static void ShowUpdate(SpellFxEvent ev)
    {
        if (ServerConfig.OnDedicatedServer || DrawUpdate == null) return;
        try { DrawUpdate(ev); }
        catch (Exception e) { RLog.Warning($"{CoopTestJoin.Tag} spell update failed: {e.Message}"); }
    }

    public static void ShowDamage(Vector3 world, float amount, bool crit)
    {
        DamageShownCount++;
        try
        {
            var hud = Features.Loaded.OfType<HudFeature>().FirstOrDefault();
            if (hud != null && Config.ShowDamageNumbers.Value) hud.Numbers.Spawn(world, amount, crit);
        }
        catch (Exception e)
        {
            RLog.Warning($"{CoopTestJoin.Tag} damage number failed: {e.Message}");
        }
    }

    public static void ShowSpell(SpellCast cast)
    {
        LastSpell = cast;
        if (ServerConfig.OnDedicatedServer) return; // nothing is shown or played on a dedicated server
        if (cast.Mode != SpellCastMode.Off)
            Audio.ModAudio.PlayAt(Audio.SoundCues.Spell(cast.SpellId), CoopSession.ToUnity(cast.Origin)); // COS-101: a teammate's cast, where they stand
        try
        {
            if ((cast.AllShapes.Count > 0 || cast.Mode == SpellCastMode.Off) && DrawCast != null) DrawCast(cast);
            else if (cast.Mode != SpellCastMode.Off) Flash(CoopSession.ToUnity(cast.Origin), new Color(0.5f, 0.7f, 1f));
            if (cast.SpellId == "spell.blink") Flash(CoopSession.ToUnity(cast.Origin) + CoopSession.ToUnity(cast.Direction), new Color(0.7f, 0.5f, 1f));
        }
        catch (Exception e)
        {
            RLog.Warning($"{CoopTestJoin.Tag} spell visual failed: {e.Message}");
        }
    }

    /// <summary>A small burst (the bundled effect, COS-102) or a light sphere that fades out: the fallback visual.</summary>
    public static void Flash(Vector3 at, Color color, float seconds = 0.6f)
    {
        var fx = Visuals.FxKit.Spawn(Visuals.FxKit.Burst, at, 0.8f, color);
        if (fx != null)
        {
            UnityEngine.Object.Destroy(fx, Math.Max(seconds, 0.7f));
            return;
        }
        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = "CotsCoopFlash";
        var col = go.GetComponent<Collider>();
        if (col != null) UnityEngine.Object.Destroy(col);
        go.transform.position = at;
        go.transform.localScale = Vector3.one * 0.8f;
        var r = go.GetComponent<Renderer>();
        if (r != null) r.material.color = color;
        UnityEngine.Object.Destroy(go, seconds);
    }
}
