using ChampionsOfTheSons.Core.Saving;
using ChampionsOfTheSons.Mod.Net;
using RedLoader;
using Sons.Gameplay.GameSetup;
using TheForest.Utils;
using UnityEngine;

namespace ChampionsOfTheSons.Mod.Saves;

/// <summary>
/// COS-100: what the character save holds besides the rules state. Health as a share of the modded max health and the game items carried
/// above their normal limit are written with the save and given back after a load (health once stats are rebuilt, items after the game
/// has trimmed them). A co-op client also keeps its own copy of the character, so it survives the host quitting first.
/// </summary>
internal sealed class SaveExtrasFeature : IModFeature
{
    /// <summary>Seconds after load before health comes back (stats rebuilt) and before carried items are topped up (the game trims them while loading).</summary>
    internal const float HealthDelay = 3f, ItemsDelay = 12f;

    private static PlayerSave? _pending;
    private static float _sinceLoad = -1, _sinceBackup;
    private static bool _healthDone, _itemsDone, _loadedThisWorld, _backupChecked;
    /// <summary>Set while playing as a client: when the host drops us, the session is already gone by the time the world exits.</summary>
    private static bool _wasClient;

    /// <summary>Items given back by the last restore (self-test).</summary>
    internal static Dictionary<int, int> LastRestored { get; } = new();

    public void Init()
    {
        Application.add_quitting((Action)(() => WriteBackup("quit")));
        SonsSdk.SdkEvents.OnWorldExited.Subscribe(() =>
        {
            WriteBackup("world exited");
            _loadedThisWorld = false;
            _backupChecked = false;
            _wasClient = false;
        });
    }

    /// <summary>Fills the save with the parts the rules character does not know.</summary>
    public static void Capture(PlayerSave save)
    {
        save.SavedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        try
        {
            var h = LocalPlayer.Vitals?.Health;
            if (h != null && h.GetMax() > 0) save.HealthFraction = Math.Clamp(h._currentValue / h.GetMax(), 0, 1);
            var inv = LocalPlayer.Inventory;
            if (inv != null)
                foreach (int id in Followups.FollowupsFeature.RaisedItems)
                {
                    int n = inv.AmountOf(id, true, false);
                    if (n > 0) save.CarriedItems[id] = n;
                }
        }
        catch (Exception e)
        {
            RLog.Warning($"Champions of the Sons: could not read health or carried items for the save: {e.Message}");
        }
    }

    /// <summary>A character was loaded from a save: give back health and carried items once the game has settled.</summary>
    public static void Loaded(PlayerSave save)
    {
        _pending = save;
        _sinceLoad = 0;
        _healthDone = save.HealthFraction < 0;
        _itemsDone = save.CarriedItems.Count == 0;
        _loadedThisWorld = true;
    }

    /// <summary>On a client, the newer of the host's copy and our own backup.</summary>
    public static PlayerSave PickForClient(PlayerSave fromHost)
    {
        if (!CoopSession.IsClient) return fromHost;
        var backup = ReadBackup();
        var pick = ClientBackup.Pick(fromHost, backup);
        if (!ReferenceEquals(pick, fromHost)) RLog.Msg("Champions of the Sons: the host's copy of this character is older than the local backup; loading the backup.");
        return pick;
    }

    public void Update(float dt, bool inWorld)
    {
        if (!inWorld) return;
        if (_pending != null && _sinceLoad >= 0)
        {
            _sinceLoad += dt > 0 ? dt : Time.unscaledDeltaTime;
            if (!_healthDone && _sinceLoad >= HealthDelay) { _healthDone = true; RestoreHealth(_pending.HealthFraction); }
            if (!_itemsDone && _sinceLoad >= ItemsDelay) { _itemsDone = true; RestoreItems(_pending.CarriedItems); }
            if (_healthDone && _itemsDone) { _pending = null; _sinceLoad = -1; }
        }
        if (!CoopSession.IsClient) return;
        _wasClient = true;
        // A host that never saved has no copy of us at all: start from our backup instead of a new character.
        if (!_backupChecked)
        {
            _backupChecked = true;
            if (!_loadedThisWorld && ReadBackup() is { } backup)
            {
                RLog.Msg("Champions of the Sons: this host has no copy of the character; loading the local backup.");
                PlayerState.FromSave(backup);
                Loaded(backup);
            }
        }
        _sinceBackup += Time.unscaledDeltaTime;
        if (_sinceBackup >= ClientBackup.Interval) { _sinceBackup = 0; WriteBackup("interval"); }
    }

    internal static void RestoreHealth(double fraction)
    {
        var h = LocalPlayer.Vitals?.Health;
        if (h == null || fraction <= 0) return;
        h.SetCurrentValue((float)(fraction * h.GetMax()));
    }

    internal static void RestoreItems(IReadOnlyDictionary<int, int> carried)
    {
        LastRestored.Clear();
        var inv = LocalPlayer.Inventory;
        if (inv == null) return;
        Followups.FollowupsFeature.ApplyCapacities(force: true);
        foreach (var (id, count) in carried)
        {
            try
            {
                int missing = count - inv.AmountOf(id, true, false);
                int n = Math.Min(missing, inv.GetAvailableSpaceForItem(id));
                if (n > 0 && inv.AddItem(id, n, false, false, null)) LastRestored[id] = n;
            }
            catch (Exception e)
            {
                RLog.Warning($"Champions of the Sons: could not give back carried item {id}: {e.Message}");
            }
        }
        if (LastRestored.Count > 0) RLog.Msg($"Champions of the Sons: gave back carried items the load trimmed: {string.Join(", ", LastRestored.Select(kv => $"{kv.Key} x{kv.Value}"))}");
    }

    // ---------------------------------------------------------------- client backup

    private static string? BackupPath()
    {
        string key;
        try { key = GameSetupManager.GetGameId().ToString(); }
        catch { return null; }
        if (string.IsNullOrWhiteSpace(key)) return null;
        return Path.Combine(Application.persistentDataPath, "ChampionsOfTheSons", ClientBackup.FileName(key));
    }

    /// <summary>Writes the client's own copy of the character (also on world exit and quit, after the host dropped us).</summary>
    internal static void WriteBackup(string why)
    {
        try
        {
            if (!CoopSession.IsClient && !_wasClient) return;
            var path = BackupPath();
            if (path == null) return;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, SaveCodec.Write(PlayerState.ToSave()));
            RLog.Msg($"Champions of the Sons: client character backed up ({why}).");
        }
        catch (Exception e)
        {
            RLog.Warning($"Champions of the Sons: client backup failed: {e.Message}");
        }
    }

    private static PlayerSave? ReadBackup()
    {
        try
        {
            var path = BackupPath();
            return path != null && File.Exists(path) ? SaveCodec.ReadPlayer(File.ReadAllText(path)) : null;
        }
        catch (Exception e)
        {
            RLog.Warning($"Champions of the Sons: client backup unreadable: {e.Message}");
            return null;
        }
    }

    public void RegisterSelfTests() => SaveExtrasSelfTest.Register();
}
