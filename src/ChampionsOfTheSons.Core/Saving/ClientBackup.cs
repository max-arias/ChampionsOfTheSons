namespace ChampionsOfTheSons.Core.Saving;

/// <summary>
/// COS-100: a co-op client's character lives in the host's save, so it is lost when the host quits before saving (COTF issues #9, #11).
/// The client also keeps its own copy, written on every game save, when the session ends and every few minutes; on load the newer one wins.
/// </summary>
public static class ClientBackup
{
    /// <summary>Seconds between the client's own backups while playing.</summary>
    public const double Interval = 120;

    /// <summary>The save to load: the local backup when it is strictly newer than the one from the host save, else the host's.</summary>
    public static PlayerSave Pick(PlayerSave fromHost, PlayerSave? backup) =>
        backup != null && backup.SavedAt > fromHost.SavedAt ? backup : fromHost;

    /// <summary>File name for a backup of the character in one multiplayer world; only letters, digits, '-' and '_' are kept.</summary>
    public static string FileName(string worldKey)
    {
        var clean = new string(worldKey.Where(c => char.IsLetterOrDigit(c) || c is '-' or '_').ToArray());
        return $"client-{(clean.Length == 0 ? "unknown" : clean)}.json";
    }
}
