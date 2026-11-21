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

/// <summary>A successful cast as sent to teammates (COS-80): the cast id, whether it switched a toggle on, and what it drew.</summary>
public sealed record SpellCastSync(string SpellId, uint CastId, bool ToggledOn, List<ChampionsOfTheSons.Core.Networking.FxShape> Shapes);

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

    /// <summary>The last successful cast's sync data (COS-80; the co-op layer sends it).</summary>
    public static SpellCastSync? LastSync { get; private set; }

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

    /// <summary>Sinew Channel's energy coming back after casts (COS-128).</summary>
    internal static readonly EnergyRefund Refund = new();

    /// <summary>Gives energy back (the opposite of <see cref="Spend"/>).</summary>
    internal static void Gain(double amount) => Spend(-amount);

    internal static void Spend(double amount)
    {
        var s = LocalPlayer.Vitals?.Stamina;
        if (s == null || amount == 0) return;
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
        Refund.Add(cost.Energy + cost.Stamina, pc.Stats.Get(StatIds.SpellCostRefund));
        uint castId = SpellNet.BeginCast();
        try
        {
            effect();
        }
        catch (Exception e)
        {
            RLog.Error($"Champions of the Sons: spell {id} failed: {e}");
        }
        var shapes = SpellNet.EndCast(castId);
        bool toggledOn = pc.Spells.IsActive(slot);
        if (toggledOn) SpellNet.ToggleOn(id, castId);
        LastSync = new SpellCastSync(id, castId, toggledOn, shapes);
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

    /// <summary>Where the player's feet land when blinking from eyes at <paramref name="origin"/> along <paramref name="dir"/> (Max 2026-11-20:
    /// where they point, on the ground when they can). Aiming at the ground or another walkable surface within range lands standing on it;
    /// aiming at a wall stops short of it and drops to the ground just below if it is close; aiming at open air goes to that point in the
    /// air, never below the ground.</summary>
    public static Vector3 BlinkTarget(Vector3 origin, Vector3 dir, float range, float eyeHeight)
    {
        dir.Normalize();
        const float radius = 0.4f, margin = 0.5f, walkable = 0.6f, stepDown = 3f;
        if (Physics.SphereCast(origin, radius, dir, out var hit, range, ~0, QueryTriggerInteraction.Ignore))
        {
            if (hit.normal.y >= walkable) return hit.point + Vector3.up * 0.1f;
            var stop = origin + dir * Mathf.Max(0, hit.distance - margin);
            var feetAtWall = stop - Vector3.up * eyeHeight;
            if (GroundY(stop + Vector3.up * 0.5f) is float wallGround)
                feetAtWall.y = feetAtWall.y - wallGround <= stepDown ? wallGround + 0.1f : Mathf.Max(feetAtWall.y, wallGround + 0.1f);
            return feetAtWall;
        }

        var feet = origin + dir * range - Vector3.up * eyeHeight;
        if (GroundY(feet + Vector3.up * (eyeHeight + 3f)) is float gy) feet.y = Mathf.Max(feet.y, gy + 0.1f);
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
        var stats = PlayerState.Character.Stats;
        // Blink range is the spell_blink_range stat (15 m; items add to it) (COS-88).
        float range = (float)(stats.Get(PerkStatIds.BlinkRange) > 0 ? stats.Get(PerkStatIds.BlinkRange) : BlinkRange);
        var start = player.position;
        var feet = BlinkTarget(eye, cam.forward, range, eye.y - player.position.y);

        TeleportPlayer(feet);
        BlinkItems(start, feet);
        return true;
    }

    /// <summary>Enemies struck by the last Blink (self-test).</summary>
    public static int BlinkStruck { get; private set; }

    /// <summary>Phase Pauldrons: every enemy passed through takes <c>spell_blink_damage</c>; Tearing Spacetime: an explosion at the exit,
    /// damage scaling with movement speed and radius with the distance blinked (COS-88).</summary>
    public static void BlinkItems(Vector3 from, Vector3 to)
    {
        var stats = PlayerState.Character.Stats;
        BlinkStruck = 0;
        double passDamage = stats.Get(PerkStatIds.BlinkDamage);
        if (passDamage > 0)
            foreach (var e in SpellWorld.ActiveEnemies().ToList())
            {
                if (SpellWorld.DistanceToSegment(SpellWorld.Center(e), from + Vector3.up, to + Vector3.up) > 1.5f) continue;
                SpellWorld.Strike(e, SpellWorld.Hit(passDamage, stats.Get("spell_blink_damage_scaling")));
                BlinkStruck++;
            }
        if (!stats.IsOn(PerkStatIds.BlinkExplosion)) return;
        float radius = Mathf.Max((float)PerkRules.BlinkExplosionMinRadius, Vector3.Distance(from, to) * (float)PerkRules.BlinkExplosionRadiusShare);
        double damage = PerkRules.BlinkExplosionDamage * Math.Max(0.1, stats.Get(StatIds.MovementSpeed));
        foreach (var e in SpellWorld.EnemiesWithin(to, radius))
        {
            SpellWorld.Strike(e, SpellWorld.Hit(damage, PerkRules.BlinkExplosionScaling));
            BlinkStruck++;
        }
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
