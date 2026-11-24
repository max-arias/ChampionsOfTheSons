// Headless XP pacing and kill-time simulation (COS-60, COS-28). Uses the real Core rules: enemy rolls, armor,
// steadfast caps, player hits, loot rarity and item rolls, perks, massacre and the XP curve. Everything the game
// would decide (kill pace, weapon damage, enemy mix) is an assumption listed in Assumptions and printed with the tables.
// COS-130: enemies roll at the player's level plus the game difficulty's offset; one run per game difficulty.
//
//   dotnet run --project tools/PacingSim -c Release -- [content folder] [--profile hunting|casual] [--seeds N] [--xp X] [--fixed-kit] [--weapon-only]
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
bool fixedKit = false;
bool weaponOnly = false;
double dropMul = 1;
double worthMul = 1;
for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--profile": a = args[++i] == "casual" ? Assumptions.Casual : Assumptions.Hunting; break;
        case "--seeds": seeds = int.Parse(args[++i]); break;
        case "--xp": xpMul = double.Parse(args[++i]); break;
        case "--fixed-kit": fixedKit = true; break;
        case "--weapon-only": weaponOnly = true; break;
        case "--drops": dropMul = double.Parse(args[++i]); break;
        case "--worth": worthMul = double.Parse(args[++i]); break;
        default: folder = Path.GetFullPath(args[i]); break;
    }
}
a = a with { XpMul = xpMul, FixedKit = fixedKit, DropMul = dropMul, WorthMul = worthMul };

var content = ContentDb.LoadFolder(folder);
if (content.Errors.Count > 0) { foreach (var e in content.Errors) Console.Error.WriteLine(e); return 1; }

Console.WriteLine($"# Pacing simulation ({a.Name}, {seeds} seeds, host XP x{a.XpMul})");
Console.WriteLine(a.Describe());

int[] marks = { 10, 25, 50, 75, 100, 125 };
var modes = new[] { GameMode.Peaceful, GameMode.Normal, GameMode.Hard, GameMode.HardSurvival }.Select(m => GameDifficulty.For(m)).ToList();
if (weaponOnly) { WeaponOnlyTable(); return 0; }
Console.WriteLine("\nEnemies roll at the player's level (COS-130): level = player +-1 + kind extra + game offset; elite chance by game difficulty.");

// 1. Leveling from level 1 on each game difficulty.
Console.WriteLine("\n## 1. Hours to reach a level from level 1 (median of seeds; '-' = not within " + a.MaxHours + " h)");
Console.WriteLine("| Game difficulty | Offset | Elites | " + string.Join(" | ", marks.Select(m => "L" + m)) + " | Level at 10 h | Level at 50 h |");
Console.WriteLine("|---|---|---|" + string.Concat(marks.Select(_ => "---|")) + "---|---|");
var runsByMode = new Dictionary<GameMode, List<RunResult>>();
foreach (var g in modes)
{
    var runs = Enumerable.Range(0, seeds).Select(s => new Sim(content, a, (ulong)(1000 + s), g).Run()).ToList();
    runsByMode[g.Mode] = runs;
    string Med(Func<RunResult, double?> f)
    {
        var v = runs.Select(f).Select(x => x ?? double.PositiveInfinity).OrderBy(x => x).ToList();
        double m = v[v.Count / 2];
        return double.IsInfinity(m) ? "-" : Hours(m);
    }
    Console.WriteLine($"| {g.Mode} | {g.LevelOffset:+0;-0;0} | {g.EliteChance:P0} | " + string.Join(" | ", marks.Select(m => Med(r => r.HoursTo(m)))) +
        $" | {runs.Select(r => r.LevelAt(10)).OrderBy(x => x).ElementAt(seeds / 2)} | {runs.Select(r => r.LevelAt(50)).OrderBy(x => x).ElementAt(seeds / 2)} |");
}
Console.WriteLine("\nDrops (equipment; XP hearts not counted): " + string.Join("; ", runsByMode.Select(kv =>
    $"{kv.Key} {kv.Value.Sum(r => r.Drops) / kv.Value.Sum(r => r.Hours):0.0}/h, {kv.Value.Sum(r => r.Drops) / (double)Math.Max(1, kv.Value.Sum(r => r.Kills)):0.00} per kill, {kv.Value.Sum(r => r.Merges) / kv.Value.Sum(r => r.Hours):0.0} merges/h")));

// 2. How a run feels over time (median seed), Normal and Hard Survival.
foreach (var mode in new[] { GameMode.Normal, GameMode.HardSurvival })
{
    var run = runsByMode[mode].OrderBy(r => r.HoursTo(50) ?? double.MaxValue).ElementAt(seeds / 2);
    Console.WriteLine($"\n## 2. {mode}: a run over time (median seed)");
    Console.WriteLine("| Hours | Level | Enemy lvl | Kills/h | Normal kill s | Hits to die |");
    Console.WriteLine("|---|---|---|---|---|---|");
    foreach (var snap in run.Snapshots)
        Console.WriteLine($"| {snap.Hours:0.#} | {snap.Level} | {snap.EnemyLevel} | {snap.KillsPerHour:0} | {snap.NormalKillSeconds:0.#} | {snap.HitsToDie:0.#} |");
}

// 3. Kill times by tier at set player levels, with the kit and perks the median run (section 2) had on reaching the level
// (COS-150: a kit rolled at the level without the run's drops and merges is far weaker than what a player has by then).
int[] levels = { 1, 10, 25, 50, 75, 100, 130 };
foreach (var g in modes.Where(m => m.Mode is GameMode.Normal or GameMode.HardSurvival))
{
    var median = runsByMode[g.Mode].OrderBy(r => r.HoursTo(50) ?? double.MaxValue).ElementAt(seeds / 2);
    Console.WriteLine($"\n## 3. {g.Mode}: kill time by tier (seconds) at a player level, kit and perks of the median run on reaching it (* = not reached: kit rolled at the level)");
    Console.WriteLine("| Player lvl | Enemy lvl | Normal | Elite | Miniboss | Boss | Normal hits to die | XP per normal kill | Goal(L) | Normal kills per level |");
    Console.WriteLine("|---|---|---|---|---|---|---|---|---|---|");
    foreach (int lvl in levels)
    {
        var sim = new Sim(content, a, 3000, g);
        bool reached = median.Saves.TryGetValue(lvl, out var save);
        if (reached) sim.SetUpFrom(save!); else sim.SetUp(lvl);
        var t = sim.KillTimesByTier();
        double xp = sim.MeanBounty(EnemyTier.Normal) * sim.Pc.Stats.Get(StatIds.ExpGain);
        double goal = XpCurve.Goal(lvl);
        Console.WriteLine($"| {lvl}{(reached ? "" : "*")} | {sim.EnemyLevel} | {Sec(t[EnemyTier.Normal])} | {Sec(t[EnemyTier.Elite])} | {Sec(t[EnemyTier.Miniboss])} | {Sec(t[EnemyTier.Boss])} | {sim.HitsToDie():0.#} | {xp:N0} | {goal:N0} | {goal / xp:0.#} |");
    }
}

WeaponOnlyTable();
return 0;

// 4. COS-191: a bare character (no perks, no attributes, no other gear) with one on-level melee weapon: hits to kill.
void WeaponOnlyTable()
{
    var g = modes.First(m => m.Mode == GameMode.Normal);
    Console.WriteLine("\n## 4. Normal: hits to kill with one on-level melee weapon and nothing else (mean of 20 weapons)");
    Console.WriteLine("| Player lvl | Rarity | Normal hits | Elite hits |");
    Console.WriteLine("|---|---|---|---|");
    foreach (int lvl in new[] { 10, 31, 60, 100 })
        foreach (var rarity in new[] { Rarity.Common, Rarity.Rare, Rarity.Legendary })
        {
            var sim = new Sim(content, a, 4000, g);
            double normal = 0, elite = 0;
            const int weapons = 20;
            for (int i = 0; i < weapons; i++)
            {
                sim.SetUpWeaponOnly(lvl, rarity);
                normal += sim.MeanHits(EnemyTier.Normal, 20);
                elite += sim.MeanHits(EnemyTier.Elite, 10);
            }
            Console.WriteLine($"| {lvl} | {rarity} | {normal / weapons:0.#} | {elite / weapons:0.#} |");
        }
}

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
    /// <summary>Drops a player sees per slot for the starting kit (and every refresh with --fixed-kit); otherwise each refresh
    /// gets the equipment the simulated kills since the last one actually dropped, spread over the slots.</summary>
    public int KitCandidates { get; init; } = 6;
    /// <summary>True: the old model, <see cref="KitCandidates"/> per slot at every refresh whatever the drop rate.</summary>
    public bool FixedKit { get; init; }
    /// <summary>Kit and perks are refreshed every this many levels.</summary>
    public int KitEveryLevels { get; init; } = 5;
    /// <summary>... or after this many hours without a refresh (COS-150: a player stuck on a level still wears what drops).</summary>
    public double KitEveryHours { get; init; } = 5;
    public double MaxHours { get; init; } = 300;
    public double MinHitsToDie { get; init; } = 4;
    public double XpMul { get; init; } = 1;
    /// <summary>Host drop chance and quantity (--drops): to see what drop rate a pace needs.</summary>
    public double DropMul { get; init; } = 1;
    /// <summary>Multiplies the worth that drives the rarity roll (--worth): what a rarer-leaning drop table would do.</summary>
    public double WorthMul { get; init; } = 1;
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
        $"vanilla enemy hit {VanillaEnemyHit}; kit = best of {KitCandidates} drops per slot (rarity from the loot roll of normal enemies at the player's level), refreshed every {KitEveryLevels} levels or {KitEveryHours} h; " +
        "perks bought greedily for damage x sqrt(effective health); spells, ranged, headshots and non-kill XP not modelled; no deaths.";
}

sealed record Snapshot(double Hours, int Level, int EnemyLevel, double KillsPerHour, double NormalKillSeconds, double HitsToDie);

sealed class RunResult
{
    public readonly SortedDictionary<int, double> FirstHourAtLevel = new();
    public readonly List<(double Hours, int Level)> Track = new();
    public readonly List<Snapshot> Snapshots = new();
    /// <summary>The character (kit and perks) on first reaching each level, for table 3.</summary>
    public readonly Dictionary<int, PlayerSave> Saves = new();
    /// <summary>Equipment drops and kills over the whole run (drop rate check, Max 2026-11-21).</summary>
    public int Drops, Kills, Merges;
    public double Hours;
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
    private double _lastKitHours;
    private int _dropsSinceKit, _kills, _totalDrops, _lastKitDrops = -1;
    private readonly List<double> _dropWorths = new();
    /// <summary>Drops the player did not wear, kept for Merge (COS-144): by item type and rarity ("*" for Legendary).</summary>
    private readonly Dictionary<(string Type, int Rarity), List<ItemInstance>> _mergePool = new();
    public int Merges { get; private set; }
    private readonly GameDifficulty _game;

    public Sim(ContentDb content, Assumptions a, ulong seed, GameDifficulty game)
    {
        _game = game;
        _content = content;
        _a = a;
        _rng = new Rng(seed * 2654435761UL + 17);
        _loot = new LootRoller(content);
        var byId = content.Enemies.Values.GroupBy(k => k.Id).ToDictionary(g => g.Key, g => g.First());
        _kinds = a.Mix.Where(kv => byId.ContainsKey(kv.Key)).Select(kv => (byId[kv.Key], kv.Value.VanillaHp)).ToArray();
        _weights = a.Mix.Where(kv => byId.ContainsKey(kv.Key)).Select(kv => kv.Value.Weight).ToArray();
        Pc = new PlayerCharacter(content);
    }

    private WorldRules Rules => new() { GameDifficulty = _game, Xp = _a.XpMul, DropChance = _a.DropMul, DropQuantity = _a.DropMul };
    private double Hours => _seconds / 3600;
    private int Party => Pc.Progress.Level;
    /// <summary>A normal enemy kind's (level extra 1) middle level at the current player level.</summary>
    public int EnemyLevel => Math.Max(1, Party + 1 + _game.LevelOffset);
    private DifficultyTier Band => Difficulties.ForLevel(EnemyLevel);

    public RunResult Run()
    {
        var r = new RunResult();
        Record(r);
        double nextSnap = 0;
        while (Hours < _a.MaxHours && Pc.Progress.Level < 130)
        {
            Refresh();
            if (Hours >= nextSnap)
            {
                double kt = MeanKillSeconds(EnemyTier.Normal, 40);
                double perGroup = (_a.GroupMin + _a.GroupMax) / 2.0;
                double kph = 3600 / (kt + _a.SecondsBetweenGroups / perGroup);
                r.Snapshots.Add(new Snapshot(Hours, Pc.Progress.Level, EnemyLevel, kph, kt, HitsToDie()));
                nextSnap = nextSnap switch { < 1 => nextSnap + 0.25, < 5 => nextSnap + 1, < 20 => nextSnap + 5, < 100 => nextSnap + 10, _ => nextSnap + 25 };
            }
            FightGroup();
            Record(r);
        }
        r.Drops = _totalDrops + _dropsSinceKit; r.Kills = _kills; r.Hours = Hours; r.Merges = Merges;
        return r;
    }

    private void Record(RunResult r)
    {
        int l = Pc.Progress.Level;
        for (int x = 1; x <= l; x++) if (!r.Saves.ContainsKey(x)) r.Saves[x] = Pc.ToSave();
        for (int x = 1; x <= l; x++) r.FirstHourAtLevel.TryAdd(x, Hours);
        if (r.Track.Count == 0 || r.Track[^1].Level != l) r.Track.Add((Hours, l));
    }

    /// <summary>Puts the character at a level with perks and a kit for it (table 3).</summary>
    public void SetUp(int level)
    {
        Pc = new PlayerCharacter(_content);
        Pc.Load(new PlayerSave { Level = level });
        _lastKitLevel = -1;
        _lastKitDrops = -1;
        Refresh();
    }

    /// <summary>COS-191 (table 4): a bare character of the level with one rolled melee weapon of the rarity in the weapon slot.</summary>
    public void SetUpWeaponOnly(int level, Rarity rarity)
    {
        Pc = new PlayerCharacter(_content);
        Pc.Load(new PlayerSave { Level = level });
        var def = _loot.PickBase((int)rarity, level, _rng, b => LootRoller.SlotsFor(b.Type).Contains(WeaponTypes.WeaponSlot)
            && b.WeaponModel != null && _content.WeaponTypes.TryGetValue(b.WeaponModel, out var t) && t.Base != WeaponBase.Bow);
        if (def != null)
            Pc.Inventory.Restore(new Dictionary<string, ItemInstance> { [WeaponTypes.WeaponSlot] = _loot.RollItem(def, level, _rng) });
    }

    /// <summary>Mean swings to kill an enemy of the tier.</summary>
    public double MeanHits(EnemyTier tier, int samples) => MeanKillSeconds(tier, samples) / SwingSeconds;

    /// <summary>Puts back the character a run had at some level (table 3).</summary>
    public void SetUpFrom(PlayerSave save)
    {
        Pc = new PlayerCharacter(_content);
        Pc.Load(save);
        _lastKitLevel = save.Level;
        _lastKitDrops = 0;
    }

    private void Refresh()
    {
        int l = Pc.Progress.Level;
        if (_lastKitLevel >= 0 && l < _lastKitLevel + _a.KitEveryLevels && Hours < _lastKitHours + _a.KitEveryHours) return;
        _lastKitLevel = l;
        _lastKitHours = Hours;
        SpendAttributes();
        BuyPerks();
        RollKit();
    }

    /// <summary>COS-174: spends the attribute points greedily, a chunk at a time, on whatever raises the score most (Vitality when none does).</summary>
    private void SpendAttributes()
    {
        while (Pc.AttributePointsLeft > 0)
        {
            int chunk = Math.Max(1, Pc.AttributePointsLeft / 8);
            double baseScore = Score();
            string best = StatIds.Vitality;
            double bestGain = 0;
            foreach (var id in AttributePoints.Ids)
            {
                int n = Pc.SpendAttribute(id, chunk);
                double gain = Score() - baseScore;
                Pc.RefundAttribute(id, n);
                if (gain > bestGain) { bestGain = gain; best = id; }
            }
            if (Pc.SpendAttribute(best, chunk) == 0) break;
        }
    }

    // ---- fighting ----

    private (EnemyKind Kind, double Hp) PickKind() => _kinds[_rng.PickWeighted(_weights)];

    private void FightGroup()
    {
        int n = _rng.RangeInclusive(_a.GroupMin, _a.GroupMax);
        var rules = Rules;
        for (int i = 0; i < n; i++)
        {
            var (kind, hp) = PickKind();
            var p = EnemyScaler.Roll(kind, hp, rules, Party, _rng);
            double t = KillSeconds(p, out bool killed);
            Advance(t);
            if (killed)
            {
                Pc.OnKill(p.Bounty);
                double mf = Math.Max(0.1, Pc.Stats.Get(StatIds.MagicFind));
                if (LootRoller.ShouldDrop(p, rules, mf, _rng))
                {
                    int count = LootRoller.DropCount(p, rules, _rng);
                    _dropsSinceKit += count;
                    for (int d = 0; d < count; d++) _dropWorths.Add(p.Bounty); // the rarity roll scales with the killed enemy's bounty
                }
                _kills++;
            }
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

    private double SwingSeconds => _a.SwingSeconds / Math.Max(0.1, Pc.Stats.Get(StatIds.AttackSpeed) * WeaponTypes.SwingSpeedFactor(WeaponTypes.Equipped(Pc.Inventory, Pc.Content)));

    /// <summary>COS-191: a mod weapon in the weapon slot hits for its own damage and pierce; without one, the assumed game weapon.</summary>
    private (double Damage, double Pierce) WeaponHit()
    {
        var type = WeaponTypes.Equipped(Pc.Inventory, Pc.Content);
        var item = Pc.Inventory.Get(WeaponTypes.WeaponSlot);
        if (type == null || item == null || type.Base == WeaponBase.Bow) return (_a.WeaponDamage, 0);
        return (WeaponTypes.WeaponDamage(type, item.Level, item.Rarity), WeaponTypes.WeaponPierce(item.Level, item.Rarity));
    }

    /// <summary>Swings a melee weapon until the enemy dies (real hit, armor, pierce, cap and Undead rules).</summary>
    public double KillSeconds(EnemyProfile p, out bool killed)
    {
        var state = new EnemyHealthState(p);
        int hits = 0;
        const int cap = 3000;
        while (!state.Dead && hits < cap)
        {
            var (dmg, pierce) = WeaponHit();
            state.TakeHit(PlayerAttack.Weapon(Pc.Stats, WeaponClass.Melee, dmg, _rng, weaponPierce: pierce));
            hits++;
        }
        killed = state.Dead;
        return hits * SwingSeconds;
    }

    private EnemyProfile? RollTier(EnemyTier tier)
    {
        var rules = Rules;
        for (int i = 0; i < 20000; i++)
        {
            var (kind, hp) = PickKind();
            var p = EnemyScaler.Roll(kind, hp, rules, Party, _rng);
            if (p.Tier == tier) return p;
        }
        return null;
    }

    public double MeanKillSeconds(EnemyTier tier, int samples)
    {
        double sum = 0;
        int n = 0;
        for (int i = 0; i < samples; i++)
        {
            var p = RollTier(tier);
            if (p == null) continue;
            sum += KillSeconds(p, out _);
            n++;
        }
        return n == 0 ? double.NaN : sum / n;
    }

    public double MeanBounty(EnemyTier tier)
    {
        double sum = 0;
        for (int i = 0; i < 200; i++) sum += RollTier(tier)?.Bounty ?? 0;
        return sum / 200;
    }

    public Dictionary<EnemyTier, double?> KillTimesByTier() =>
        Enum.GetValues<EnemyTier>().ToDictionary(t => t, t =>
        {
            double v = MeanKillSeconds(t, t == EnemyTier.Normal ? 60 : 20);
            return double.IsNaN(v) ? (double?)null : v;
        });

    /// <summary>Hits from an average normal enemy at the player's level that kill the player (armor, dodge, block).</summary>
    public double HitsToDie()
    {
        var rules = Rules;
        double sum = 0;
        const int n = 100;
        var s = Pc.Stats;
        for (int i = 0; i < n; i++)
        {
            var (kind, hp) = PickKind();
            var p = EnemyScaler.Roll(kind, hp, rules, Party, _rng);
            if (p.Tier != EnemyTier.Normal) { i--; continue; }
            double raw = EnemyScaler.DamageToPlayer(p, _a.VanillaEnemyHit);
            double taken = Math.Max(1, raw * s.Get(StatIds.AllDamageTaken) * (1 - ArmorMath.Reduction(s.Get(StatIds.Armor))) - s.Get(StatIds.Block));
            sum += taken * Math.Clamp(s.Get(StatIds.GetHitChance), 0.05, 1);
        }
        return Derived.MaxHealth(s) / (sum / n);
    }

    // ---- character building ----

    private double MeanArmor() => EnemyScaler.MeanArmor(EnemyLevel);

    /// <summary>Damage per second against a typical armored enemy times the square root of effective health.</summary>
    private double Score()
    {
        var s = Pc.Stats;
        var (weaponDamage, weaponPierce) = WeaponHit();
        double armor = Math.Max(0, MeanArmor() - Derived.MeleeArmorPierce(s) - weaponPierce);
        double hit = (weaponDamage + s.Get(StatIds.MeleeFlatDmg)) * Derived.MeleeMultiplier(s)
                     * (1 + Math.Clamp(s.Get(StatIds.CritChance), 0, 1) * s.Get(StatIds.CritDamage)) * (1 - ArmorMath.Reduction(armor));
        double dps = hit * Math.Max(0.1, s.Get(StatIds.AttackSpeed));
        double taken = Math.Max(0.05, s.Get(StatIds.AllDamageTaken)) * (1 - ArmorMath.Reduction(s.Get(StatIds.Armor))) * Math.Clamp(s.Get(StatIds.GetHitChance), 0.05, 1);
        double ehp = Derived.MaxHealth(s) / taken;
        return dps * Math.Sqrt(Math.Max(1, ehp)) * (1 + 0.5 * (s.Get(StatIds.ExpGain) - 1));
    }

    private void BuyPerks()
    {
        var perks = Pc.Content.Perks.Values.Where(p => !p.Manual).OrderBy(p => p.LegacyIndex).ToList();
        while (Pc.Perks.AvailablePoints(Pc.Progress) > 0)
        {
            double baseScore = Score();
            PerkDef? best = null;
            double bestGain = 0;
            foreach (var p in perks)
            {
                if (Pc.Perks.CanBuy(p.Id, Pc.Progress) != BuyResult.Ok) continue;
                var before = Pc.Perks.Bought.ToDictionary(kv => kv.Key, kv => kv.Value);
                Pc.Perks.Buy(p.Id, Pc.Progress);
                Pc.Stats.SetSource("perks", Pc.Perks.Modifiers());
                // Gain per point; perks that only open the path still count a little so the tree is explored.
                double gain = (Score() - baseScore) / baseScore / p.Cost + 1e-6 / p.Cost;
                Pc.Perks.Restore(before, Pc.Perks.UnlockedSpells.ToList());
                Pc.Stats.SetSource("perks", Pc.Perks.Modifiers());
                if (gain > bestGain) { bestGain = gain; best = p; }
            }
            if (best == null) break;
            Pc.BuyPerk(best.Id);
        }
    }

    /// <summary>Per slot: the best of N drops rolled at the player's level with the rarity roll of enemies at that level.</summary>
    private void RollKit()
    {
        int level = Pc.Progress.Level;
        double normalWorth = MeanBounty(EnemyTier.Normal);
        double mf = Math.Max(0.1, Pc.Stats.Get(StatIds.MagicFind));
        var slots = Pc.Inventory.Slots.Where(kv => kv.Key.StartsWith("equip.", StringComparison.Ordinal)).ToDictionary(kv => kv.Key, kv => kv.Value);
        bool first = _lastKitDrops < 0;
        bool fixedKit = first || _a.FixedKit;
        // Real drops since the last refresh: each with the bounty of the enemy that dropped it, dealt to random slots.
        var worths = fixedKit ? new List<double>() : _dropWorths.OrderBy(_ => _rng.NextDouble()).ToList();
        var bySlot = LootRoller.EquipSlots.ToDictionary(sl => sl, _ => new List<double>());
        for (int i = 0; i < worths.Count; i++) bySlot[LootRoller.EquipSlots[i % LootRoller.EquipSlots.Count]].Add(worths[i]);
        _totalDrops += _dropsSinceKit;
        _lastKitDrops = _dropsSinceKit;
        _dropsSinceKit = 0;
        _dropWorths.Clear();
        foreach (var slot in LootRoller.EquipSlots)
        {
            var dropWorths = fixedKit ? Enumerable.Repeat(normalWorth, _a.KitCandidates).ToList() : bySlot[slot];
            foreach (double w in dropWorths)
            {
                int rarity = LootRoller.RollRarity(w * _a.WorthMul, level, mf, Band.Index, _rng);
                var def = _loot.PickBase(rarity, level, _rng, b => LootRoller.SlotsFor(b.Type).Contains(slot));
                if (def == null) continue;
                Offer(slots, slot, _loot.RollItem(def, level, _rng), keepLeftovers: !fixedKit);
            }
        }
        if (!fixedKit) MergePool(slots);
        Pc.Inventory.Restore(slots);
    }

    /// <summary>Wears the item in the slot if that scores better; whatever is not worn goes to the merge pool.</summary>
    private void Offer(Dictionary<string, ItemInstance> slots, string slot, ItemInstance item, bool keepLeftovers)
    {
        var old = slots.GetValueOrDefault(slot);
        Pc.Inventory.Restore(slots);
        double before = Score();
        slots[slot] = item;
        Pc.Inventory.Restore(slots);
        double after = Score();
        ItemInstance? spare;
        if (after > before) spare = old;
        else
        {
            spare = item;
            if (old != null) slots[slot] = old; else slots.Remove(slot);
        }
        if (keepLeftovers && spare != null) Pool(spare);
    }

    private void Pool(ItemInstance item)
    {
        if (!_content.Items.TryGetValue(item.BaseId, out var def)) return;
        var key = ((int)item.Rarity == Rarities.Max ? "*" : def.Type, (int)item.Rarity);
        if (!_mergePool.TryGetValue(key, out var list)) _mergePool[key] = list = new();
        list.Add(item);
    }

    /// <summary>Merge (COS-144) as a player would: any full set of one kind becomes one item of the next rarity, at the lowest
    /// input level, offered to its slot; leftovers stay pooled.</summary>
    private void MergePool(Dictionary<string, ItemInstance> slots)
    {
        for (bool merged = true; merged;)
        {
            merged = false;
            foreach (var key in _mergePool.Keys.ToList())
            {
                var list = _mergePool[key];
                int need = Merger.Need((Rarity)key.Rarity);
                if (list.Count < need) continue;
                var batch = list.OrderByDescending(i => i.Level).Take(need).ToList();
                foreach (var i in batch) list.Remove(i);
                int lvl = batch.Min(i => i.Level);
                int rarity = Math.Min(key.Rarity + 1, Rarities.Max);
                var inputs = batch.Select(i => i.BaseId).ToHashSet();
                var pool = _content.Items.Values.Where(d => d.Rarity == rarity && LootRoller.IsEquippable(d)
                    && (key.Type == "*" ? !inputs.Contains(d.Id) : d.Type == key.Type)).OrderBy(d => d.Id, StringComparer.Ordinal).ToList();
                if (pool.Count == 0) continue;
                var fitting = pool.Where(d => d.MinLevel <= lvl).ToList();
                if (fitting.Count == 0) fitting = pool.Where(d => d.MinLevel == pool.Min(x => x.MinLevel)).ToList();
                var def = _rng.Pick(fitting);
                var result = _loot.RollItem(def, lvl, _rng);
                Merges++;
                merged = true;
                var slot = LootRoller.SlotsFor(def.Type).FirstOrDefault(sl => LootRoller.EquipSlots.Contains(sl));
                if (slot != null) Offer(slots, slot, result, keepLeftovers: true); else Pool(result);
            }
        }
    }
}
