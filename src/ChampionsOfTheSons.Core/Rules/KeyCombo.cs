namespace ChampionsOfTheSons.Core.Rules;

/// <summary>
/// A mod key with optional modifiers, stored in the config as "alt+1" or "ctrl+shift+f" (COS-133). Either Alt, Ctrl or Shift key counts.
/// A plain value ("f", "leftShift") has no modifiers.
/// </summary>
public readonly record struct KeyCombo(string Key, bool Ctrl, bool Shift, bool Alt)
{
    public int ModifierCount => (Ctrl ? 1 : 0) + (Shift ? 1 : 0) + (Alt ? 1 : 0);
    public bool HasModifiers => ModifierCount > 0;

    public static KeyCombo Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return new KeyCombo("", false, false, false);
        var parts = value.Trim().Split('+');
        bool ctrl = false, shift = false, alt = false;
        for (int i = 0; i < parts.Length - 1; i++)
        {
            switch (Modifier(parts[i]))
            {
                case 'c': ctrl = true; break;
                case 's': shift = true; break;
                case 'a': alt = true; break;
                default: return new KeyCombo(value.Trim(), false, false, false); // not a combo after all
            }
        }
        var key = parts[^1];
        return key.Length == 0 ? new KeyCombo(value.Trim(), false, false, false) : new KeyCombo(key, ctrl, shift, alt);
    }

    /// <summary>'c', 's', 'a' for a Ctrl, Shift or Alt key name (left, right or either), otherwise '\0'.</summary>
    public static char Modifier(string name) => name.ToLowerInvariant() switch
    {
        "ctrl" or "leftctrl" or "rightctrl" => 'c',
        "shift" or "leftshift" or "rightshift" => 's',
        "alt" or "leftalt" or "rightalt" => 'a',
        _ => '\0',
    };

    public static bool IsModifierKey(string name) => Modifier(name) != '\0';

    /// <summary>The config form: modifiers in a fixed order, then the key ("ctrl+alt+1").</summary>
    public override string ToString() => (Ctrl ? "ctrl+" : "") + (Shift ? "shift+" : "") + (Alt ? "alt+" : "") + Key;

    public bool SameAs(KeyCombo o) =>
        Ctrl == o.Ctrl && Shift == o.Shift && Alt == o.Alt && string.Equals(Key, o.Key, StringComparison.OrdinalIgnoreCase);

    public bool SameKey(KeyCombo o) => string.Equals(Key, o.Key, StringComparison.OrdinalIgnoreCase);

    /// <summary>Every modifier this combo needs is held (extra held modifiers are allowed, so Shift-sprinting still casts on F).</summary>
    public bool ModifiersHeld(bool ctrl, bool shift, bool alt) => (!Ctrl || ctrl) && (!Shift || shift) && (!Alt || alt);

    /// <summary>
    /// Whether this combo fires when its key is pressed with the given modifiers held, next to the other mod combos: its modifiers must be
    /// held, and no other combo on the same key whose modifiers are held may need more of them (Alt+1 wins over a plain 1).
    /// </summary>
    public bool Fires(IEnumerable<KeyCombo> others, bool ctrl, bool shift, bool alt)
    {
        if (!ModifiersHeld(ctrl, shift, alt)) return false;
        var self = this;
        return !others.Any(o => o.SameKey(self) && !o.SameAs(self) && o.ModifiersHeld(ctrl, shift, alt) && o.ModifierCount > self.ModifierCount);
    }

    /// <summary>Display text: "Ctrl+Alt+" before the key's own short name.</summary>
    public string Display(Func<string, string> keyName) =>
        (Ctrl ? "Ctrl+" : "") + (Shift ? "Shift+" : "") + (Alt ? "Alt+" : "") + keyName(Key);
}
