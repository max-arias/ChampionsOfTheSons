using ChampionsOfTheSons.Core.Rules;
using RedLoader;
using TheForest.Utils;
using UnityEngine;

namespace ChampionsOfTheSons.Mod.Spells;

public enum SpellCastOutcome
{
    Cast,
    EmptySlot,
    OnCooldown,
    NotEnoughEnergy,
    NotImplemented,
    NotInWorld,
    /// <summary>A channeled or toggle spell that was running has been switched off.</summary>
    Stopped,
}

/// <summary>A cast, as a clear event a later co-op sync can send: which spell, from where, along what direction, at what aim point.</summary>
public readonly record struct SpellCastInfo(string SpellId, Vector3 Origin, Vector3 Direction, Vector3 Aim);

/// <summary>
/// Casting: costs, cooldown (<see cref="SpellBook.TryCast"/>) and the in-game effect per spell id.
/// Energy is the game's own stamina bar scaled to the character's max energy: a cast takes the spell's
/// energy part, plus its stamina part, out of it.
/// </summary>
internal static class SpellCaster
{
    public const float BlinkRange = 15f;

    private static readonly Dictionary<string, Func<bool>> Effects = new(SpellEffects.Table)
    {
        ["spell.blink"] = Blink,
    };

    /// <summary>Raised after every successful cast (or switch-on of a channeled/toggle spell), with where and how it was cast.</summary>
    public static event Action<SpellCastInfo>? Casted;

    public static string LastMessage { get; private set; } = "";

    public static bool IsImplemented(string spellId) => Effects.ContainsKey(spellId);

    /// <summary>Current energy in the character's energy units (game stamina scaled to max energy).</summary>
    public static double Energy
    {
        get
        {
            var s = LocalPlayer.Vitals?.Stamina;
            if (s == null) return 0;
            float max = s.GetMax();
            return max <= 0 ? 0 : s._currentValue / max * MaxEnergy;
        }
    }

    public static double MaxEnergy => Derived.MaxEnergy(PlayerState.Character.Stats);

    internal static void Spend(double amount)
    {
        var s = LocalPlayer.Vitals?.Stamina;
        if (s == null || amount <= 0) return;
        float max = s.GetMax();
        s.SetCurrentValue(Mathf.Clamp(s._currentValue - (float)(amount / MaxEnergy) * max, 0, max));
    }

    public static SpellCastOutcome Cast(int slot)
    {
        LastMessage = "";
        if (!LocalPlayer.IsInWorld || LocalPlayer.Vitals == null) return SpellCastOutcome.NotInWorld;
        var pc = PlayerState.Character;
        if (slot is < 0 or >= SpellBook.Slots || pc.Spells.Loadout[slot] is not string id) return SpellCastOutcome.EmptySlot;
        if (!Effects.TryGetValue(id, out var effect))
        {
            LastMessage = "not implemented yet";
            return SpellCastOutcome.NotImplemented;
        }

        var result = pc.Spells.TryCast(slot, Energy, pc.Stats, out var cost);
        switch (result)
        {
            case CastResult.EmptySlot: return SpellCastOutcome.EmptySlot;
            case CastResult.OnCooldown: return SpellCastOutcome.OnCooldown;
            case CastResult.NotEnoughEnergy: LastMessage = "not enough energy"; return SpellCastOutcome.NotEnoughEnergy;
            case CastResult.Stopped:
                SpellEffects.Stopped(slot);
                return SpellCastOutcome.Stopped;
        }

        Spend(cost.Energy + cost.Stamina);
        try
        {
            effect();
        }
        catch (Exception e)
        {
            RLog.Error($"Champions of the Sons: spell {id} failed: {e}");
        }
        try
        {
            var eye = SpellWorld.Eye;
            Casted?.Invoke(new SpellCastInfo(id, eye.position, eye.forward, SpellWorld.AimPoint(50)));
        }
        catch (Exception e)
        {
            RLog.Error($"Champions of the Sons: spell cast listener failed: {e}");
        }
        return SpellCastOutcome.Cast;
    }

    // ---------------------------------------------------------------- Blink

    /// <summary>Where the player's feet land when blinking from eyes at <paramref name="origin"/> along <paramref name="dir"/>: up to the range,
    /// stopping short of obstacles, and never below the ground.</summary>
    public static Vector3 BlinkTarget(Vector3 origin, Vector3 dir, float range, float eyeHeight)
    {
        dir.Normalize();
        var dest = origin + dir * range;
        const float radius = 0.4f, margin = 0.5f;
        if (Physics.SphereCast(origin, radius, dir, out var hit, range, ~0, QueryTriggerInteraction.Ignore))
            dest = origin + dir * Mathf.Max(0, hit.distance - margin);

        // Stay on the ground: look straight down from above the destination.
        var feet = dest - Vector3.up * eyeHeight;
        if (GroundY(dest + Vector3.up * 3f) is float gy) feet.y = Mathf.Max(feet.y, gy + 0.1f);
        return feet;
    }

    /// <summary>Height of the first thing below <paramref name="from"/>, ignoring the player's own colliders.</summary>
    public static float? GroundY(Vector3 from)
    {
        var self = LocalPlayer.Transform != null ? LocalPlayer.Transform.root : null;
        float? best = null;
        foreach (var h in Physics.RaycastAll(from, Vector3.down, 200f, ~0, QueryTriggerInteraction.Ignore))
        {
            if (self != null && h.collider.transform.IsChildOf(self)) continue;
            if (best == null || h.point.y > best) best = h.point.y;
        }
        return best;
    }

    private static bool Blink()
    {
        var player = LocalPlayer.Transform;
        var cam = Camera.main != null ? Camera.main.transform : player;
        // Cast from the eyes so the sphere starts clear of our own collider, along the view direction.
        var eye = cam.position;
        var feet = BlinkTarget(eye, cam.forward, BlinkRange, eye.y - player.position.y);

        TeleportPlayer(feet);
        return true;
    }

    /// <summary>Moves the player's feet to a point without the character controller fighting it.</summary>
    public static void TeleportPlayer(Vector3 feet)
    {
        var player = LocalPlayer.Transform;
        var cc = player.GetComponent<CharacterController>();
        if (cc != null) cc.enabled = false;
        player.position = feet;
        if (cc != null) cc.enabled = true;
        var rb = player.GetComponent<Rigidbody>();
        if (rb != null) rb.velocity = Vector3.zero;
    }
}
