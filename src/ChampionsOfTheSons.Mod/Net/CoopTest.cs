using RedLoader;
using UnityEngine;

namespace ChampionsOfTheSons.Mod.Net;

/// <summary>
/// Client-side steps of the server+client co-op test (tools/run-coop-test.ps1). Steps run in order once the
/// host has accepted the handshake; every line is tagged <see cref="CoopTestJoin.Tag"/>; the run ends with
/// "COOP CLIENT DONE failures=N" and quits the game. A step returns true when it is finished, false to be called again
/// next frame (until its timeout).
/// </summary>
public static class CoopTest
{
    private sealed record Step(string Name, float Timeout, Func<bool> Run);

    private static readonly List<Step> Steps = new();
    private static int _next;
    private static int _failures;
    private static float _stepStart = -1;
    private static bool _finished;

    public static bool Enabled => CoopTestJoin.Target != null;

    /// <summary>Registers a step. Return true when done; throw or time out to fail.</summary>
    public static void Add(string name, Func<bool> run, float timeout = 20f) => Steps.Add(new Step(name, timeout, run));

    public static void Check(string name, bool ok, string detail = "")
    {
        if (!ok) _failures++;
        RLog.Msg($"{CoopTestJoin.Tag} {(ok ? "PASS" : "FAIL")} {name}{(detail.Length > 0 ? " - " + detail : "")}");
    }

    public static void Update()
    {
        if (!Enabled || _finished || !CoopFeature.Accepted) return;
        if (_next >= Steps.Count) { Finish(); return; }
        if (_stepStart < 0) _stepStart = Time.realtimeSinceStartup;
        var step = Steps[_next];
        bool done;
        try
        {
            done = step.Run();
        }
        catch (Exception e)
        {
            Check(step.Name, false, e.GetType().Name + ": " + e.Message);
            done = true;
            _next++;
            _stepStart = -1;
            return;
        }
        if (!done && Time.realtimeSinceStartup - _stepStart > step.Timeout)
        {
            Check(step.Name, false, "timed out");
            done = true;
        }
        if (done)
        {
            _next++;
            _stepStart = -1;
        }
    }

    private static void Finish()
    {
        if (_finished) return;
        _finished = true;
        RLog.Msg($"{CoopTestJoin.Tag} COOP CLIENT DONE failures={_failures}");
        Application.Quit();
    }
}
