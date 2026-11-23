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
    private const float ListY = Header + 10, RowH = 96, RowW = PageKit.W - Left - Right;
    private const float FootY = PageKit.H - 92 - 50, FootH = 44;
    private const float NextX = PageKit.W - Right - 56, PrevX = NextX - 56 - 90;
    private const float RefundX = Left, RefundW = 210;
    private const float SlotW = 34, SlotGap = 4, SlotsX = Left + RowW - SpellBook.Slots * (SlotW + SlotGap), LearnW = 190, LearnX = Left + RowW - LearnW;
    public const int RowsPerPage = 8;
    private const float ArmSeconds = 4;

    private sealed class Row
    {
        public Image Shade = null!, Icon = null!, ActionBg = null!;
        public TextMeshProUGUI Name = null!, Meta = null!, Warn = null!, ActionText = null!;
        public (Image Bg, TextMeshProUGUI Text)[] Slots = new (Image, TextMeshProUGUI)[SpellBook.Slots];
    }

    private static RectTransform _root = null!;
    private static TextMeshProUGUI _points = null!, _pager = null!, _refundText = null!;
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
                Name = PageKit.Text(_root, "spell-name-" + i, Left + 74, y + 4, SlotsX - Left - 80, 32, "", 24, PageKit.Ink),
                Meta = PageKit.Text(_root, "spell-meta-" + i, Left + 74, y + 36, SlotsX - Left - 80, 24, "", 16, PageKit.InkFaint),
                Warn = PageKit.Text(_root, "spell-warn-" + i, Left + 74, y + 60, RowW - 80, 26, "", 15, PageKit.Select, wrap: true),
                ActionBg = PageKit.Box(_root, "spell-act-bg-" + i, LearnX, y + 18, LearnW, 44, PageKit.PaperShade),
                ActionText = PageKit.Text(_root, "spell-act-" + i, LearnX, y + 18, LearnW, 44, "", 18, PageKit.Ink, TextAlignmentOptions.Center),
            };
            r.Icon.preserveAspect = true;
            for (int s = 0; s < SpellBook.Slots; s++)
            {
                float x = SlotsX + s * (SlotW + SlotGap);
                r.Slots[s] = (PageKit.Box(_root, $"spell-slot-bg-{i}-{s}", x, y + 18, SlotW, 44, PageKit.PaperShade),
                    PageKit.Text(_root, $"spell-slot-{i}-{s}", x, y + 18, SlotW, 44, (s + 1).ToString(), 20, PageKit.Ink, TextAlignmentOptions.Center));
            }
            Rows.Add(r);
        }
        PageKit.Box(_root, "prev-bg", PrevX, FootY, 56, FootH, PageKit.TitleBar);
        PageKit.Text(_root, "prev", PrevX, FootY, 56, FootH, "<", 30, PageKit.TitleText, TextAlignmentOptions.Center);
        PageKit.Box(_root, "next-bg", NextX, FootY, 56, FootH, PageKit.TitleBar);
        PageKit.Text(_root, "next", NextX, FootY, 56, FootH, ">", 30, PageKit.TitleText, TextAlignmentOptions.Center);
        _pager = PageKit.Text(_root, "pager", PrevX + 56, FootY, 90, FootH, "", 26, PageKit.Ink, TextAlignmentOptions.Center);
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
            r.Meta.text = PerkInfo.SpellMeta(def, faint);
            r.Warn.text = tooDear ? PerkInfo.TooDearText() : "";
            r.ActionBg.enabled = r.ActionText.enabled = !learned;
            foreach (var (bg, text) in r.Slots) bg.enabled = text.enabled = learned;
            if (!learned)
            {
                var res = Pc.Perks.CanUnlockSpell(def.Id, Pc.Progress);
                r.ActionText.text = res == BuyResult.Ok
                    ? string.Format(UiText.T("ui.spells.unlock", "Unlock ({0} points)"), PerkTree.SpellUnlockCost)
                    : res == BuyResult.LevelTooLow ? string.Format(UiText.T("ui.spells.need_level", "Needs level {0}"), def.LevelReq)
                    : UiText.T("ui.spells.need_points", "Not enough points");
                r.ActionBg.color = res == BuyResult.Ok ? PageKit.Good : PageKit.PaperShade;
                r.ActionText.color = res == BuyResult.Ok ? PageKit.TitleText : PageKit.InkFaint;
                hits.Add(new PageHit("spell.learn." + def.Id, LearnX, y + 18, LearnW, 44));
            }
            else
            {
                for (int s = 0; s < SpellBook.Slots; s++)
                {
                    bool here = Pc.Spells.Loadout[s] == def.Id;
                    r.Slots[s].Bg.color = here ? PageKit.TitleBar : PageKit.PaperShade;
                    r.Slots[s].Text.color = here ? PageKit.TitleText : PageKit.Ink;
                    hits.Add(new PageHit($"spell.slot.{s}.{def.Id}", SlotsX + s * (SlotW + SlotGap), y + 18, SlotW, 44));
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
        r.Name.enabled = r.Meta.enabled = r.Warn.enabled = on;
        r.ActionBg.enabled = r.ActionText.enabled = on;
        foreach (var (bg, text) in r.Slots) bg.enabled = text.enabled = on;
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
