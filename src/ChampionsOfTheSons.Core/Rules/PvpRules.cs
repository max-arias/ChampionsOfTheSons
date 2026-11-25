namespace ChampionsOfTheSons.Core.Rules;

/// <summary>The game's own PvP damage setting (COS-213: the mod follows it, it does not set it).</summary>
public enum PvpMode : byte
{
    Off = 0,
    Reduced = 1,
    Normal = 2,
}

/// <summary>
/// Player-versus-player rules (COS-213, Max 2026-11-24: co-op honours the base game's PvP setting). Spells hit other players
/// only when the game's PvP damage is on: full on Normal, half on Reduced (own number), none when Off.
/// </summary>
public static class PvpRules
{
    public const double ReducedFactor = 0.5;

    public static double Factor(PvpMode mode) => mode switch
    {
        PvpMode.Normal => 1,
        PvpMode.Reduced => ReducedFactor,
        _ => 0,
    };

    /// <summary>The game's setting name ("Off", "Reduced", "Normal"); anything unknown counts as Off.</summary>
    public static PvpMode Parse(string? setting) => setting?.Trim().ToLowerInvariant() switch
    {
        "normal" => PvpMode.Normal,
        "reduced" => PvpMode.Reduced,
        _ => PvpMode.Off,
    };
}
