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
    /// <summary>Seconds the timeline has been pushed back because a phase started late (see <see cref="Update"/>).</summary>
    private static float _lag;
    private static float _start = -1;
    private static int _next;
    private static int _failures;
    private static bool _sorted;

    public static bool Enabled { get; } = Environment.GetCommandLineArgs().Any(a => a == "--cots-selftest");

    /// <summary>Seconds to wait after the player is in the world before the first step.</summary>
    private const float SettleTime = 8f;

    /// <summary>Hard limit so a stuck run never keeps the game open (below run-selftest.ps1's 1200 s default).</summary>
    private const float Timeout = 1140f;

    private static readonly float LaunchTime = Time.realtimeSinceStartup;

    public static void Log(string msg) => RLog.Msg($"{Tag} {msg}");

    public static void Check(string name, bool ok, string detail = "")
    {
        if (!ok) _failures++;
        Log($"{(ok ? "PASS" : "FAIL")} {name}{(detail.Length > 0 ? " — " + detail : "")}");
    }

    /// <summary>Registers a step to run <paramref name="delay"/> seconds after the world is ready.</summary>
    public static void Add(float delay, string name, Action step) => Steps.Add((_phaseStart + delay, name, step));

    private static float _phaseStart;

    /// <summary>Raised at the start of every phase so features can close panels and clear their own state.</summary>
    public static event Action? PhaseReset;

    /// <summary>
    /// Starts a new phase: its steps run after every step registered so far (plus a gap), on a fresh character,
    /// so features' checks never interleave on shared state (one feature's respec emptying another's spell bar).
    /// </summary>
    public static void BeginPhase(string name)
    {
        float end = Steps.Count == 0 ? 0 : Steps.Max(s => s.At);
        _phaseStart = Steps.Count == 0 ? 0 : end + 3;
        Steps.Add((_phaseStart, "phase " + name, () =>
        {
            Log($"--- phase {name} ---");
            PhaseReset?.Invoke();
            PlayerState.FromSave(new Core.Saving.PlayerSave());
        }));
        _phaseStart += 1;
    }

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
        // Wait for the world once; after that keep going (holding the guide book clears IsInWorld, COS-62).
        if (_start < 0 && !LocalPlayer.IsInWorld) return;
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
        // Re-read the clock before every step: some steps block for seconds (simulated frames), and a stale t would
        // hide how late the next phase really is.
        float Now() => Time.realtimeSinceStartup - _start - _lag;
        float t = Now();
        while (_next < Steps.Count && (t = Now()) >= Steps[_next].At)
        {
            var (at, name, step) = Steps[_next++];
            // A phase that starts late (the one before ran long or the game hitched) would otherwise fire all of its
            // steps in one frame: push the rest of the timeline back so every phase keeps its own spacing.
            if (name.StartsWith("phase ", StringComparison.Ordinal) && t - at > 0.5f)
            {
                Log($"{name} started {t - at:0.0} s late; later steps shifted");
                _lag += t - at;
                t = at;
            }
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
