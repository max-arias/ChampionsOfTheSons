using ChampionsOfTheSons.Core.Rules;
using ChampionsOfTheSons.Mod.Settings;
using ChampionsOfTheSons.Mod.UI;
using RedLoader;

namespace ChampionsOfTheSons.Mod.Hints;

/// <summary>
/// One-time hint toasts (COS-97): every few seconds in the world, the first hint (<see cref="Core.Rules.Hints"/>) whose
/// condition holds and that this install has not shown yet appears on the HUD for a while, at most one a minute. The shown
/// ids are kept in the config file; the Hints setting turns them off. Self-test runs show hints only from their own steps.
/// </summary>
public sealed class HintsFeature : IModFeature
{
    private const float CheckEvery = 3f, Gap = 60f, FirstDelay = 20f, ShowSeconds = 10f;

    private static ConfigEntry<string>? _seen;
    private static float _sinceLast = Gap - FirstDelay;
    private float _check;

    /// <summary>The last hint shown (self-test).</summary>
    public static string? LastShown { get; private set; }

    public static IReadOnlySet<string> Seen =>
        (_seen?.Value ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToHashSet();

    public void Init()
    {
        _seen = Config.Category.CreateEntry("hints_seen", "", "Hints seen", "Internal: hints already shown. Clear it to see them again.");
        _seen.IsHidden = true;
    }

    public void Update(float dt, bool inWorld)
    {
        if (!inWorld || SelfTest.Enabled) return;
        _sinceLast += dt;
        _check -= dt;
        if (_check > 0) return;
        _check = CheckEvery;
        if (_sinceLast < Gap) return;
        TryShow();
    }

    /// <summary>Shows the next hint now if one applies. Returns its id.</summary>
    public static string? TryShow()
    {
        if (!Config.ShowHints.Value || CotsUi.AnyPanelOpen) return null;
        var hud = Hud.HudFeature.Instance;
        if (hud == null || !hud.Shown) return null;
        var id = Core.Rules.Hints.Next(Context(), Seen);
        if (id == null) return null;
        hud.Progress.HintBanner.Show(UiText.T("ui.hints.title", "Hint"), Text(id), ShowSeconds);
        MarkSeen(id);
        LastShown = id;
        return id;
    }

    /// <summary>Forgets every shown hint (self-test).</summary>
    public static void ResetSeen()
    {
        if (_seen == null) return;
        _seen.Value = "";
        ModKeys.Save();
    }

    private static void MarkSeen(string id)
    {
        if (_seen == null) return;
        var set = Seen.ToHashSet();
        set.Add(id);
        _seen.Value = string.Join(",", set.OrderBy(x => x));
        ModKeys.Save();
        _sinceLast = 0;
    }

    /// <summary>The hint's text with the player's current keys filled in.</summary>
    public static string Text(string id)
    {
        static string Key(string keyId) => KeyNames.Short(ModKeys.Value(keyId));
        // The guide book opens with the game's own key (no mod inventory key since 2026-11-21).
        static string BookKey() => KeyNames.Short(ModKeys.GameKeyOf("Book", "b"));
        return id switch
        {
            "welcome" => string.Format(UiText.T("ui.hints.welcome", "Enemies now have levels and drop gear. Your character and bag are in the guide book ({0}); the Help tab explains the rest."), BookKey()),
            "perk_points" => string.Format(UiText.T("ui.hints.perk_points", "You have perk points to spend. Open the perk tree with {0}."), Key("perks_key")),
            "first_loot" => string.Format(UiText.T("ui.hints.first_loot", "You picked up gear. Open your bag with {0} and equip it to raise your stats."), BookKey()),
            "spell_unlocked" => string.Format(UiText.T("ui.hints.spell_unlocked", "You unlocked a spell. Put it in a slot in the perk tree ({0}) and cast it with {1}."), Key("perks_key"), Key("spell_slot_1")),
            "no_spell" => string.Format(UiText.T("ui.hints.no_spell", "Spells unlock with perks. Look for them in the perk tree ({0})."), Key("perks_key")),
            "merge" => UiText.T("ui.hints.merge", "You have enough items of one rarity to merge them into a better one. See the Merge row on the Gear page."),
            "use_item" => UiText.T("ui.hints.use_item", "An item in your bag can be used: open its menu and pick Use."),
            "streak" => UiText.T("ui.hints.streak", "Three quick kills start a kill streak. Keep killing to raise its multiplier; the XP pays out when the timer runs out."),
            "coop" => UiText.T("ui.hints.coop", "In co-op, kills near you share XP, and drops are rolled with the whole party's magic find."),
            "settings" => string.Format(UiText.T("ui.hints.settings", "Keys, sound and HUD options are in the mod settings ({0})."), Key("settings_key")),
            _ => id,
        };
    }

    private static HintContext Context()
    {
        var pc = PlayerState.Character;
        var bag = pc.Inventory.Slots.Where(kv => kv.Key.StartsWith("bag.", StringComparison.Ordinal)).Select(kv => kv.Value).ToList();
        var gear = bag.Where(i => !Consumables.CanUse(i)).ToList();
        bool merge = gear.GroupBy(i => i.Rarity).Any(g => g.Count() >= Merger.Need(g.Key));
        return new HintContext(
            pc.Progress.Level,
            pc.Perks.AvailablePoints(pc.Progress),
            bag.Count,
            pc.Inventory.Equipped.Count(),
            pc.Perks.UnlockedSpells.Count,
            pc.Spells.Loadout.Count(x => x != null),
            Net.CoopSession.Active,
            merge,
            bag.Any(Consumables.CanUse),
            pc.Massacre.Active);
    }

    public void RegisterSelfTests() => HintsSelfTest.Register();
}

internal static class HintsSelfTest
{
    public static void Register()
    {
        SelfTest.Add(1f, "hints: unpause", TestKit.Unpause);
        SelfTest.Add(2f, "hints: first hint shows once", () =>
        {
            HintsFeature.ResetSeen();
            Config.ShowHints.Value = true;
            var first = HintsFeature.TryShow();
            var banner = Hud.HudFeature.Instance?.Progress.HintBanner;
            SelfTest.Check("hints: the welcome hint toasts", first == "welcome" && banner != null && banner.Visible, $"{first}, visible {banner?.Visible}");
            var again = HintsFeature.TryShow();
            SelfTest.Check("hints: a shown hint does not come back", again != "welcome", again ?? "none");
        });
        SelfTest.Add(2.5f, "hints: shot", () => TestKit.Screenshot("hints-01-toast"));
        SelfTest.Add(3f, "hints: text and setting", () =>
        {
            string text = HintsFeature.Text("perk_points");
            SelfTest.Check("hints: hint text carries the player's key", text.Contains(KeyNames.Short(ModKeys.Value("perks_key"))), text);
            Config.ShowHints.Value = false;
            SelfTest.Check("hints: the setting turns hints off", HintsFeature.TryShow() == null);
            Config.ShowHints.Value = true;
            HintsFeature.ResetSeen();
        });
    }
}
