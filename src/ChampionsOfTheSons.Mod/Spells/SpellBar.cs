using ChampionsOfTheSons.Core.Rules;
using ChampionsOfTheSons.Mod.UI;
using SUI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static ChampionsOfTheSons.Mod.UI.PanelKit;
using static SUI.SUI;

namespace ChampionsOfTheSons.Mod.Spells;

/// <summary>The spell bar HUD: 6 slots with hotkey, spell name and a cooldown overlay, and an energy bar under them.</summary>
internal sealed class SpellBar
{
    public const string PanelId = "cots-spellbar";
    private const float Cell = 68, Gap = 8;
    private const float BarW = SpellBook.Slots * Cell + (SpellBook.Slots - 1) * Gap;
    /// <summary>Width of the slot row and energy bar; the XP bar below uses the same width so the two line up.</summary>
    public const float Width = BarW;
    /// <summary>Bottom edge of the spell bar above the screen bottom: just over the XP bar (see ProgressHud).</summary>
    public const float Bottom = 44;

    private sealed class Slot
    {
        public SContainerOptions Frame = null!, Cooldown = null!;
        public SLabelOptions Key = null!, Name = null!, Remaining = null!;
        public string? ShownSpell;
        public string ShownKey = "";
        public bool? ShownReady;
    }

    private readonly Slot[] _slots = new Slot[SpellBook.Slots];
    private SPanelOptions _root = null!;
    private SContainerOptions _energyFill = null!;
    private SLabelOptions _energyText = null!;

    public static SpellBar Instance { get; private set; } = null!;

    public bool Visible => _root.Root.activeSelf;

    public string SlotLabel(int i) => _slots[i].Name.TextObject.text;
    public string SlotKeyLabel(int i) => _slots[i].Key.TextObject.text;
    public string SlotRemaining(int i) => _slots[i].Remaining.TextObject.text;
    public float CooldownOverlayFraction(int i) => _slots[i].Cooldown.RectTransform.anchorMax.y;

    public void Build()
    {
        Instance = this;
        _root = RegisterNewPanel(PanelId, enableInput: false);
        var bar = SContainer.Anchor(AnchorType.BottomCenter).Pivot(0.5f, 0f).Size(BarW + 24, Cell + 54).Position(0, Bottom);
        _root.Add(bar);

        for (int i = 0; i < SpellBook.Slots; i++)
        {
            var s = new Slot();
            float x = 12 + i * (Cell + Gap);
            s.Frame = SContainer.TopLeft(x, 6).Size(Cell, Cell).Background(C(UiTheme.SlotFrameEmpty), EBackground.Round10);
            bar.Add(s.Frame);
            var inner = SContainer.Anchor(AnchorType.Fill).Margin(2f).Background(C("#0B0E12E8"), EBackground.None);
            s.Frame.Add(inner);
            s.Name = Label("", 13, UiTheme.Text, TextAlignmentOptions.Center, wrap: true).Anchor(AnchorType.Fill);
            s.Name.TextObject.margin = new Vector4(4, 14, 4, 4);
            s.Name.TextObject.enableAutoSizing = true;
            s.Name.TextObject.fontSizeMin = 8;
            s.Name.TextObject.fontSizeMax = 14;
            s.Name.TextObject.overflowMode = TextOverflowModes.Truncate;
            inner.Add(s.Name);

            // Cooldown overlay drains downward: anchored to the top, its height is the remaining fraction.
            s.Cooldown = SContainer.Background(C("#000000B0"), EBackground.None);
            inner.Add(s.Cooldown);
            s.Cooldown.Root.GetComponent<Image>().raycastTarget = false;

            s.Remaining = Label("", 20, UiTheme.Text, TextAlignmentOptions.Center).Anchor(AnchorType.Fill);
            inner.Add(s.Remaining);
            s.Key = Label("", 13, UiTheme.Gold, TextAlignmentOptions.TopLeft).Anchor(AnchorType.Fill);
            s.Key.TextObject.margin = new Vector4(5, 2, 2, 2);
            inner.Add(s.Key);
            SetOverlay(s, 0);
            _slots[i] = s;
        }

        var back = SContainer.TopLeft(12, 6 + Cell + 10).Size(BarW, 16).Background(C(UiTheme.BarBack), EBackground.None);
        bar.Add(back);
        _energyFill = SContainer.Background(C("#4D9DFF"), EBackground.None);
        back.Add(_energyFill);
        _energyFill.Root.GetComponent<Image>().raycastTarget = false;
        _energyText = Label("", 13, UiTheme.Text, TextAlignmentOptions.Center).Anchor(AnchorType.Fill);
        back.Add(_energyText);
        _root.Active(false);
    }

    private static void SetOverlay(Slot s, float remainingFraction)
    {
        var rt = s.Cooldown.RectTransform;
        rt.anchorMin = new Vector2(0, 1 - Mathf.Clamp01(remainingFraction));
        rt.anchorMax = new Vector2(1, 1);
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        s.Cooldown.Root.SetActive(remainingFraction > 0.001f);
    }

    public void SetVisible(bool v)
    {
        if (_root.Root.activeSelf != v) _root.Active(v);
    }

    public void Refresh()
    {
        var pc = PlayerState.Character;
        for (int i = 0; i < SpellBook.Slots; i++)
        {
            var s = _slots[i];
            var id = pc.Spells.Loadout[i];
            if (id != s.ShownSpell)
            {
                s.ShownSpell = id;
                s.Name.Text(id == null ? "" : ModContent.Text.Get(id + ".name", ModContent.Db.Spells[id].Name));
                s.ShownReady = null;
            }
            string key = SpellKeys.Label(i);
            if (key != s.ShownKey)
            {
                s.ShownKey = key;
                s.Key.Text(key);
            }

            if (id == null)
            {
                SetOverlay(s, 0);
                s.Remaining.Text("");
                if (s.ShownReady != true) { s.ShownReady = true; s.Frame.Root.GetComponent<Image>().color = C(UiTheme.SlotFrameEmpty); }
                continue;
            }
            double progress = pc.Spells.CooldownProgress(i);
            bool ready = progress >= 1;
            SetOverlay(s, (float)(1 - progress));
            if (ready)
            {
                s.Remaining.Text("");
            }
            else
            {
                double cd = ModContent.Db.Spells[id].Cooldown * pc.Stats.Get(StatIds.Cooldown);
                double rate = Math.Max(0.0001, pc.Stats.Get(StatIds.CooldownRate));
                s.Remaining.Text(Math.Ceiling((1 - progress) * cd / rate).ToString("0"));
            }
            if (s.ShownReady != ready)
            {
                s.ShownReady = ready;
                s.Frame.Root.GetComponent<Image>().color = C(ready ? "#B8923A" : UiTheme.SlotFrameEmpty);
            }
        }

        double max = Math.Max(1, SpellCaster.MaxEnergy);
        double cur = Math.Clamp(SpellCaster.Energy, 0, max);
        var rt = _energyFill.RectTransform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = new Vector2((float)(cur / max), 1);
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        _energyText.Text($"{cur:0} / {max:0}");
    }
}
