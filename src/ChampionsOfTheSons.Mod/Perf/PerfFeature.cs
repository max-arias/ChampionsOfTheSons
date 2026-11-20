using RedLoader;
using SonsSdk;

namespace ChampionsOfTheSons.Mod.Perf;

/// <summary>
/// Turns the frame-cost profiler on from the config (<c>perf_log</c>, off by default) or the launch option <c>--cots-perf</c>, and
/// logs the table every <see cref="Window"/> seconds of real time (COS-132).
/// </summary>
public sealed class PerfFeature : IModFeature
{
    public const float Window = 10f;

    public static ConfigEntry<bool> LogEnabled { get; private set; } = null!;

    /// <summary>Self-tests drive the profiler themselves: no periodic log while set.</summary>
    internal static bool Manual { get; set; }

    private float _elapsed;
    private bool _was;

    public void Init()
    {
        LogEnabled = Config.Category.CreateEntry("perf_log", false, "Performance log",
            "Log the mod's frame cost per feature (avg, p99, max ms and allocations) to the RedLoader log every 10 seconds.");
        if (Environment.GetCommandLineArgs().Any(a => a == "--cots-perf")) LogEnabled.Value = true;
    }

    public void Update(float dt, bool inWorld)
    {
        if (Manual) return;
        bool on = LogEnabled.Value;
        if (on != _was)
        {
            _was = on;
            Perf.Enabled = on;
            Perf.Reset();
            _elapsed = 0;
            RLog.Msg($"COTS perf log {(on ? "on" : "off")}");
        }
        if (!on || !inWorld) return;
        _elapsed += UnityEngine.Time.unscaledDeltaTime;
        if (_elapsed < Window) return;
        _elapsed = 0;
        Perf.LogAndReset();
    }

    public void RegisterSelfTests() => PerfSelfTest.Register();
}
