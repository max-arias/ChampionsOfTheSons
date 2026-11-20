using RedLoader;

namespace ChampionsOfTheSons.Mod;

/// <summary>
/// A self-contained piece of the mod (loot, HUD, perks UI...). Implement this in your own folder and it is
/// discovered and wired automatically — no edits to <see cref="ChampionsOfTheSonsMod"/> needed, so parallel
/// work never conflicts on the entry point. Every member is optional.
/// </summary>
public interface IModFeature
{
    /// <summary>After content, saves and networking are set up (OnSdkInitialized).</summary>
    void Init() { }

    /// <summary>Every frame. <paramref name="inWorld"/>: the local player is in the world.</summary>
    void Update(float dt, bool inWorld) { }

    /// <summary>Once the player spawns and gains control.</summary>
    void OnGameStart() { }

    /// <summary>Only when --cots-selftest is on: add steps with <see cref="SelfTest.Add"/>.</summary>
    void RegisterSelfTests() { }
}

public static class Features
{
    private static readonly List<IModFeature> All = new();

    public static IReadOnlyList<IModFeature> Loaded => All;

    /// <summary>Finds every non-abstract IModFeature with a parameterless constructor, ordered by type name.</summary>
    public static void Discover(bool dedicatedServer = false)
    {
        // A dedicated server has no screen, camera or player body: only the networking, loot-rule and admin-command features run
        // there, plus the enemy ability loops (the host owns enemies: their casts reach the clients from there, COS-79).
        var types = typeof(Features).Assembly.GetTypes()
            .Where(t => typeof(IModFeature).IsAssignableFrom(t) && !t.IsAbstract && !t.IsInterface && t.GetConstructor(Type.EmptyTypes) != null)
            .Where(t => !dedicatedServer || t.Namespace is "ChampionsOfTheSons.Mod.Net" or "ChampionsOfTheSons.Mod.Loot" or "ChampionsOfTheSons.Mod.Admin"
                || t == typeof(Bridge.EnemyAbilityFeature) || t == typeof(Abilities.ActiveAbilitiesFeature))
            .OrderBy(t => t.FullName, StringComparer.Ordinal);
        foreach (var t in types) All.Add((IModFeature)Activator.CreateInstance(t)!);
        UpdateSlots = All.Select(f => Perf.Perf.Register(f.GetType().Name + ".Update", true)).ToArray();
        RLog.Msg($"Champions of the Sons features: {string.Join(", ", All.Select(f => f.GetType().Name))}");
    }

    private static int[] UpdateSlots = Array.Empty<int>();

    /// <summary>Every feature's Update, each timed when the profiler is on (COS-132).</summary>
    public static void UpdateAll(float dt, bool inWorld)
    {
        for (int i = 0; i < All.Count; i++)
        {
            using var probe = Perf.Perf.Scope(UpdateSlots[i]);
            try
            {
                All[i].Update(dt, inWorld);
            }
            catch (Exception e)
            {
                RLog.Error($"Champions of the Sons: {All[i].GetType().Name}.Update failed: {e}");
            }
        }
    }

    public static void Each(string what, Action<IModFeature> action)
    {
        foreach (var f in All)
        {
            try
            {
                action(f);
            }
            catch (Exception e)
            {
                RLog.Error($"Champions of the Sons: {f.GetType().Name}.{what} failed: {e}");
            }
        }
    }
}
