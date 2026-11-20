using ChampionsOfTheSons.Mod.Bridge;
using ChampionsOfTheSons.Mod.Loot;
using Sons.Ai.Vail;
using TheForest.Utils;
using UnityEngine;

namespace ChampionsOfTheSons.Mod.Perf;

/// <summary>
/// A busy scene measured with the profiler (COS-132): ~15 enemies in front of the player, a few ground drops and the HUD up, then
/// <see cref="Seconds"/> of frames. Logs the table and checks the mod's own update stays inside the budget. Run it windowed
/// (<c>run-selftest.ps1 -Only PerfFeature</c> without -Headless) for real frame times.
/// </summary>
internal static class PerfSelfTest
{
    private const float Seconds = 15f;

    /// <summary>Mod Update loop budget, ms per frame (average and p99), at 60 fps a frame is 16.7 ms.</summary>
    private const double BudgetAvgMs = 1.0, BudgetP99Ms = 3.0;

    private static readonly List<VailActor> Spawned = new();

    public static void Register()
    {
        SelfTest.Add(0.5f, "perf: busy scene", Setup);
        SelfTest.Add(5f, "perf: start", Start);
        SelfTest.Add(5f + Seconds, "perf: report", Report);
        SelfTest.Add(6f + Seconds, "perf: cleanup", Cleanup);
        SelfTest.Add(9f + Seconds, "perf: clean", Clean);
    }

    /// <summary>While the scene runs (setup to cleanup) the player cannot be hurt: the unpaused enemies attack, and a dead or
    /// wounded player breaks every later phase (spells return NotInWorld).</summary>
    internal static bool Protecting { get; private set; }

    /// <summary>Every frame from <see cref="PerfFeature"/> while <see cref="Protecting"/>.</summary>
    internal static void Protect()
    {
        var health = LocalPlayer.Vitals?.Health;
        if (health != null && health._currentValue > 0 && health._currentValue < health.GetMax()) health.SetCurrentValue(health.GetMax());
    }

    private static void Setup()
    {
        Protecting = true;
        TestKit.Unpause();
        Spawned.Clear();
        for (int i = 0; i < 15; i++)
        {
            var type = i % 5 == 4 ? VailActorTypeId.Billy : VailActorTypeId.Carl;
            var a = TestKit.Spawn(type, distance: 10 + (i / 5) * 6, sideways: (i % 5 - 2) * 4f);
            if (a != null) Spawned.Add(a);
        }
        var at = LocalPlayer.Transform.position + LocalPlayer.Transform.forward * 3;
        for (int i = 0; i < 4; i++) LootFeature.SpawnScattered(TestKit.Give(rarity: 2 + i % 3), at);
        SelfTest.Check("perf: enemies spawned", Spawned.Count >= 10, $"{Spawned.Count} spawned, {EnemyRegistry.Count} tracked");
    }

    private static void Start()
    {
        PerfFeature.Manual = true;
        Perf.Enabled = true;
        Perf.Reset();
        SelfTest.Log($"perf: measuring {Seconds} s, {EnemyRegistry.Count} enemies tracked, HUD shown {Hud.HudFeature.Instance?.Shown}");
    }

    private static void Report()
    {
        var f = Perf.Frames();
        foreach (var line in Perf.Report().Split('\n')) SelfTest.Log(line.TrimEnd('\r'));
        Perf.Enabled = false;
        PerfFeature.Manual = false;
        SelfTest.Check("perf: frames measured", f.Frames > 30, $"{f.Frames} frames");
        SelfTest.Check("perf: mod update within budget", f.ModAvg <= BudgetAvgMs && f.ModP99 <= BudgetP99Ms,
            $"avg {f.ModAvg:0.000} ms (budget {BudgetAvgMs}), p99 {f.ModP99:0.000} ms (budget {BudgetP99Ms})");
    }

    private static void Cleanup()
    {
        foreach (var a in Spawned)
        {
            try
            {
                if (a != null && !a.IsDead()) TestKit.Kill(a);
            }
            catch
            {
                // already gone
            }
        }
        Spawned.Clear();
        LootFeature.ClearAll();
    }

    /// <summary>Kill loot can land a frame or more after the kill: clear again, heal, and check the next phase gets a clean world.</summary>
    private static void Clean()
    {
        LootFeature.ClearAll();
        Protecting = false;
        var health = LocalPlayer.Vitals?.Health;
        if (health != null && health._currentValue > 0) health.SetCurrentValue(health.GetMax());
        int alive = EnemyRegistry.Live.Count(r => r.IsActive);
        SelfTest.Check("perf: scene cleaned up", LocalPlayer.IsInWorld && health != null && health._currentValue > 0 && LootFeature.Ground.All.Count() == 0,
            $"in world {LocalPlayer.IsInWorld}, health {health?._currentValue:0}, drops {LootFeature.Ground.All.Count()}, active enemies {alive}");
    }
}
