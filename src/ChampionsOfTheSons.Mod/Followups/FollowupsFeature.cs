using ChampionsOfTheSons.Core;
using ChampionsOfTheSons.Core.Rules;
using ChampionsOfTheSons.Mod.Bridge;
using Sons.Items.Core;
using TheForest.Utils;
using UnityEngine;

namespace ChampionsOfTheSons.Mod.Followups;

/// <summary>
/// Wave 1 follow-ups (COS-42, COS-49): event perks (dodge heal and shield, Adrenaline Rush), utility perks (carry limits and daily
/// grants through the game's item data) and the time-based unique flags. State classes live in Core (PerkEffects.cs, UniqueFlags.cs);
/// this is the game side.
/// </summary>
public sealed class FollowupsFeature : IModFeature
{
    private static readonly Rng Rng = Rng.FromTime();

    public static readonly DodgeShield Shield = new();
    public static readonly AdrenalineRush Rush = new();
    public static readonly UniqueRuntime Unique = new();

    private static PlayerCharacter Pc => PlayerState.Character;

    private static Vector3? _lastPos;
    private static string _rushSig = "", _uniqueSig = "";
    private static string _capacitySig = "";
    private static float _scan;

    /// <summary>Original carry limits of items we raised, by item id.</summary>
    private static readonly Dictionary<int, int> BaseMax = new();

    /// <summary>Game items whose carry limit the mod has raised this session (their counts are saved, COS-100).</summary>
    internal static IReadOnlyCollection<int> RaisedItems => BaseMax.Keys;

    public void Init() { }

    public void OnGameStart()
    {
        Shield.Reset();
        Rush.Reset();
        Unique.Reset();
        _capacitySig = "";
    }

    public void RegisterSelfTests() => FollowupsSelfTest.Register();

    public void Update(float dt, bool inWorld)
    {
        if (!inWorld || LocalPlayer.Vitals == null) return;
        Frame(dt);
        _scan += dt;
        if (_scan >= 1f)
        {
            _scan = 0;
            ApplyCapacities();
            CheckNewDay();
        }
    }

    /// <summary>One frame of perk and unique-flag timers (self-tests step it themselves).</summary>
    public static void Frame(float dt)
    {
        Shield.Tick(dt);
        Rush.Tick(dt);
        var p = LocalPlayer.Transform.position;
        bool moving = _lastPos is not { } last || new Vector2(p.x - last.x, p.z - last.z).magnitude / Mathf.Max(dt, 0.0001f) > 0.5f;
        _lastPos = p;
        double drain = Unique.Tick(dt, moving, Pc.Stats);
        var health = LocalPlayer.Vitals?.Health;
        if (drain > 0 && health != null && health._currentValue > 1)
            health.SetCurrentValue(Mathf.Max(1, health._currentValue - (float)drain * health.GetMax()));
        SyncSources();
    }

    /// <summary>Pushes the Adrenaline Rush and unique-flag modifiers into the stat sheet when they changed.</summary>
    public static void SyncSources()
    {
        var rush = Rush.Modifiers(Pc.Perks);
        string rs = string.Join(";", rush.Select(m => $"{m.Stat}{m.Value}"));
        if (rs != _rushSig)
        {
            _rushSig = rs;
            if (rush.Count == 0) Pc.Stats.RemoveSource(AdrenalineRush.Source);
            else Pc.Stats.SetSource(AdrenalineRush.Source, rush);
        }
        var uni = Unique.Modifiers(Pc.Stats);
        string us = string.Join(";", uni.Select(m => $"{m.Stat}{m.Value}"));
        if (us != _uniqueSig)
        {
            _uniqueSig = us;
            // Changing this source changes the flags' own inputs only through stats they do not read, so no feedback.
            if (uni.Count == 0) Pc.Stats.RemoveSource(UniqueRuntime.Source);
            else Pc.Stats.SetSource(UniqueRuntime.Source, uni);
        }
    }

    // ---------------------------------------------------------------- player hooks (called from PlayerBridge)

    /// <summary>The player dodged a hit: Improved Dodges heals, Deployable Shield grants a shield, Wind Armor heals and buffs.</summary>
    public static void OnDodge()
    {
        var health = LocalPlayer.Vitals?.Health;
        if (health == null) return;
        double max = health.GetMax();
        if (Pc.Perks.TimesBought(PerkEffectIds.ImprovedDodges) > 0) PlayerEffects.Heal(PerkTuning.DodgeHealFraction * max);
        Shield.Grant(PerkTuning.ShieldFor(Pc.Perks, max));
        double heal = Unique.OnDodge(Pc.Stats);
        if (heal > 0) PlayerEffects.Heal(heal * max);
        SyncSources();
    }

    /// <summary>A hit is about to land for <paramref name="damage"/>: the shield soaks first, Adrenaline Rush may trigger, King Qruie remembers it.
    /// Returns the damage that still lands.</summary>
    public static double OnIncoming(double damage, DamageKind kind)
    {
        var health = LocalPlayer.Vitals?.Health;
        if (health == null || damage <= 0) return damage;
        damage = Shield.Absorb(damage);
        if (damage <= 0) return 0;
        if (kind == DamageKind.Physical) Unique.OnPhysicalDamageTaken(damage);
        if (Rush.TryTrigger(Pc.Perks, health._currentValue - damage, health.GetMax())) SyncSources();
        return damage;
    }

    // ---------------------------------------------------------------- player attack hooks (called from EnemyPatches)

    /// <summary>The local player's weapon hit is built: unique flags change the damage, Death Pact charges health, Infinity Loop trims a cooldown.</summary>
    public static OutgoingHit OnLocalHit(OutgoingHit hit, WeaponClass weapon)
    {
        var s = Pc.Stats;
        var health = LocalPlayer.Vitals?.Health;
        double amount = hit.Amount;
        if (weapon == WeaponClass.Melee) amount += Unique.KingQruieBonus(s);
        if (health != null && s.IsOn(UniqueFlagIds.DeathPact))
        {
            double max = health.GetMax();
            amount *= UniqueRuntime.DeathPactMultiplier(s, health._currentValue, max);
            health.SetCurrentValue(health._currentValue - (float)UniqueRuntime.DeathPactCost(s, health._currentValue, max));
        }
        if (weapon is WeaponClass.Melee or WeaponClass.Ranged && s.IsOn(UniqueFlagIds.InfinityLoop))
            Pc.Spells.ReduceRandomCooldown(UniqueTuning.InfinityLoopSeconds, s, Rng);
        return hit with { Amount = amount };
    }

    // ---------------------------------------------------------------- utility perks

    /// <summary>The game's items with exactly this name (case-insensitive).</summary>
    public static IEnumerable<ItemData> ItemsFor(string name)
    {
        var list = ItemDatabaseManager.Items;
        for (int i = 0; i < list.Count; i++)
        {
            var d = list[i];
            if (d != null && string.Equals(d.Name, name, StringComparison.OrdinalIgnoreCase)) yield return d;
        }
    }

    /// <summary>The most specific keyword (longest) among those that match an item decides its entry, so "small rock" beats "rock".</summary>
    private static Dictionary<int, (ItemData Item, int Amount)> Resolve(Dictionary<string, int> totals)
    {
        var best = new Dictionary<int, (ItemData Item, int Amount, int Len)>();
        foreach (var (keyword, amount) in totals)
            foreach (var d in ItemsFor(keyword))
                if (!best.TryGetValue(d.Id, out var cur) || keyword.Length > cur.Len)
                    best[d.Id] = (d, amount, keyword.Length);
        return best.ToDictionary(kv => kv.Key, kv => (kv.Value.Item, kv.Value.Amount));
    }

    /// <summary>Sets the carry limit of every item a bought capacity perk or a worn carry stat names to its original limit plus
    /// the bonus; undoes it when the perks or items are gone.</summary>
    public static void ApplyCapacities(bool force = false)
    {
        var totals = UtilityPerks.Totals(Pc.Perks, UtilityKind.Capacity);
        foreach (var (name, amount) in ItemCarry.Totals(Pc.Inventory.Equipped.Select(kv => kv.Value)))
            totals[name] = totals.GetValueOrDefault(name) + amount;
        foreach (var (name, amount) in ItemCarry.FromStrength(Pc.Attributes.Get(StatIds.Strength)))
            totals[name] = totals.GetValueOrDefault(name) + amount;
        string sig = string.Join(";", totals.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key}={kv.Value}"));
        if (!force && sig == _capacitySig) return;
        _capacitySig = sig;
        // Back to the originals first, then raise what the perks name.
        var list = ItemDatabaseManager.Items;
        for (int i = 0; i < list.Count; i++)
        {
            var d = list[i];
            if (d != null && BaseMax.TryGetValue(d.Id, out var orig)) d.MaxAmount = orig;
        }
        foreach (var (id, (item, amount)) in Resolve(totals))
        {
            if (!BaseMax.ContainsKey(id)) BaseMax[id] = item.MaxAmount;
            item.MaxAmount = BaseMax[id] + amount;
        }
    }

    /// <summary>Gives the daily-grant perks' items (up to the carry limit; the rest is lost).</summary>
    public static Dictionary<string, int> GrantDaily()
    {
        var given = new Dictionary<string, int>();
        var inv = LocalPlayer.Inventory;
        if (inv == null) return given;
        foreach (var (id, (item, amount)) in Resolve(UtilityPerks.Totals(Pc.Perks, UtilityKind.DailyGrant)))
        {
            int room = inv.GetAvailableSpaceForItem(id);
            int n = Math.Min(amount, room);
            if (n <= 0) continue;
            if (inv.AddItem(id, n, false, false, null)) given[item.Name] = n;
        }
        return given;
    }

    private static void CheckNewDay()
    {
        int day;
        try { day = Sons.Environment.TimeOfDayHolder.GetTimeOfDay().Days; }
        catch { return; }
        // The day is kept with the character, so a reload neither pays twice nor loses a grant (COS-100).
        if (!PlayerState.Character.Progress.TakeDailyGrant(day)) return;
        var given = GrantDaily();
        if (given.Count > 0) LootFeatureMessage(string.Format(UI.UiText.T("ui.followups.daily", "A new day: {0}"), string.Join(", ", given.Select(kv => $"{kv.Value} {kv.Key}"))));
    }

    private static void LootFeatureMessage(string text) => Loot.LootFeature.Message(text);
}
