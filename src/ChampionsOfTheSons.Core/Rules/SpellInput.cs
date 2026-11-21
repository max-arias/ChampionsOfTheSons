using ChampionsOfTheSons.Core.Content;

namespace ChampionsOfTheSons.Core.Rules;

/// <summary>How a spell reacts to its key (COS-82, after COTF).</summary>
public enum CastMode
{
    /// <summary>Casts on key down, and again while the key is held whenever it is ready.</summary>
    Instant,
    /// <summary>Holding the key shows where the spell will land; the cast and its cost happen on key up.</summary>
    Release,
    /// <summary>Runs while the key is held, paying its upkeep every second, and stops on key up.</summary>
    Channel,
    /// <summary>Key down switches it on or off.</summary>
    Toggle,
}

/// <summary>What the caster should do this frame for one slot.</summary>
public enum SpellInputAction
{
    None,
    Cast,
    BeginAim,
    CancelAim,
    StopChannel,
}

/// <summary>
/// Turns each spell slot's held/not-held key state, read once per frame, into casts (COS-82). Pure: the mod reads the keys and the pad,
/// this decides. Only one slot aims at a time: pressing another aimed spell drops the first aim without casting it.
/// </summary>
public sealed class SpellInput
{
    private readonly bool[] _held = new bool[SpellBook.Slots];
    private readonly bool[] _aiming = new bool[SpellBook.Slots];

    public static CastMode ModeOf(SpellDef spell) =>
        spell.Channeled ? CastMode.Channel : spell.Toggle ? CastMode.Toggle : spell.CastOnRelease ? CastMode.Release : CastMode.Instant;

    public bool IsAiming(int slot) => slot is >= 0 and < SpellBook.Slots && _aiming[slot];

    /// <summary>The slot being aimed, or -1.</summary>
    public int AimingSlot => Array.IndexOf(_aiming, true);

    /// <param name="down">The slot's key (or pad binding) is held this frame.</param>
    /// <param name="ready">The slot is off cooldown (Instant repeats only when ready).</param>
    /// <param name="running">A channel in this slot is running.</param>
    public SpellInputAction Step(int slot, bool down, CastMode mode, bool ready, bool running)
    {
        bool pressed = down && !_held[slot];
        bool released = !down && _held[slot];
        _held[slot] = down;
        switch (mode)
        {
            case CastMode.Instant:
                return down && (pressed || ready) ? SpellInputAction.Cast : SpellInputAction.None;
            case CastMode.Release:
                if (pressed)
                {
                    for (int i = 0; i < _aiming.Length; i++) _aiming[i] = false;
                    _aiming[slot] = true;
                    return SpellInputAction.BeginAim;
                }
                if (released && _aiming[slot])
                {
                    _aiming[slot] = false;
                    return SpellInputAction.Cast;
                }
                return SpellInputAction.None;
            case CastMode.Channel:
                if (pressed) return running ? SpellInputAction.StopChannel : SpellInputAction.Cast;
                if (released && running) return SpellInputAction.StopChannel;
                return SpellInputAction.None;
            default:
                return pressed ? SpellInputAction.Cast : SpellInputAction.None;
        }
    }

    /// <summary>Drops an aim without casting (a menu opened, the spell left the slot, silence). Returns whether the slot was aiming.</summary>
    public bool Cancel(int slot)
    {
        if (!IsAiming(slot)) return false;
        _aiming[slot] = false;
        return true;
    }

    /// <summary>Forgets every key state (world left, focus lost): a key still held afterwards counts as a new press.</summary>
    public void Reset()
    {
        Array.Clear(_held);
        Array.Clear(_aiming);
    }
}
