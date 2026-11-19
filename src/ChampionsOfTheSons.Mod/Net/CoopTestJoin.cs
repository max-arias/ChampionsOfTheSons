using RedLoader;
using Sons.Gui;
using Sons.Multiplayer;
using Sons.Multiplayer.Dedicated;
using Steamworks;
using UnityEngine;

namespace ChampionsOfTheSons.Mod.Net;

/// <summary>
/// Test loop for co-op: with the launch option <c>--cots-join &lt;ip:port&gt;</c> the client sits on the title screen,
/// asks Steam for the dedicated server at that address and joins it (no human at the menu).
/// Used by tools/run-coop-test.ps1; ignored when the option is absent.
/// </summary>
public sealed class CoopTestJoin : IModFeature
{
    public const string Tag = "[COTS-COOP]";

    /// <summary>The ip:port given with --cots-join, or null.</summary>
    public static string? Target { get; } = ReadTarget();

    private float _next = 15f;
    private bool _requested;
    private bool _connected;
    private float _started = -1;

    private float _dialogCheck;

    /// <summary>The game asks "join this server?" in a dialog when started with +connect_ds; nobody is there to click it.</summary>
    private void AcceptJoinDialog()
    {
        if (Time.realtimeSinceStartup < _dialogCheck) return;
        _dialogCheck = Time.realtimeSinceStartup + 2f;
        try
        {
            foreach (var d in Resources.FindObjectsOfTypeAll<DedicatedGameInviteDialogController>())
            {
                if (d == null || !d.gameObject.activeInHierarchy) continue;
                RLog.Msg($"{Tag} join: accepting the game's join dialog");
                d.OnOption1();
                _connected = true;
                return;
            }
        }
        catch (Exception e)
        {
            RLog.Warning($"{Tag} join dialog check failed: {e.Message}");
        }
    }

    private static string? ReadTarget()
    {
        var args = Environment.GetCommandLineArgs();
        int i = Array.IndexOf(args, "--cots-join");
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    public void Update(float dt, bool inWorld)
    {
        if (Target == null || inWorld || _connected) return;
        if (_started < 0) _started = Time.realtimeSinceStartup;
        AcceptJoinDialog();
        if (Time.realtimeSinceStartup - _started < _next) return;
        _next += 10f;
        try
        {
            RLog.Msg($"{Tag} join: scene '{UnityEngine.SceneManagement.SceneManager.GetActiveScene().name}', dialogs: {Resources.FindObjectsOfTypeAll<DedicatedGameInviteDialogController>().Length}");
            var browser = Resources.FindObjectsOfTypeAll<DedicatedServerBrowser>().FirstOrDefault();
            if (browser == null)
            {
                RLog.Msg($"{Tag} join: no DedicatedServerBrowser in the scene yet");
                return;
            }
            if (_requested) return;
            _requested = true;
            RLog.Msg($"{Tag} join: querying {Target}");
            DedicatedServerByIpPortRequest.RunAsync(Target, (Il2CppSystem.Action<gameserveritem_t>)(gsi =>
            {
                RLog.Msg($"{Tag} join: server found '{gsi.GetServerName()}' {gsi.m_nPlayers}/{gsi.m_nMaxPlayers}; connecting");
                _connected = true;
                browser.Connect(gsi);
            }), (Il2CppSystem.Action)(() =>
            {
                RLog.Error($"{Tag} join: server query failed");
                _requested = false;
            }));
        }
        catch (Exception e)
        {
            RLog.Error($"{Tag} join failed: {e}");
            _requested = false;
        }
    }
}
