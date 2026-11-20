using ChampionsOfTheSons.Core.Networking;
using ChampionsOfTheSons.Mod.Hud;
using RedLoader;
using UnityEngine;

namespace ChampionsOfTheSons.Mod.Net;

/// <summary>
/// What other players do, shown locally: damage numbers for their hits (COS-44) and the visuals of their spell casts (COS-51).
/// Spells draw through <see cref="Register"/>: the spells track registers a richer effect per spell id; any cast without one gets
/// a short flash at the caster so teammates always see that something happened.
/// </summary>
public static class CoopFx
{
    private static readonly Dictionary<string, Action<SpellCast>> SpellFx = new();

    /// <summary>Last remote spell shown (self-tests).</summary>
    public static SpellCast? LastSpell { get; private set; }

    /// <summary>Remote damage numbers shown so far (self-tests).</summary>
    public static int DamageShownCount { get; private set; }

    public static void Register(string spellId, Action<SpellCast> draw) => SpellFx[spellId] = draw;

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
        Audio.ModAudio.PlayAt(Audio.SoundCues.Spell(cast.SpellId), CoopSession.ToUnity(cast.Origin)); // COS-101: a teammate's cast, where they stand
        try
        {
            if (SpellFx.TryGetValue(cast.SpellId, out var draw)) draw(cast);
            else Flash(CoopSession.ToUnity(cast.Origin), new Color(0.5f, 0.7f, 1f));
            if (cast.SpellId == "spell.blink") Flash(CoopSession.ToUnity(cast.Origin) + CoopSession.ToUnity(cast.Direction), new Color(0.7f, 0.5f, 1f));
        }
        catch (Exception e)
        {
            RLog.Warning($"{CoopTestJoin.Tag} spell visual failed: {e.Message}");
        }
    }

    /// <summary>A small light sphere that fades out: the fallback visual.</summary>
    public static void Flash(Vector3 at, Color color, float seconds = 0.6f)
    {
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
