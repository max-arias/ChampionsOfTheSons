using System.Diagnostics;
using System.Text;
using RedLoader;

namespace ChampionsOfTheSons.Mod.Perf;

/// <summary>
/// Opt-in frame-cost profiler (COS-132). Off by default: every probe is one bool check. When on, each named slot (a feature's
/// Update, a Harmony hook) records the wall time and the managed bytes allocated per call, and <see cref="Report"/> turns a
/// window of samples into avg / p99 / max per call plus the cost per frame. Main thread only.
/// </summary>
public static class Perf
{
    public static bool Enabled { get; set; }

    private sealed class Slot
    {
        public string Name = "";
        public readonly List<float> Ms = new(4096);
        public double TotalMs;
        public long Bytes;
        public int Calls;
        public bool TopLevel;
    }

    private static readonly List<Slot> Slots = new();
    private static readonly Dictionary<string, int> ByName = new();
    private static readonly List<float> FrameMs = new(4096);
    private static long _modTicksThisFrame;
    private static readonly List<float> ModFrameMs = new(4096);
    private static int _gen0Start, _gen1Start, _gen2Start;
    private static long _il2cppHeapStart;

    /// <summary>A slot id for a name; call once (static field) so the hot path needs no lookup. <paramref name="topLevel"/>: part of
    /// the mod's own Update loop, so it counts toward "mod update per frame" (hooks run inside the game's code and are listed alone).</summary>
    public static int Register(string name, bool topLevel = false)
    {
        if (ByName.TryGetValue(name, out int id)) return id;
        id = Slots.Count;
        Slots.Add(new Slot { Name = name, TopLevel = topLevel });
        ByName[name] = id;
        return id;
    }

    /// <summary>A timing scope: <c>using var _ = Perf.Scope(slot);</c>. A struct, so it allocates nothing.</summary>
    public static Probe Scope(int slot) => Enabled ? new Probe(slot, Stopwatch.GetTimestamp(), GC.GetAllocatedBytesForCurrentThread()) : default;

    public readonly struct Probe : IDisposable
    {
        private readonly int _slot;
        private readonly long _start, _bytes;

        internal Probe(int slot, long start, long bytes)
        {
            _slot = slot + 1; // 0 = disabled (default struct)
            _start = start;
            _bytes = bytes;
        }

        public void Dispose()
        {
            if (_slot == 0) return;
            long ticks = Stopwatch.GetTimestamp() - _start;
            long bytes = GC.GetAllocatedBytesForCurrentThread() - _bytes;
            Record(_slot - 1, ticks, bytes);
        }
    }

    private static void Record(int slot, long ticks, long bytes)
    {
        var s = Slots[slot];
        double ms = ticks * 1000.0 / Stopwatch.Frequency;
        s.Ms.Add((float)ms);
        s.TotalMs += ms;
        s.Bytes += bytes;
        s.Calls++;
        if (s.TopLevel) _modTicksThisFrame += ticks;
    }

    /// <summary>Once per frame, after the mod's Update loop.</summary>
    public static void EndFrame(float unscaledDt)
    {
        if (!Enabled) return;
        FrameMs.Add(unscaledDt * 1000f);
        ModFrameMs.Add((float)(_modTicksThisFrame * 1000.0 / Stopwatch.Frequency));
        _modTicksThisFrame = 0;
    }

    public static void Reset()
    {
        foreach (var s in Slots)
        {
            s.Ms.Clear();
            s.TotalMs = 0;
            s.Bytes = 0;
            s.Calls = 0;
        }
        FrameMs.Clear();
        ModFrameMs.Clear();
        _modTicksThisFrame = 0;
        _gen0Start = GC.CollectionCount(0);
        _gen1Start = GC.CollectionCount(1);
        _gen2Start = GC.CollectionCount(2);
        _il2cppHeapStart = Il2CppHeap();
    }

    private static long Il2CppHeap()
    {
        try
        {
            return UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong();
        }
        catch
        {
            return 0;
        }
    }

    public readonly record struct Summary(int Frames, double FrameAvg, double FrameP99, double FrameMax, double ModAvg, double ModP99, double ModMax);

    public static Summary Frames()
    {
        int n = FrameMs.Count;
        return new Summary(n, Avg(FrameMs), Pct(FrameMs, 0.99), Max(FrameMs), Avg(ModFrameMs), Pct(ModFrameMs, 0.99), Max(ModFrameMs));
    }

    /// <summary>Per-slot cost per frame (ms) in the current window, by name.</summary>
    public static double MsPerFrame(string name)
    {
        if (!ByName.TryGetValue(name, out int id) || FrameMs.Count == 0) return 0;
        return Slots[id].TotalMs / FrameMs.Count;
    }

    /// <summary>The window as a table (one line per slot, slowest per frame first). Does not reset.</summary>
    public static string Report()
    {
        var f = Frames();
        int frames = Math.Max(1, f.Frames);
        var sb = new StringBuilder();
        sb.AppendLine($"COTS perf: {f.Frames} frames, frame avg {f.FrameAvg:0.00} p99 {f.FrameP99:0.00} max {f.FrameMax:0.00} ms; " +
                      $"mod update avg {f.ModAvg:0.000} p99 {f.ModP99:0.000} max {f.ModMax:0.000} ms/frame; " +
                      $"GC gen0/1/2 {GC.CollectionCount(0) - _gen0Start}/{GC.CollectionCount(1) - _gen1Start}/{GC.CollectionCount(2) - _gen2Start}; " +
                      $"il2cpp heap {(Il2CppHeap() - _il2cppHeapStart) / 1024.0:+0;-0} KB");
        sb.AppendLine($"  {"slot",-34} {"ms/frame",9} {"calls/f",8} {"avg",8} {"p99",8} {"max",8} {"KB/frame",9}");
        foreach (var s in Slots.Where(s => s.Calls > 0).OrderByDescending(s => s.TotalMs))
        {
            sb.AppendLine($"  {s.Name,-34} {s.TotalMs / frames,9:0.000} {(double)s.Calls / frames,8:0.00} {s.TotalMs / s.Calls,8:0.000} " +
                          $"{Pct(s.Ms, 0.99),8:0.000} {Max(s.Ms),8:0.000} {s.Bytes / 1024.0 / frames,9:0.00}");
        }
        return sb.ToString().TrimEnd();
    }

    public static void LogAndReset()
    {
        foreach (var line in Report().Split('\n')) RLog.Msg(line.TrimEnd('\r'));
        Reset();
    }

    private static double Avg(List<float> v) => v.Count == 0 ? 0 : v.Average();
    private static double Max(List<float> v) => v.Count == 0 ? 0 : v.Max();

    private static double Pct(List<float> v, double p)
    {
        if (v.Count == 0) return 0;
        var sorted = v.ToArray();
        Array.Sort(sorted);
        return sorted[Math.Min(sorted.Length - 1, (int)Math.Ceiling(p * sorted.Length) - 1)];
    }
}
