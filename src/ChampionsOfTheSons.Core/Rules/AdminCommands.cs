namespace ChampionsOfTheSons.Core.Rules;

/// <summary>Where a console command came from (COS-99).</summary>
public enum CommandSource
{
    /// <summary>The local console, no multiplayer session.</summary>
    SinglePlayer,
    /// <summary>The local console of the co-op host.</summary>
    Host,
    /// <summary>The local console of a co-op client.</summary>
    Client,
    /// <summary>Server chat from a player the dedicated server lists as admin or owner.</summary>
    ServerAdmin,
    /// <summary>Server chat from any other player.</summary>
    ServerPlayer,
}

/// <summary>
/// Who may use the admin and cheat commands (Max, 2026-11-20): only the host or a dedicated-server admin; clients never.
/// Commands that change another player arrive at that player as <see cref="Networking.AdminGrant"/>, which a client
/// accepts only from the host's connection.
/// </summary>
public static class AdminAccess
{
    public static bool MayRun(CommandSource source, bool commandsEnabled) =>
        commandsEnabled && source is CommandSource.SinglePlayer or CommandSource.Host or CommandSource.ServerAdmin;

    /// <summary>Splits a console line into lower-case command name and arguments; a leading "cots" is dropped.</summary>
    public static (string Name, string[] Args) Parse(string line)
    {
        var parts = (line ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        int start = parts.Length > 0 && parts[0].Equals("cots", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        if (parts.Length <= start) return ("help", Array.Empty<string>());
        return (parts[start].ToLowerInvariant(), parts.Skip(start + 1).ToArray());
    }
}
