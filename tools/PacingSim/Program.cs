// Headless XP pacing and kill-time simulation (COS-60, COS-28). Uses the real Core rules: enemy rolls, armor,
// steadfast caps, player hits, loot rarity and item rolls, perks, massacre and the XP curve. Everything the game
// would decide (kill pace, weapon damage, enemy mix) is an assumption listed in Assumptions and printed with the tables.
//
//   dotnet run --project tools/PacingSim -c Release -- [content folder] [--profile hunting|casual] [--seeds N] [--xp X]
using System.Globalization;
using ChampionsOfTheSons.Core;
using ChampionsOfTheSons.Core.Content;
using ChampionsOfTheSons.Core.Rules;
using ChampionsOfTheSons.Core.Saving;

CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

string folder = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "content"));
var a = Assumptions.Hunting;
int seeds = 3;
double xpMul = 1;
for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--profile": a = args[++i] == "casual" ? Assumptions.Casual : Assumptions.Hunting; break;
        case "--seeds": seeds = int.Parse(args[++i]); break;
        case "--xp": xpMul = double.Parse(args[++i]); break;
        default: folder = Path.GetFullPath(args[i]); break;
    }
}
a = a with { XpMul = xpMul };

var content = ContentDb.LoadFolder(folder);
if (content.Errors.Count > 0) { foreach (var e in content.Errors) Console.Error.WriteLine(e); return 1; }

Console.WriteLine($"# Pacing simulation ({a.Name}, {seeds} seeds, host XP x{a.XpMul})");
Console.WriteLine(a.Describe());

int[] marks = { 10, 25, 50, 75, 100, 125 };

// 1. Staying on one difficulty from level 1.
Console.WriteLine("\n## 1. One difficulty from level 1: hours to reach a level (median of seeds; '-' = not within " + a.MaxHours + " h)");
Console.WriteLine("| d | Difficulty | Enemy lvl | " + string.Join(" | ", marks.Select(m => "L" + m)) + " | Level at 10 h | Level at 50 h | Player >= enemy lvl at |");
Console.WriteLine("|---|---|---|" + string.Concat(marks.Select(_ => "---|")) + "---|---|---|");
foreach (var d in Difficulties.All)
{
    var runs = Enumerable.Range(0, seeds).Select(s => new Sim(content, a, (ulong)(1000 + s)).RunFixed(d)).ToList();
    string Med(Func<RunResult, double?> f)
    {
        var v = runs.Select(f).Select(x => x ?? double.PositiveInfinity).OrderBy(x => x).ToList();
        double m = v[v.Count / 2];
        return double.IsInfinity(m) ? "-" : Hours(m);
    }
    int enemyLvl = d.BaseLevelMin + 1;
    Console.WriteLine($"| {d.Index} | {d.Name} | {d.BaseLevelMin + 1}-{d.BaseLevelMax + 1} | " +
        string.Join(" | ", marks.Select(m => Med(r => r.HoursTo(m)))) +
        $" | {runs.Select(r => r.LevelAt(10)).OrderBy(x => x).ElementAt(seeds / 2)} | {runs.Select(r => r.LevelAt(50)).OrderBy(x => x).ElementAt(seeds / 2)} | {Med(r => r.HoursTo(enemyLvl))} |");
}

// 2. Ladder: the player always plays the difficulty with the best XP per hour that is still survivable.
Console.WriteLine($"\n## 2. Ladder: best XP/hour among survivable difficulties (a normal enemy needs >= {a.MinHitsToDie} hits to kill the player)");
var ladders = Enumerable.Range(0, seeds).Select(s => new Sim(content, a, (ulong)(2000 + s)).RunLadder()).ToList();
var ladder = ladders.OrderBy(r => r.HoursTo(50) ?? double.MaxValue).ThenByDescending(r => r.LevelAt(a.MaxHours)).ElementAt(seeds / 2);
Console.WriteLine("| Hours | Level | Difficulty | Enemy lvl | Kills/h | Normal kill s | Hits to die |");
Console.WriteLine("|---|---|---|---|---|---|---|");
foreach (var snap in ladder.Snapshots)
    Console.WriteLine($"| {snap.Hours:0.#} | {snap.Level} | {snap.Difficulty} | {snap.EnemyLevel} | {snap.KillsPerHour:0} | {snap.NormalKillSeconds:0.#} | {snap.HitsToDie:0.#} |");
Console.WriteLine("\nLadder hours to level (median seed): " + string.Join(", ", marks.Select(m => $"L{m} {(ladder.HoursTo(m) is double h ? Hours(h) : "-")}")));
Console.WriteLine("Difficulty first played at: " + string.Join(", ", ladder.Entered.Select(kv => $"{kv.Key} {Hours(kv.Value)} (L{ladder.LevelWhenEntered[kv.Key]})")));

// 3. Kill times by tier, player at the difficulty's enemy level (capped at 130), geared and perked for it.
Console.WriteLine("\n## 3. Kill time by tier (seconds), player at the enemy level of the difficulty (max 130), kit rolled on that difficulty");
Console.WriteLine("| d | Difficulty | Player lvl | Normal | Elite | Miniboss | Boss | Normal hits to die | XP per normal kill | Goal(L) | Normal kills per level |");
Console.WriteLine("|---|---|---|---|---|---|---|---|---|---|---|");
foreach (var d in Difficulties.All)
{
    int lvl = Math.Min(130, d.BaseLevelMin + 1);
    var sim = new Sim(content, a, 3000);
    sim.SetUp(lvl, d);
    var t = sim.KillTimesByTier(d);
    double xp = sim.MeanBounty(d, EnemyTier.Normal) * sim.Pc.Stats.Get(StatIds.ExpGain);
    double goal = XpCurve.Goal(lvl);
    Console.WriteLine($"| {d.Index} | {d.Name} | {lvl} | {Sec(t[EnemyTier.Normal])} | {Sec(t[EnemyTier.Elite])} | {Sec(t[EnemyTier.Miniboss])} | {Sec(t[EnemyTier.Boss])} | {sim.HitsToDie(d):0.#} | {xp:N0} | {goal:N0} | {goal / xp:0.#} |");
}
return 0;

static string Hours(double h) => h < 1 ? $"{h * 60:0} min" : $"{h:0.#} h";
static string Sec(double? s) => s is double v ? (v >= 600 ? $"{v / 60:0} min" : $"{v:0.#}") : "n/a";

/// <summary>What the game decides that the rules don't. Change these after the COS-28 play session measures them.</summary>
sealed record Assumptions
{
    public string Name { get; init; } = "";
    /// <summary>Damage of one melee swing of the held game weapon, before the mod's stats.</summary>
    public double WeaponDamage { get; init; } = 30;
    /// <summary>Seconds per swing at attack speed 1.</summary>
    public double SwingSeconds { get; init; } = 1.0;
    /// <summary>Enemies per encounter (uniform).</summary>
    public int GroupMin { get; init; } = 2;
    public int GroupMax { get; init; } = 5;
    /// <summary>Seconds between encounters: travel, looting, building, eating.</summary>
    public double SecondsBetweenGroups { get; init; } = 90;
    /// <summary>Game enemy hit damage before the mod's scaling.</summary>
    public double VanillaEnemyHit { get; init; } = 15;
    /// <summary>Drops a player sees per slot before each kit refresh (keeps the best).</summary>
    public int KitCandidates { get; init; } = 6;
    /// <summary>Kit and perks are refreshed every this many levels.</summary>
    public int KitEveryLevels { get; init; } = 5;
    public double MaxHours { get; init; } = 300;
    public double MinHitsToDie { get; init; } = 4;
    public double XpMul { get; init; } = 1;
    /// <summary>Enemy mix: kind id → (weight, vanilla max health). Story bosses are left out.</summary>
    public IReadOnlyDictionary<string, (double Weight, double VanillaHp)> Mix { get; init; } = new Dictionary<string, (double, double)>
    {
        ["enemy.cannibal"] = (40, 100), ["enemy.muddy_cannibal"] = (10, 100), ["enemy.heavy_cannibal"] = (10, 250),
        ["enemy.painted_cannibal"] = (5, 150), ["enemy.tribe_veteran"] = (8, 150), ["enemy.faceless_cannibal"] = (3, 150),
        ["enemy.fingers"] = (4, 400), ["enemy.twins"] = (3, 500), ["enemy.john"] = (2, 500), ["enemy.puffy"] = (4, 300),
        ["enemy.armsy"] = (2, 800), ["enemy.legsy"] = (2, 800), ["enemy.holey"] = (2, 600), ["enemy.baby"] = (3, 30),
        ["enemy.demon"] = (1, 600), ["enemy.creepy_virginia"] = (1, 600),
    };

    public static readonly Assumptions Hunting = new() { Name = "hunting: fights most of the time" };
    public static readonly Assumptions Casual = new() { Name = "casual: builds and explores between fights", SecondsBetweenGroups = 300 };

    public string Describe() =>
        $"Assumptions: melee weapon {WeaponDamage} per swing every {SwingSeconds} s; groups of {GroupMin}-{GroupMax} enemies, {SecondsBetweenGroups} s between groups; " +
        $"vanilla enemy hit {VanillaEnemyHit}; kit = best of {KitCandidates} drops per slot (rarity from the loot roll of the difficulty's normal enemies), refreshed every {KitEveryLevels} levels; " +
        "perks bought greedily for damage x sqrt(effective health); spells, ranged, headshots, crafting and non-kill XP not modelled; no deaths.";
}

sealed record Snapshot(double Hours, int Level, string Difficulty, int EnemyLevel, double KillsPerHour, double NormalKillSeconds, double HitsToDie);

sealed class RunResult
{
    public readonly SortedDictionary<int, double> FirstHourAtLevel = new();
    public readonly List<(double Hours, int Level)> Track = new();
    public readonly List<Snapshot> Snapshots = new();
    public readonly Dictionary<string, double> Entered = new();
    public readonly Dictionary<string, int> LevelWhenEntered = new();
    public double? HoursTo(int level) => FirstHourAtLevel.TryGetValue(level, out var h) ? h : null;
    public int LevelAt(double hours) => Track.LastOrDefault(t => t.Hours <= hours).Level;
}

sealed class Sim
{
    private readonly ContentDb _content;
    private readonly Assumptions _a;
    private readonly Rng _rng;
    private readonly LootRoller _loot;
    private readonly (EnemyKind Kind, double Hp)[] _kinds;
    private readonly double[] _weights;
    public PlayerCharacter Pc { get; private set; }
    private double _seconds;
    private int _lastKitLevel = -1;
    private DifficultyTier? _kitDifficulty;

    public Sim(ContentDb content, Assumptions a, ulong seed)
    {
        _content = content;
        _a = a;
        _rng = new Rng(seed * 2654435761UL + 17);
        _loot = new LootRoller(content);
        var byId = content.Enemies.Values.GroupBy(k => k.Id).ToDictionary(g => g.Key, g => g.First());
        _kinds = a.Mix.Where(kv => byId.ContainsKey(kv.Key)).Select(kv => (byId[kv.Key], kv.Value.VanillaHp)).ToArray();
        _weights = a.Mix.Where(kv => byId.ContainsKey(kv.Key)).Select(kv => kv.Value.Weight).ToArray();
        Pc = new PlayerCharacter(content);
    }

    private WorldRules Rules(DifficultyTier d) => new() { Difficulty = d, Xp = _a.XpMul };
    private double Hours => _seconds / 3600;

    public RunResult RunFixed(DifficultyTier d)
    {
        var r = new RunResult();
        Record(r);
        while (Hours < _a.MaxHours && Pc.Progress.Level < 130)
        {
            Refresh(d);
            FightGroup(d);
            Record(r);
        }
        return r;
    }

    public RunResult RunLadder()
    {
        var r = new RunResult();
        Record(r);
        double nextSnap = 0;
        DifficultyTier? current = null;
        double nextChoice = 0;
        while (Hours < _a.MaxHours && Pc.Progress.Level < 130)
        {
            if (current == null || Hours >= nextChoice)
            {
                current = ChooseDifficulty();
                nextChoice = Hours + 0.5;
                if (!r.Entered.ContainsKey(current.Name)) { r.Entered[current.Name] = Hours; r.LevelWhenEntered[current.Name] = Pc.Progress.Level; }
            }
            Refresh(current);
            if (Hours >= nextSnap)
            {
                double kt = MeanKillSeconds(current, EnemyTier.Normal, 40);
                double perGroup = (_a.GroupMin + _a.GroupMax) / 2.0;
                double kph = 3600 / (kt + _a.SecondsBetweenGroups / perGroup);
                r.Snapshots.Add(new Snapshot(Hours, Pc.Progress.Level, current.Name, current.BaseLevelMin + 1, kph, kt, HitsToDie(current)));
                nextSnap = nextSnap switch { < 1 => nextSnap + 0.25, < 5 => nextSnap + 1, < 20 => nextSnap + 5, < 100 => nextSnap + 10, _ => nextSnap + 25 };
            }
            FightGroup(current);
            Record(r);
        }
        return r;
    }

    /// <summary>Expected XP per hour on each difficulty with the current kit, among those that are survivable.</summary>
    private DifficultyTier ChooseDifficulty()
    {
        DifficultyTier best = Difficulties.Easy;
        double bestRate = -1;
        foreach (var d in Difficulties.All)
        {
            var saved = (_lastKitLevel, _kitDifficulty);
            var savedSlots = Pc.Inventory.Slots.ToList();
            ForceKit(d);
            if (d.Index > 0 && HitsToDie(d) < _a.MinHitsToDie) { RestoreKit(savedSlots, saved); continue; }
            double kt = MeanKillSeconds(d, EnemyTier.Normal, 30);
            double xp = MeanBounty(d, EnemyTier.Normal);
            double perGroup = (_a.GroupMin + _a.GroupMax) / 2.0;
            double rate = xp * Massacre.Multiplier((int)perGroup) / (kt + _a.SecondsBetweenGroups / perGroup);
            if (kt > 120) rate = 0;
            if (rate > bestRate) { bestRate = rate; best = d; }
            RestoreKit(savedSlots, saved);
        }
        return best;
    }

    private void ForceKit(DifficultyTier d) { _lastKitLevel = -1; Refresh(d); }

    private void RestoreKit(List<KeyValuePair<string, ItemInstance>> slots, (int, DifficultyTier?) saved)
    {
        Pc.Inventory.Restore(slots);
        (_lastKitLevel, _kitDifficulty) = saved;
    }

    private void Record(RunResult r)
    {
        int l = Pc.Progress.Level;
        for (int x = 1; x <= l; x++) r.FirstHourAtLevel.TryAdd(x, Hours);
        if (r.Track.Count == 0 || r.Track[^1].Level != l) r.Track.Add((Hours, l));
    }

    /// <summary>Puts the character at a level with perks and a kit for a difficulty (table 3).</summary>
    public void SetUp(int level, DifficultyTier d)
    {
        Pc = new PlayerCharacter(_content);
        Pc.Load(new PlayerSave { Level = level });
        _lastKitLevel = -1;
        Refresh(d);
    }

    private void Refresh(DifficultyTier d)
    {
        int l = Pc.Progress.Level;
        if (_lastKitLevel >= 0 && l < _lastKitLevel + _a.KitEveryLevels && _kitDifficulty == d) return;
        _lastKitLevel = l;
        _kitDifficulty = d;
        BuyPerks(d);
        RollKit(d);
    }

    // ---- fighting ----

    private (EnemyKind Kind, double Hp) PickKind() => _kinds[_rng.PickWeighted(_weights)];

    private void FightGroup(DifficultyTier d)
    {
        int n = _rng.RangeInclusive(_a.GroupMin, _a.GroupMax);
        var rules = Rules(d);
        for (int i = 0; i < n; i++)
        {
            var (kind, hp) = PickKind();
            var p = EnemyScaler.Roll(kind, hp, rules, _rng);
            double t = KillSeconds(p, out bool killed);
            Advance(t);
            if (killed) Pc.OnKill(p.Bounty);
        }
        Advance(_a.SecondsBetweenGroups);
    }

    private void Advance(double seconds)
    {
        // Tick in steps so a streak ends at the right time.
        while (seconds > 0)
        {
            double dt = Math.Min(seconds, 5);
            Pc.Tick(dt);
            _seconds += dt;
            seconds -= dt;
        }
    }

    private double SwingSeconds => _a.SwingSeconds / Math.Max(0.1, Pc.Stats.Get(StatIds.AttackSpeed));

    /// <summary>Swings a melee weapon until the enemy dies (real hit, armor, pierce, cap and Undead rules).</summary>
    public double KillSeconds(EnemyProfile p, out bool killed)
    {
        var state = new EnemyHealthState(p);
        int hits = 0;
        const int cap = 3000;
        while (!state.Dead && hits < cap)
        {
            state.TakeHit(PlayerAttack.Weapon(Pc.Stats, WeaponClass.Melee, _a.WeaponDamage, _rng));
            hits++;
        }
        killed = state.Dead;
        return hits * SwingSeconds;
    }

    private EnemyProfile? RollTier(DifficultyTier d, EnemyTier tier)
    {
        var rules = Rules(d);
        for (int i = 0; i < 20000; i++)
        {
            var (kind, hp) = PickKind();
            var p = EnemyScaler.Roll(kind, hp, rules, _rng);
            if (p.Tier == tier) return p;
        }
        return null;
    }

    public double MeanKillSeconds(DifficultyTier d, EnemyTier tier, int samples)
    {
        double sum = 0;
        int n = 0;
        for (int i = 0; i < samples; i++)
        {
            var p = RollTier(d, tier);
            if (p == null) continue;
            sum += KillSeconds(p, out _);
            n++;
        }
        return n == 0 ? double.NaN : sum / n;
    }

    public double MeanBounty(DifficultyTier d, EnemyTier tier)
    {
        double sum = 0;
        for (int i = 0; i < 200; i++) sum += RollTier(d, tier)?.Bounty ?? 0;
        return sum / 200;
    }

    public Dictionary<EnemyTier, double?> KillTimesByTier(DifficultyTier d) =>
        Enum.GetValues<EnemyTier>().ToDictionary(t => t, t =>
        {
            double v = MeanKillSeconds(d, t, t == EnemyTier.Normal ? 60 : 20);
            return double.IsNaN(v) ? (double?)null : v;
        });

    /// <summary>Hits from an average normal enemy of the difficulty that kill the player (armor, dodge, block).</summary>
    public double HitsToDie(DifficultyTier d)
    {
        var rules = Rules(d);
        double sum = 0;
        const int n = 100;
        var s = Pc.Stats;
        for (int i = 0; i < n; i++)
        {
            var (kind, hp) = PickKind();
            var p = EnemyScaler.Roll(kind, hp, rules, _rng);
            if (p.Tier != EnemyTier.Normal) { i--; continue; }
            double raw = EnemyScaler.DamageToPlayer(p, _a.VanillaEnemyHit);
            double taken = Math.Max(1, raw * s.Get(StatIds.AllDamageTaken) * (1 - ArmorMath.Reduction(s.Get(StatIds.Armor))) - s.Get(StatIds.Block));
            sum += taken * Math.Clamp(s.Get(StatIds.GetHitChance), 0.05, 1);
        }
        return Derived.MaxHealth(s) / (sum / n);
    }

    // ---- character building ----

    private double MeanArmor(DifficultyTier d)
    {
        int lvl = d.BaseLevelMin + 1;
        double p = Math.Pow(lvl, 2.43);
        return (p / 3 + 1 + p * 2 / 3 + 20) / 2 * d.ArmorFactor;
    }

    /// <summary>Damage per second against a typical armored enemy times the square root of effective health.</summary>
    private double Score(DifficultyTier d)
    {
        var s = Pc.Stats;
        double armor = Math.Max(0, MeanArmor(d) - Derived.MeleeArmorPierce(s));
        double hit = (_a.WeaponDamage + s.Get(StatIds.MeleeFlatDmg)) * Derived.MeleeMultiplier(s)
                     * (1 + Math.Clamp(s.Get(StatIds.CritChance), 0, 1) * s.Get(StatIds.CritDamage)) * (1 - ArmorMath.Reduction(armor));
        double dps = hit * Math.Max(0.1, s.Get(StatIds.AttackSpeed));
        double taken = Math.Max(0.05, s.Get(StatIds.AllDamageTaken)) * (1 - ArmorMath.Reduction(s.Get(StatIds.Armor))) * Math.Clamp(s.Get(StatIds.GetHitChance), 0.05, 1);
        double ehp = Derived.MaxHealth(s) / taken;
        return dps * Math.Sqrt(Math.Max(1, ehp)) * (1 + 0.5 * (s.Get(StatIds.ExpGain) - 1));
    }

    private void BuyPerks(DifficultyTier d)
    {
        var perks = Pc.Content.Perks.Values.Where(p => !p.Manual).OrderBy(p => p.LegacyIndex).ToList();
        while (Pc.Perks.AvailablePoints(Pc.Progress) > 0)
        {
            double baseScore = Score(d);
            PerkDef? best = null;
            double bestGain = 0;
            foreach (var p in perks)
            {
                if (Pc.Perks.CanBuy(p.Id, Pc.Progress) != BuyResult.Ok) continue;
                var before = Pc.Perks.Bought.ToDictionary(kv => kv.Key, kv => kv.Value);
                Pc.Perks.Buy(p.Id, Pc.Progress);
                Pc.Stats.SetSource("perks", Pc.Perks.Modifiers());
                // Gain per point; perks that only open the path still count a little so the tree is explored.
                double gain = (Score(d) - baseScore) / baseScore / p.Cost + 1e-6 / p.Cost;
                Pc.Perks.Restore(before, Pc.Perks.UnlockedSpells.ToList());
                Pc.Stats.SetSource("perks", Pc.Perks.Modifiers());
                if (gain > bestGain) { bestGain = gain; best = p; }
            }
            if (best == null) break;
            Pc.BuyPerk(best.Id);
        }
    }

    /// <summary>Per slot: the best of N drops rolled at the player's level with the difficulty's rarity roll.</summary>
    private void RollKit(DifficultyTier d)
    {
        int level = Pc.Progress.Level;
        double worth = MeanBounty(d, EnemyTier.Normal);
        double mf = Math.Max(0.1, Pc.Stats.Get(StatIds.MagicFind));
        var slots = Pc.Inventory.Slots.Where(kv => kv.Key.StartsWith("equip.", StringComparison.Ordinal)).ToDictionary(kv => kv.Key, kv => kv.Value);
        foreach (var slot in LootRoller.EquipSlots)
        {
            ItemInstance? best = slots.GetValueOrDefault(slot);
            Pc.Inventory.Restore(slots);
            double bestScore = Score(d);
            for (int i = 0; i < _a.KitCandidates; i++)
            {
                int rarity = LootRoller.RollRarity(worth, level, mf, d.Index, _rng);
                var def = _loot.PickBase(rarity, level, _rng, b => LootRoller.SlotsFor(b.Type).Contains(slot));
                if (def == null) continue;
                var item = _loot.RollItem(def, level, _rng);
                slots[slot] = item;
                Pc.Inventory.Restore(slots);
                double sc = Score(d);
                if (sc > bestScore) { bestScore = sc; best = item; }
            }
            if (best != null) slots[slot] = best; else slots.Remove(slot);
            Pc.Inventory.Restore(slots);
        }
    }
}
