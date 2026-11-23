using ChampionsOfTheSons.Core;
using ChampionsOfTheSons.Core.Rules;
using ChampionsOfTheSons.Mod.Spells;
using ChampionsOfTheSons.Mod.Weapons;
using Sons.Weapon;
using TheForest.Utils;

namespace ChampionsOfTheSons.Mod.ItemSweep;

/// <summary>
/// Crash sweep over the mod's own content (2026-11-22): every mod item rolled, equipped and (weapons) drawn and used,
/// then every spell slotted and cast at a nearby enemy. Only for runs that name it (<c>-Only ModSweepFeature</c>).
/// Same resume-after-crash progress file scheme as <see cref="ItemSweepFeature"/> (<c>%TEMP%\cots-mod-sweep.txt</c>,
/// keys "item:&lt;id&gt;" and "spell:&lt;id&gt;"). Logs use content ids only.
/// </summary>
public sealed class ModSweepFeature : IModFeature
{
    private const int PerRun = 320;
    private const float Spacing = 3f;
    private static readonly string ProgressFile = Path.Combine(Path.GetTempPath(), "cots-mod-sweep.txt");
    private static readonly List<string> Todo = new();
    private static List<string> _all = new();
    private static readonly Rng Rng = new(4242);
    private static string? _equippedSlot;
    private static Func<string, StatSheet, double>? _costScale;

    private static PlayerCharacter Pc => PlayerState.Character;

    public void RegisterSelfTests()
    {
        SelfTest.Add(2f, "mod sweep: setup", Setup);
        for (int i = 0; i < PerRun; i++)
        {
            int slot = i;
            float t = 4f + i * Spacing;
            SelfTest.Add(t, $"mod sweep: slot {slot} begin", () => { if (slot < Todo.Count) Begin(Todo[slot]); });
            SelfTest.Add(t + 1.3f, $"mod sweep: slot {slot} use", () => { if (slot < Todo.Count) Use(Todo[slot]); });
            SelfTest.Add(t + 2.6f, $"mod sweep: slot {slot} end", () => { if (slot < Todo.Count) End(Todo[slot]); });
        }
        SelfTest.Add(4f + PerRun * Spacing + 0.5f, "mod sweep: summary", () =>
        {
            var lines = File.Exists(ProgressFile) ? File.ReadAllLines(ProgressFile) : Array.Empty<string>();
            int ok = lines.Count(l => l.StartsWith("ok ")), crash = lines.Count(l => l.StartsWith("crash "));
            var covered = lines.Where(l => !l.StartsWith("start ")).Select(l => l.Split(' ')[1]).ToHashSet();
            int left = _all.Count(k => !covered.Contains(k));
            SelfTest.Log($"mod sweep: {ok} ok, {crash} crashed, {left} left of {_all.Count}");
            SelfTest.Check("mod sweep: nothing crashed the game", crash == 0, string.Join(" ", lines.Where(l => l.StartsWith("crash "))));
        });
    }

    private static void Setup()
    {
        TestKit.Unpause();
        PlayerState.FromSave(new Core.Saving.PlayerSave { Level = 60, Xp = 0 });
        Pc.Inventory.Restore(Array.Empty<KeyValuePair<string, ItemInstance>>());
        var done = new HashSet<string>();
        string? open = null;
        if (File.Exists(ProgressFile))
            foreach (var line in File.ReadAllLines(ProgressFile))
            {
                var parts = line.Split(' ');
                if (parts.Length != 2) continue;
                if (parts[0] == "start") open = parts[1];
                else { done.Add(parts[1]); if (open == parts[1]) open = null; }
            }
        if (open != null)
        {
            File.AppendAllText(ProgressFile, $"crash {open}\n");
            done.Add(open);
            SelfTest.Log($"mod sweep: {open} crashed the last run");
        }
        _all = ModContent.Db.Items.Keys.OrderBy(k => k, StringComparer.Ordinal).Select(k => "item:" + k)
            .Concat(ModContent.Db.Spells.Keys.OrderBy(k => k, StringComparer.Ordinal).Select(k => "spell:" + k)).ToList();
        Todo.Clear();
        Todo.AddRange(_all.Where(k => !done.Contains(k)).Take(PerRun));
        SelfTest.Log($"mod sweep: {_all.Count} entries, {done.Count} done before, {Todo.Count} this run");
    }

    private static void Begin(string key)
    {
        File.AppendAllText(ProgressFile, $"start {key}\n");
        Try(key, "stash", () => LocalPlayer.Inventory?.StashHeldItems(false, true));
        _equippedSlot = null;
        var id = key.Split(':', 2)[1];
        if (key.StartsWith("item:"))
        {
            Try(key, "equip", () =>
            {
                var def = ModContent.Db.Items[id];
                var item = new LootRoller(ModContent.Db).RollItem(def, 30, Rng);
                Pc.Inventory.AddToBag(item, def.StackSize);
                if (!LootRoller.IsEquippable(def)) return;
                var bag = Pc.Inventory.Slots.First(kv => kv.Key.StartsWith("bag.", StringComparison.Ordinal) && kv.Value == item).Key;
                if (!Pc.Inventory.Equip(bag, ModContent.Db)) return;
                _equippedSlot = Pc.Inventory.Slots.FirstOrDefault(kv => !kv.Key.StartsWith("bag.", StringComparison.Ordinal) && kv.Value == item).Key;
                if (def.Type != "weapon") return;
                WeaponSlotSync.Tick(); // the slot gives its game weapon and draws it
            });
        }
        else
        {
            Try(key, "slot", () =>
            {
                var stamina = LocalPlayer.Vitals?.Stamina;
                stamina?.SetCurrentValue(stamina.GetMax());
                // A fresh enemy close by for each spell (targeted ones need one in reach), and no cost: this checks that
                // every spell runs, not its price.
                TestKit.Spawn(distance: 5);
                _costScale ??= Pc.Spells.CostScale ?? ((_, _) => 1.0);
                Pc.Spells.CostScale = (_, _) => 0;
                Pc.Spells.Equip(0, id);
                Pc.Spells.Tick(1000, Pc.Stats);
            });
        }
    }

    private static void Use(string key)
    {
        var player = LocalPlayer.Transform;
        if (player == null) return;
        if (key.StartsWith("spell:"))
        {
            Try(key, "cast", () => SelfTest.Log($"mod sweep: {key} cast {SpellCaster.Cast(0)}"));
            return;
        }
        if (_equippedSlot == null) return;
        var ranged = player.GetComponentInChildren<RangedWeapon>(false);
        if (ranged != null) { Try(key, "shoot", () => ranged.Shoot()); return; }
        if (player.GetComponentInChildren<MeleeWeaponController>(false) != null) Try(key, "swing", TestKit.TapAttack);
    }

    private static void End(string key)
    {
        if (key.StartsWith("spell:"))
        {
            Try(key, "stop", () =>
            {
                if (Pc.Spells.IsActive(0)) SpellControl.StopSlot(0);
                FlickerRun.Cancel();
                Pc.Spells.Equip(0, null);
                if (_costScale != null) Pc.Spells.CostScale = _costScale;
            });
        }
        else
        {
            Try(key, "unequip", () =>
            {
                LocalPlayer.Inventory?.StashHeldItems(false, true);
                Pc.Inventory.Restore(Array.Empty<KeyValuePair<string, ItemInstance>>());
            });
        }
        TestKit.Unpause();
        File.AppendAllText(ProgressFile, $"ok {key}\n");
    }

    /// <summary>A managed exception is not a crash: logged and the sweep goes on.</summary>
    private static void Try(string key, string what, Action a)
    {
        try { a(); }
        catch (Exception e) { SelfTest.Log($"mod sweep: {key} {what} threw {e.GetType().Name}: {e.Message.Split('\n')[0]}"); }
    }
}
