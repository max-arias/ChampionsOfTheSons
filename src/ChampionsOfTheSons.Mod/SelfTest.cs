using RedLoader;
using TheForest.Utils;
using UnityEngine;

namespace ChampionsOfTheSons.Mod;

/// <summary>
/// Automated in-game checks for a fast feedback loop without a human at the keyboard.
/// Enabled with the launch option <c>--cots-selftest</c> (combine with <c>--savegame &lt;id&gt;</c> to skip menus).
/// Every line is tagged <see cref="Tag"/> in the RedLoader log; the run ends with SELFTEST DONE and quits the game.
/// </summary>
public static class SelfTest
{
    public const string Tag = "[COTS-SELFTEST]";

    private static readonly List<(float At, string Name, Action Step)> Steps = new();
    private static float _start = -1;
    private static int _next;
    private static int _failures;
    private static bool _sorted;

    public static bool Enabled { get; } = Environment.GetCommandLineArgs().Any(a => a == "--cots-selftest");

    /// <summary>Seconds to wait after the player is in the world before the first step.</summary>
    private const float SettleTime = 8f;

    /// <summary>Hard limit so a stuck run never keeps the game open.</summary>
    private const float Timeout = 240f;

    private static readonly float LaunchTime = Time.realtimeSinceStartup;

    public static void Log(string msg) => RLog.Msg($"{Tag} {msg}");

    public static void Check(string name, bool ok, string detail = "")
    {
        if (!ok) _failures++;
        Log($"{(ok ? "PASS" : "FAIL")} {name}{(detail.Length > 0 ? " — " + detail : "")}");
    }

    /// <summary>Registers a step to run <paramref name="delay"/> seconds after the world is ready.</summary>
    public static void Add(float delay, string name, Action step) => Steps.Add((delay, name, step));

    public static void RegisterDefaultSteps()
    {
        Add(0, "world", () =>
        {
            Check("player in world", LocalPlayer.IsInWorld);
            Log($"player at {LocalPlayer.Transform.position}");
            Check("content loaded", ModContent.Db.Errors.Count == 0, ModContent.Db.Summary());
        });
    }

    /// <summary>Called every frame by the mod while enabled.</summary>
    public static void Update()
    {
        if (!Enabled) return;
        if (Time.realtimeSinceStartup - LaunchTime > Timeout)
        {
            Log($"TIMEOUT after {Timeout}s (player in world: {LocalPlayer.IsInWorld})");
            Finish();
            return;
        }
        if (!LocalPlayer.IsInWorld) return;
        if (_start < 0)
        {
            _start = Time.realtimeSinceStartup + SettleTime;
            Log("player in world, starting steps soon");
        }
        if (!_sorted)
        {
            _sorted = true;
            var ordered = Steps.Select((s, i) => (s, i)).OrderBy(x => x.s.At).ThenBy(x => x.i).Select(x => x.s).ToList();
            Steps.Clear();
            Steps.AddRange(ordered);
        }
        float t = Time.realtimeSinceStartup - _start;
        while (_next < Steps.Count && t >= Steps[_next].At)
        {
            var (_, name, step) = Steps[_next++];
            try
            {
                step();
            }
            catch (Exception e)
            {
                Check(name, false, e.GetType().Name + ": " + e.Message);
            }
        }
        if (_next >= Steps.Count && t >= 0) Finish();
    }

    private static bool _finished;

    private static void Finish()
    {
        if (_finished) return;
        _finished = true;
        Log($"SELFTEST DONE failures={_failures}");
        Application.Quit();
    }
}
