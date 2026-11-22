using Sons.Items.Core;
using Sons.Weapon;
using TheForest.Utils;

namespace ChampionsOfTheSons.Mod.ItemSweep;

/// <summary>
/// Crash sweep over every item in the game (2026-11-22, after a firearm shot crashed the game): add it, equip it, use it
/// (fire, swing or eat), put it away. Only for runs that name it (<c>-Only ItemSweepFeature</c>); it is slow.
/// Progress goes to <c>%TEMP%\cots-item-sweep.txt</c>: "start id" before an item, "ok id" after. A run that dies leaves a
/// "start" with no "ok"; the next run marks that item "crash" and carries on after it, so repeated runs cover every item.
/// Logs use item ids only.
/// </summary>
public sealed class ItemSweepFeature : IModFeature
{
    private const int PerRun = 300;
    private const float Spacing = 2.5f;
    private static readonly string ProgressFile = Path.Combine(Path.GetTempPath(), "cots-item-sweep.txt");

    private static readonly List<int> Todo = new();
    private static List<int> _all = new();

    public void RegisterSelfTests()
    {
        // The item database is filled once the world loads: the list is built in the first step, the slots are fixed.
        SelfTest.Add(2f, "sweep: setup", Setup);
        for (int i = 0; i < PerRun; i++)
        {
            int slot = i;
            float t = 3f + i * Spacing;
            SelfTest.Add(t, $"sweep: slot {slot} equip", () => { if (slot < Todo.Count) Begin(Todo[slot]); });
            SelfTest.Add(t + 1.2f, $"sweep: slot {slot} use", () => { if (slot < Todo.Count) Use(Todo[slot]); });
            SelfTest.Add(t + 2.2f, $"sweep: slot {slot} away", () => { if (slot < Todo.Count) End(Todo[slot]); });
        }
        SelfTest.Add(3f + PerRun * Spacing + 0.5f, "sweep: summary", () =>
        {
            var lines = File.Exists(ProgressFile) ? File.ReadAllLines(ProgressFile) : Array.Empty<string>();
            int ok = lines.Count(l => l.StartsWith("ok ")), crash = lines.Count(l => l.StartsWith("crash "));
            var covered = lines.Where(l => !l.StartsWith("start ")).Select(l => l.Split(' ')[1]).ToHashSet();
            int left = _all.Count(i => !covered.Contains(i.ToString()));
            SelfTest.Log($"sweep: {ok} ok, {crash} crashed, {left} left of {_all.Count}");
            SelfTest.Check("sweep: no item crashed the game", crash == 0, string.Join(" ", lines.Where(l => l.StartsWith("crash "))));
        });
    }

    private static void Setup()
    {
        TestKit.Unpause();
        var done = new HashSet<int>();
        int? open = null;
        if (File.Exists(ProgressFile))
            foreach (var line in File.ReadAllLines(ProgressFile))
            {
                var parts = line.Split(' ');
                if (parts.Length != 2 || !int.TryParse(parts[1], out var id)) continue;
                if (parts[0] == "start") open = id;
                else { done.Add(id); if (open == id) open = null; }
            }
        if (open is int crashed)
        {
            File.AppendAllText(ProgressFile, $"crash {crashed}\n");
            done.Add(crashed);
            SelfTest.Log($"sweep: item {crashed} crashed the last run");
        }
        var ids = new List<int>();
        try
        {
            foreach (var it in ItemDatabaseManager.Items)
            {
                if (it == null) continue;
                ids.Add(it.Id);
                // Ammo for every firearm and launcher, so a shot really fires.
                if ((it.Name ?? "").Contains("Ammo", StringComparison.OrdinalIgnoreCase))
                    try { LocalPlayer.Inventory?.AddItem(it.Id, 20, true, false, null); } catch { /* not addable */ }
            }
        }
        catch (Exception e)
        {
            SelfTest.Log("sweep: no item list " + e.Message);
        }
        _all = ids.Distinct().OrderBy(i => i).ToList();
        Todo.Clear();
        Todo.AddRange(_all.Where(i => !done.Contains(i)).Take(PerRun));
        SelfTest.Log($"sweep: {_all.Count} items, {done.Count} done before, {Todo.Count} this run");
    }

    private static bool _added;

    private static void Begin(int id)
    {
        File.AppendAllText(ProgressFile, $"start {id}\n");
        var inv = LocalPlayer.Inventory;
        if (inv == null) return;
        Try(id, "stash", () => inv.StashHeldItems(false, true));
        _added = false;
        Try(id, "add", () => _added = inv.AmountOf(id, true, false) > 0 || inv.AddItem(id, 1, true, false, null));
        if (_added) Try(id, "equip", () => inv.TryEquip(id, false, true, false));
    }

    private static void Use(int id)
    {
        var inv = LocalPlayer.Inventory;
        var player = LocalPlayer.Transform;
        if (inv == null || player == null || !_added) return;
        var ranged = player.GetComponentInChildren<RangedWeapon>(false);
        if (ranged != null && inv.IsRightHandHolding(id)) { Try(id, "shoot", () => ranged.Shoot()); return; }
        var melee = player.GetComponentInChildren<MeleeWeaponController>(false);
        if (melee != null && inv.IsRightHandHolding(id)) { Try(id, "swing", () => melee.AttackButtonDown()); return; }
        if (inv.HasInAnySlot(id)) return; // worn or held with no attack
        Try(id, "consume", () =>
        {
            var action = player.GetComponentInChildren<TheForest.Player.Actions.PlayerConsumeItemAction>(true);
            var instance = inv.GetItemInstanceOfType(id);
            var data = ItemDatabaseManager.ItemById(id);
            bool edible = data != null && (data._consumeAnimationType.ToString() != "None" || (data._usedStatEffect?.Length ?? 0) > 0);
            if (action != null && instance != null && edible) action.ConsumeItem(instance, false, null);
        });
    }

    private static void End(int id)
    {
        var inv = LocalPlayer.Inventory;
        if (inv != null)
        {
            Try(id, "stash", () => inv.StashHeldItems(false, true));
            Try(id, "remove", () =>
            {
                int n = inv.AmountOf(id, true, false);
                if (n > 0 && !(ItemDatabaseManager.ItemById(id)?.Name ?? "").Contains("Ammo", StringComparison.OrdinalIgnoreCase))
                    inv.RemoveItem(id, n, false, false, false, null, false);
            });
        }
        TestKit.Unpause();
        File.AppendAllText(ProgressFile, $"ok {id}\n");
    }

    /// <summary>A managed exception is not a crash: logged by id and the sweep goes on.</summary>
    private static void Try(int id, string what, Action a)
    {
        try { a(); }
        catch (Exception e) { SelfTest.Log($"sweep: {id} {what} threw {e.GetType().Name}: {e.Message.Split('\n')[0]}"); }
    }
}
