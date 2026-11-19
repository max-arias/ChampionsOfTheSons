using ChampionsOfTheSons.Core.Rules;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ChampionsOfTheSons.Mod.Pad;

/// <summary>
/// Gamepad state, sampled once per frame by <see cref="GamepadFeature"/>. Edges (pressed this frame) are computed here from the
/// held state so real pads and injected presses (self-tests) behave the same. The left stick also acts as a d-pad for
/// navigation, with key repeat; bindings use true edges only (<see cref="Pressed"/>), navigation uses <see cref="NavPressed"/>.
/// </summary>
internal static class PadInput
{
    private const float StickThreshold = 0.55f;
    private const float RepeatDelay = 0.35f, RepeatInterval = 0.11f;

    private static readonly int Count = Enum.GetValues(typeof(PadButton)).Length;
    private static readonly bool[] Down = new bool[Count];
    private static readonly bool[] Prev = new bool[Count];
    private static readonly bool[] Injected = new bool[Count];
    private static readonly bool[] Nav = new bool[4];
    private static readonly bool[] NavPrev = new bool[4];
    private static readonly bool[] NavFire = new bool[4];
    private static readonly float[] NavTimer = new float[4];

    public static bool Connected => Gamepad.current != null;

    /// <summary>True while a pad is the active input: a pad button went down within the last few seconds.</summary>
    public static bool RecentlyUsed => Time.unscaledTime - LastUse < 4f;
    public static float LastUse { get; private set; } = -100f;

    public static bool Held(PadButton b) => Down[(int)b];
    public static bool Pressed(PadButton b) => Down[(int)b] && !Prev[(int)b];
    public static bool NavPressed(FocusDirection d) => NavFire[(int)d];

    /// <summary>Makes <paramref name="b"/> read as held for the next <see cref="Poll"/> only (a button press for self-tests).</summary>
    public static void Inject(params PadButton[] buttons)
    {
        foreach (var b in buttons) Injected[(int)b] = true;
    }

    public static bool IsTriggered(PadBinding binding) => binding.IsTriggered(Held, Pressed);

    public static void Poll(float dt)
    {
        Array.Copy(Down, Prev, Count);
        Array.Copy(Nav, NavPrev, 4);
        var pad = Gamepad.current;
        Vector2 stick = default;
        if (pad != null)
        {
            try
            {
                stick = pad.leftStick.ReadValue();
            }
            catch
            {
                stick = default;
            }
        }
        for (int i = 0; i < Count; i++)
        {
            Down[i] = Injected[i] || (pad != null && Read(pad, (PadButton)i));
            Injected[i] = false;
        }
        var stickDirs = new[]
        {
            stick.y > StickThreshold, stick.y < -StickThreshold, stick.x < -StickThreshold, stick.x > StickThreshold,
        };
        for (int d = 0; d < 4; d++)
        {
            var button = d switch { 0 => PadButton.DpadUp, 1 => PadButton.DpadDown, 2 => PadButton.DpadLeft, _ => PadButton.DpadRight };
            Nav[d] = Down[(int)button] || stickDirs[d];
            NavFire[d] = false;
            if (!Nav[d]) { NavTimer[d] = 0; continue; }
            if (!NavPrev[d]) { NavFire[d] = true; NavTimer[d] = RepeatDelay; }
            else
            {
                NavTimer[d] -= dt;
                if (NavTimer[d] <= 0) { NavFire[d] = true; NavTimer[d] = RepeatInterval; }
            }
        }
        for (int i = 0; i < Count; i++)
        {
            if (Down[i] && !Prev[i]) LastUse = Time.unscaledTime;
        }
        for (int d = 0; d < 4; d++)
        {
            if (Nav[d] && !NavPrev[d]) LastUse = Time.unscaledTime;
        }
    }

    private static bool Read(Gamepad pad, PadButton b)
    {
        try
        {
            return b switch
            {
                PadButton.South => pad.buttonSouth.isPressed,
                PadButton.East => pad.buttonEast.isPressed,
                PadButton.West => pad.buttonWest.isPressed,
                PadButton.North => pad.buttonNorth.isPressed,
                PadButton.LeftShoulder => pad.leftShoulder.isPressed,
                PadButton.RightShoulder => pad.rightShoulder.isPressed,
                PadButton.LeftTrigger => pad.leftTrigger.isPressed,
                PadButton.RightTrigger => pad.rightTrigger.isPressed,
                PadButton.DpadUp => pad.dpad.up.isPressed,
                PadButton.DpadDown => pad.dpad.down.isPressed,
                PadButton.DpadLeft => pad.dpad.left.isPressed,
                PadButton.DpadRight => pad.dpad.right.isPressed,
                PadButton.Start => pad.startButton.isPressed,
                PadButton.Select => pad.selectButton.isPressed,
                PadButton.LeftStick => pad.leftStickButton.isPressed,
                PadButton.RightStick => pad.rightStickButton.isPressed,
                _ => false,
            };
        }
        catch
        {
            return false;
        }
    }
}
