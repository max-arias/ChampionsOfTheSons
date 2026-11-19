namespace ChampionsOfTheSons.Core.Rules;

/// <summary>The gamepad buttons the mod can bind. Names match Unity's GamepadButton so the bridge can map them 1:1.</summary>
public enum PadButton
{
    South, East, West, North,
    LeftShoulder, RightShoulder, LeftTrigger, RightTrigger,
    DpadUp, DpadDown, DpadLeft, DpadRight,
    Start, Select, LeftStick, RightStick,
}

/// <summary>
/// A pad binding: one button, optionally with modifier buttons that must be held ("LeftShoulder+DpadUp").
/// It fires on the frame its last (main) button goes down while every modifier is held.
/// </summary>
public sealed record PadBinding(PadButton Main, IReadOnlyList<PadButton> Modifiers)
{
    public static bool TryParse(string? text, out PadBinding binding)
    {
        binding = null!;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var parts = text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0) return false;
        var buttons = new List<PadButton>();
        foreach (var p in parts)
        {
            if (!Enum.TryParse<PadButton>(p, true, out var b) || !Enum.IsDefined(b)) return false;
            buttons.Add(b);
        }
        binding = new PadBinding(buttons[^1], buttons.Take(buttons.Count - 1).ToList());
        return true;
    }

    public override string ToString() => string.Join("+", Modifiers.Append(Main));

    /// <summary>True when <see cref="Main"/> went down this frame and every modifier is held.</summary>
    public bool IsTriggered(Func<PadButton, bool> held, Func<PadButton, bool> pressedThisFrame) =>
        pressedThisFrame(Main) && Modifiers.All(held);

    /// <summary>True when any of this binding's buttons is a modifier of <paramref name="other"/>, i.e. the two share a button.</summary>
    public bool Uses(PadButton b) => Main == b || Modifiers.Contains(b);
}

public enum FocusDirection { Up, Down, Left, Right }

/// <summary>A rectangle in screen space, y growing downwards.</summary>
public readonly record struct FocusRect(float X, float Y, float W, float H)
{
    public float CenterX => X + W / 2;
    public float CenterY => Y + H / 2;
}

/// <summary>Directional focus movement over a set of rectangles, for panels that were built for a mouse.</summary>
public static class FocusNav
{
    /// <summary>Index of the rectangle to focus when moving from <paramref name="current"/> in a direction, or -1 when nothing lies that way.
    /// Candidates must lie mostly in the direction; the nearest wins, with sideways offset counting double so a row stays a row.</summary>
    public static int Move(IReadOnlyList<FocusRect> items, int current, FocusDirection dir)
    {
        if (current < 0 || current >= items.Count) return -1;
        var from = items[current];
        int best = -1;
        float bestScore = float.MaxValue;
        for (int i = 0; i < items.Count; i++)
        {
            if (i == current) continue;
            float dx = items[i].CenterX - from.CenterX, dy = items[i].CenterY - from.CenterY;
            float along = dir switch { FocusDirection.Up => -dy, FocusDirection.Down => dy, FocusDirection.Left => -dx, _ => dx };
            float across = dir is FocusDirection.Up or FocusDirection.Down ? Math.Abs(dx) : Math.Abs(dy);
            if (along <= 0.5f || across > along * 2.5f + from.W) continue; // behind, or too far off the axis
            float score = along + across * 2;
            if (score < bestScore) { bestScore = score; best = i; }
        }
        return best;
    }

    /// <summary>The rectangle whose center is nearest to a point, or -1 for an empty list.</summary>
    public static int Nearest(IReadOnlyList<FocusRect> items, float x, float y)
    {
        int best = -1;
        float bestD = float.MaxValue;
        for (int i = 0; i < items.Count; i++)
        {
            float dx = items[i].CenterX - x, dy = items[i].CenterY - y;
            float d = dx * dx + dy * dy;
            if (d < bestD) { bestD = d; best = i; }
        }
        return best;
    }
}
