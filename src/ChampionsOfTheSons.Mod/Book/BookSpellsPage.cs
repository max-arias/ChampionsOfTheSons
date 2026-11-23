using ChampionsOfTheSons.Core.Content;
using ChampionsOfTheSons.Core.Rules;
using ChampionsOfTheSons.Mod.Perks;
using ChampionsOfTheSons.Mod.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ChampionsOfTheSons.Mod.Book;

/// <summary>
/// The book's Spells page (COS-168): every spell with its level, cost and cooldown, a warning when it costs more than the whole
/// energy bar (COS-172), Learn (perk points) or the six bar slots, and a free refund of every spell (COS-170).
/// </summary>
internal static class BookSpellsPage
{
    private const float Left = 62, Right = 16, Header = 80;
    private const float ListY = Header + 10, RowH = 110, RowW = PageKit.W - Left - Right;
    private const float FootY = PageKit.H - 92 - 50, FootH = 44;
    private const float NextX = PageKit.W - Right - 56, PrevX = NextX - 56 - 90;
    private const float RefundX = Left, RefundW = 210;
    // COS-182: the bar slots are a small copy of the action bar: six frames, each with its key underneath.
    private const float SlotW = 40, SlotGap = 4, SlotsX = Left + RowW - SpellBook.Slots * (SlotW + SlotGap), LearnW = 190, LearnX = Left + RowW - LearnW;
    public const int RowsPerPage = 7;
    private const float ActionY = 50, ActionH = 40, KeyY = ActionY + ActionH + 1, KeyH = 16;
    private static readonly Color SlotFrame = PageKit.Hex("#3A2E24"), SlotLit = PageKit.Hex("#C8942E"), SlotEmpty = PageKit.Hex("#5A4A3A");
    private const float ArmSeconds = 4;

    private sealed class Row
    {
        public Image Shade = null!, Icon = null!, ActionBg = null!;
        public TextMeshProUGUI Name = null!, Short = null!, Meta = null!, Warn = null!, ActionText = null!;
        public BarSlot[] Slots = new BarSlot[SpellBook.Slots];
    }

    private sealed class BarSlot
    {
        public Image Bg = null!, Icon = null!;
        public Image[] Frame = null!;
        public TextMeshProUGUI Key = null!;
    }

    private static RectTransform _root = null!;
    private static TextMeshProUGUI _points = null!, _pager = null!, _refundText = null!, _barHint = null!;
    private static Image _refundBg = null!;
    private static readonly List<Row> Rows = new();
    private static bool _armed;
    private static float _armedAt;

    public static int Page { get; set; }
    public static int Pages => Math.Max(1, (AllSpells().Count + RowsPerPage - 1) / RowsPerPage);

    private static PlayerCharacter Pc => PlayerState.Character;

    /// <summary>Spells in the order the list shows them: by level, then name.</summary>
    public static List<SpellDef> AllSpells() => ModContent.Db.Spells.Values.OrderBy(s => s.LevelReq).ThenBy(s => s.Name, StringComparer.Ordinal).ToList();

    public static RectTransform Build(RectTransform page)
    {
        _root = PageKit.Fill(page, "spells");
        PageKit.Box(_root, "titlebar", 0, 0, PageKit.W, Header - 14, PageKit.TitleBar);
        PageKit.Text(_root, "title", Left, 12, 300, Header - 26, UiText.T("ui.book.tab.spells", "Spells").ToUpperInvariant(), 26, PageKit.TitleText);
        _points = PageKit.Text(_root, "points", Left + 220, 12, PageKit.W - Left - Right - 220, Header - 26, "", 22, PageKit.TitleText, TextAlignmentOptions.MidlineRight);
        for (int i = 0; i < RowsPerPage; i++)
        {
            float y = ListY + i * RowH;
            var r = new Row
            {
                Shade = PageKit.Box(_root, "spell-shade-" + i, Left - 6, y, RowW + 12, RowH - 6, i % 2 == 0 ? new Color(0, 0, 0, 0.06f) : Color.clear),
                Icon = PageKit.Box(_root, "spell-icon-" + i, Left, y + 10, 64, 64, Color.white),
                // COS-177: name, then a one-sentence summary across the row, then numbers and mastery left of the bar slots.
                Name = PageKit.Text(_root, "spell-name-" + i, Left + 74, y + 2, RowW - 80, 30, "", 24, PageKit.Ink),
                Short = PageKit.Text(_root, "spell-short-" + i, Left + 74, y + 30, RowW - 80, 22, "", 16, PageKit.Ink),
                Meta = PageKit.Text(_root, "spell-meta-" + i, Left + 74, y + 52, SlotsX - Left - 80, 20, "", 15, PageKit.Ink),
                Warn = PageKit.Text(_root, "spell-warn-" + i, Left + 74, y + 72, SlotsX - Left - 80, 20, "", 15, PageKit.Select),
                ActionBg = PageKit.Box(_root, "spell-act-bg-" + i, LearnX, y + ActionY, LearnW, ActionH, PageKit.PaperShade),
                ActionText = PageKit.Text(_root, "spell-act-" + i, LearnX, y + ActionY, LearnW, ActionH, "", 17, PageKit.Ink, TextAlignmentOptions.Center),
            };
            r.Icon.preserveAspect = true;
            for (int s = 0; s < SpellBook.Slots; s++)
            {
                float x = SlotsX + s * (SlotW + SlotGap);
                var slot = new BarSlot
                {
                    Bg = PageKit.Box(_root, $"spell-slot-bg-{i}-{s}", x, y + ActionY, SlotW, ActionH, SlotFrame),
                    Icon = PageKit.Box(_root, $"spell-slot-icon-{i}-{s}", x + 4, y + ActionY + 4, SlotW - 8, ActionH - 8, Color.white),
                    Frame = PageKit.Frame(_root, x, y + ActionY, SlotW, ActionH, SlotEmpty, 3),
                    Key = PageKit.Text(_root, $"spell-slot-key-{i}-{s}", x - 2, y + KeyY, SlotW + 4, KeyH, "", 12, PageKit.Ink, TextAlignmentOptions.Center),
                };
                slot.Icon.preserveAspect = true;
                slot.Key.enableAutoSizing = true;
                slot.Key.fontSizeMin = 9;
                slot.Key.fontSizeMax = 12;
                r.Slots[s] = slot;
            }
            Rows.Add(r);
        }
        PageKit.Box(_root, "prev-bg", PrevX, FootY, 56, FootH, PageKit.TitleBar);
        PageKit.Text(_root, "prev", PrevX, FootY, 56, FootH, "<", 30, PageKit.TitleText, TextAlignmentOptions.Center);
        PageKit.Box(_root, "next-bg", NextX, FootY, 56, FootH, PageKit.TitleBar);
        PageKit.Text(_root, "next", NextX, FootY, 56, FootH, ">", 30, PageKit.TitleText, TextAlignmentOptions.Center);
        _pager = PageKit.Text(_root, "pager", PrevX + 56, FootY, 90, FootH, "", 26, PageKit.Ink, TextAlignmentOptions.Center);
        _barHint = PageKit.Text(_root, "bar-hint", Left, FootY - 26, RowW, 22, "", 15, PageKit.Ink, TextAlignmentOptions.MidlineRight);
        _refundBg = PageKit.Box(_root, "refund-bg", RefundX, FootY, RefundW, FootH, PageKit.TitleBar);
        _refundText = PageKit.Text(_root, "refund", RefundX, FootY, RefundW, FootH, "", 20, PageKit.TitleText, TextAlignmentOptions.Center);
        return _root;
    }

    public static void Refresh(List<PageHit> hits)
    {
        if (_armed && Time.unscaledTime - _armedAt > ArmSeconds) _armed = false;
        int avail = Pc.Perks.AvailablePoints(Pc.Progress);
        var faint = PageKit.HexOf(PageKit.InkFaint);
        _points.text = $"{UiText.T("ui.perks.points", "Points")}: <b>{avail}</b>   {UiText.T("ui.book.spells.energy", "Max energy")} <b>{MaxEnergyText()}</b>";
        _barHint.text = string.Format(UiText.T("ui.book.spells.bar_hint", "Click a slot on the right to put a spell on your bar ({0} - {1})."),
            ChampionsOfTheSons.Mod.Spells.SpellKeys.Label(0), ChampionsOfTheSons.Mod.Spells.SpellKeys.Label(SpellBook.Slots - 1));
        var all = AllSpells();
        Page = Math.Clamp(Page, 0, Pages - 1);
        for (int i = 0; i < Rows.Count; i++)
        {
            var r = Rows[i];
            int index = Page * RowsPerPage + i;
            bool on = index < all.Count;
            Show(r, on);
            if (!on) continue;
            var def = all[index];
            float y = ListY + i * RowH;
            bool learned = Pc.Perks.UnlockedSpells.Contains(def.Id);
            bool tooDear = PerkInfo.TooDear(def);
            var sprite = Icons.IconCache.GetSprite(def.Id);
            r.Icon.sprite = sprite;
            r.Icon.enabled = sprite != null;
            r.Name.text = PerkInfo.Name(def.Id, def.Name);
            r.Name.color = tooDear ? PageKit.Select : learned ? PageKit.TitleBar : PageKit.Ink;
            r.Short.text = ModContent.Text.Get(def.Id + ".short", "");
            r.Meta.text = PerkInfo.SpellMeta(def, faint);
            // COS-176: mastery on the third line, the energy warning wins the line when there is one.
            r.Warn.text = tooDear ? PerkInfo.TooDearText() : PerkInfo.MasteryLine(def.Id);
            r.Warn.color = tooDear ? PageKit.Select : PageKit.Good;
            r.ActionBg.enabled = r.ActionText.enabled = !learned;
            foreach (var sl in r.Slots) ShowSlot(sl, learned);
            if (!learned)
            {
                var res = Pc.Perks.CanUnlockSpell(def.Id, Pc.Progress);
                r.ActionText.text = res == BuyResult.Ok
                    ? string.Format(UiText.T("ui.spells.unlock", "Unlock ({0} points)"), PerkTree.SpellUnlockCost)
                    : res == BuyResult.LevelTooLow ? string.Format(UiText.T("ui.spells.need_level", "Needs level {0}"), def.LevelReq)
                    : UiText.T("ui.spells.need_points", "Not enough points");
                r.ActionBg.color = res == BuyResult.Ok ? PageKit.Good : PageKit.PaperShade;
                r.ActionText.color = res == BuyResult.Ok ? PageKit.TitleText : PageKit.InkFaint;
                hits.Add(new PageHit("spell.learn." + def.Id, LearnX, y + ActionY, LearnW, ActionH));
            }
            else
            {
                for (int s = 0; s < SpellBook.Slots; s++)
                {
                    // Lit with this spell's icon where it sits; another spell's icon dimmed where that one sits; plain when empty.
                    var sl = r.Slots[s];
                    var occupant = Pc.Spells.Loadout[s];
                    bool here = occupant == def.Id;
                    var occSprite = occupant != null ? Icons.IconCache.GetSprite(occupant) : null;
                    sl.Icon.sprite = occSprite;
                    sl.Icon.enabled = occSprite != null;
                    sl.Icon.color = here ? Color.white : new Color(1, 1, 1, 0.3f);
                    foreach (var f in sl.Frame) f.color = here ? SlotLit : SlotEmpty;
                    sl.Key.text = ChampionsOfTheSons.Mod.Spells.SpellKeys.Label(s);
                    sl.Key.color = here ? SlotLit : PageKit.Ink;
                    hits.Add(new PageHit($"spell.slot.{s}.{def.Id}", SlotsX + s * (SlotW + SlotGap), y + ActionY, SlotW, ActionH));
                }
            }
            hits.Add(new PageHit(def.Id, Left, y, RowW, RowH - 6)); // the row itself: hover shows the spell on the card
        }
        _pager.text = $"{Page + 1}/{Pages}";
        hits.Add(new PageHit("prev", PrevX, FootY, 56, FootH));
        hits.Add(new PageHit("next", NextX, FootY, 56, FootH));
        _refundText.text = _armed ? UiText.T("ui.book.perks.confirm", "Click again to refund") : UiText.T("ui.book.spells.refund", "Refund all spells");
        _refundBg.color = _armed ? PageKit.Select : PageKit.TitleBar;
        hits.Add(new PageHit("spells.refund", RefundX, FootY, RefundW, FootH));
    }

    private static string MaxEnergyText() => ChampionsOfTheSons.Mod.Spells.SpellCaster.MaxEnergy.ToString("0", System.Globalization.CultureInfo.InvariantCulture);

    private static void Show(Row r, bool on)
    {
        r.Shade.enabled = r.Icon.enabled = on;
        r.Name.enabled = r.Short.enabled = r.Meta.enabled = r.Warn.enabled = on;
        r.ActionBg.enabled = r.ActionText.enabled = on;
        foreach (var sl in r.Slots) ShowSlot(sl, on);
    }

    private static void ShowSlot(BarSlot sl, bool on)
    {
        sl.Bg.enabled = sl.Key.enabled = on;
        if (!on) sl.Icon.enabled = false;
        foreach (var f in sl.Frame) f.enabled = on;
    }

    /// <summary>A click on this page; null when the key is not ours.</summary>
    public static string? Click(string key)
    {
        if (key.StartsWith("spell.learn.", StringComparison.Ordinal))
        {
            var r = Pc.Perks.UnlockSpell(key.Substring("spell.learn.".Length), Pc.Progress);
            if (r == BuyResult.Ok) Audio.ModAudio.Play(Audio.SoundCues.PerkBought);
            return r == BuyResult.Ok ? "learned" : "refused " + r;
        }
        if (key.StartsWith("spell.slot.", StringComparison.Ordinal))
        {
            var rest = key.Substring("spell.slot.".Length);
            int dot = rest.IndexOf('.');
            if (dot <= 0 || !int.TryParse(rest.AsSpan(0, dot), out int slot)) return "bad";
            string id = rest.Substring(dot + 1);
            if (!Pc.Perks.UnlockedSpells.Contains(id)) return "not-learned";
            // Clicking the slot it already sits in takes it off the bar.
            bool here = Pc.Spells.Loadout[slot] == id;
            return Pc.Spells.Equip(slot, here ? null : id) ? (here ? "unslotted" : "slotted") : "refused";
        }
        if (key == "spells.refund")
        {
            if (!_armed || Time.unscaledTime - _armedAt > ArmSeconds) { _armed = true; _armedAt = Time.unscaledTime; return "armed"; }
            _armed = false;
            int n = Pc.RespecSpells();
            if (n > 0) Audio.ModAudio.Play(Audio.SoundCues.Craft);
            return "refunded " + n;
        }
        if (key.StartsWith("spell.", StringComparison.Ordinal)) return "row";
        return null;
    }
}
