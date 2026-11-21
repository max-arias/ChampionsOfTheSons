using ChampionsOfTheSons.Core.Rules;
using ChampionsOfTheSons.Mod.ActionBar;
using ChampionsOfTheSons.Mod.Hud;
using ChampionsOfTheSons.Mod.UI;
using SUI;
using TMPro;
using UnityEngine;
using static SUI.SUI;

namespace ChampionsOfTheSons.Mod.Spells;

/// <summary>
/// The spell group in the middle of the action bar (COS-151): 6 slots with icon, key, cooldown ash and a rune glow when castable
/// (red-tinted icon when energy is short), and the energy channel under them. Built and shown by <see cref="ActionBar.ActionBar"/>.
/// </summary>
internal sealed class SpellBar
{
    public const float Cell = 64, Gap = 6;
    public const float Width = SpellBook.Slots * Cell + (SpellBook.Slots - 1) * Gap;
    public const float ChannelH = 22;
    public const float Height = Cell + 6 + ChannelH;

    private readonly BarSlot[] _slots = new BarSlot[SpellBook.Slots];
    private readonly string?[] _shownSpell = new string?[SpellBook.Slots];
    private readonly string[] _shownKey = new string[SpellBook.Slots];
    private readonly string[] _shownRemaining = new string[SpellBook.Slots];
    private SContainerOptions _energyFill = null!;
    private SLabelOptions _energyText = null!;
    private string _energyShown = "";
    private GameObject _visibleRoot = null!;

    public static SpellBar Instance { get; private set; } = null!;

    public bool Visible => _visibleRoot != null && _visibleRoot.activeSelf;

    public string SlotLabel(int i) => _slots[i].Name.TextObject.text;
    public string SlotKeyLabel(int i) => _slots[i].Key.TextObject.text;
    public string SlotRemaining(int i) => _slots[i].Center.TextObject.text;
    public float CooldownOverlayFraction(int i) => _slots[i].CooldownFraction;
    public string SlotGlow(int i) => _slots[i].GlowHex;
    public string EnergyText => _energyText.TextObject.text;

    public enum SlotState { Empty, Ready, Cooldown, Starved, Silenced }

    /// <summary>Each slot's state at the last refresh (self-test).</summary>
    public readonly SlotState[] States = new SlotState[SpellBook.Slots];

    /// <summary>Builds the group at (x, y) inside <paramref name="parent"/>; <paramref name="visibleRoot"/> is the action bar's panel.</summary>
    public void Build(SContainerOptions parent, float x, float y, GameObject visibleRoot)
    {
        Instance = this;
        _visibleRoot = visibleRoot;
        for (int i = 0; i < SpellBook.Slots; i++)
        {
            _slots[i] = new BarSlot(parent, x + i * (Cell + Gap), y, Cell, 44);
            _shownKey[i] = "";
            _shownRemaining[i] = "";
        }

        // Energy: a bone channel whose carved groove holds the fill (groove inset matches tools/hud_art.py bar_channel at this scale).
        const float scale = ChannelH / 40f;
        var channel = HudArt.Piece(HudArt.BarChannel, scale, UiTheme.BoneDark).TopLeft(x, y + Cell + 6).Size(Width, ChannelH);
        parent.Add(channel);
        var groove = SContainer.TopLeft(22 * scale, 11 * scale).Size(Width - 44 * scale, ChannelH - 22 * scale);
        channel.Add(groove);
        _energyFill = HudArt.Piece(HudArt.BarFill, 0.5f, UiTheme.EnergyFill, UiTheme.EnergyFill);
        groove.Add(_energyFill);
        _energyText = HudKit.Label("", 11, UiTheme.Bone, TextAlignmentOptions.Center).Anchor(AnchorType.Fill);
        _energyText.TextObject.fontStyle = FontStyles.Bold;
        BarSlot.Outline(_energyText, 0.3f);
        channel.Add(_energyText);
        HudKit.NoRaycast(channel.Root);
    }

    public void Refresh()
    {
        var pc = PlayerState.Character;
        double energy = SpellCaster.Energy;
        bool silenced = pc.Stats.IsOn(StatIds.Silenced);
        for (int i = 0; i < SpellBook.Slots; i++)
        {
            var s = _slots[i];
            var id = pc.Spells.Loadout[i];
            if (id != _shownSpell[i])
            {
                _shownSpell[i] = id;
                s.Name.Text(id == null ? "" : ModContent.Text.Get(id + ".name", ModContent.Db.Spells[id].Name));
                s.Name.Active(!s.ShowContentIcon(id)); // the icon replaces the name text when there is one
            }
            string key = SpellKeys.Label(i);
            if (key != _shownKey[i])
            {
                _shownKey[i] = key;
                s.Key.Text(key);
            }

            string remaining = "";
            if (id == null)
            {
                States[i] = SlotState.Empty;
                s.SetCooldown(0);
                s.SetGlow(null);
                s.SetPlateDim(true);
            }
            else
            {
                double progress = pc.Spells.CooldownProgress(i);
                bool ready = progress >= 1;
                s.SetCooldown((float)(1 - progress));
                bool affordable = pc.Spells.IsActive(i) || energy >= pc.Spells.CostOf(id, pc.Stats).Total;
                if (!ready) remaining = Math.Ceiling((1 - progress) * pc.Spells.EffectiveCooldown(id, pc.Stats)).ToString("0");
                // COS-95: three states. Cooldown: the draining overlay and seconds. No energy: a cold tint. Silenced: dark, no glow.
                var state = silenced ? SlotState.Silenced : !ready ? SlotState.Cooldown : !affordable ? SlotState.Starved : SlotState.Ready;
                States[i] = state;
                s.SetPlateDim(silenced);
                s.SetIconColor(state switch { SlotState.Silenced => UiTheme.Silenced, SlotState.Starved => UiTheme.Starved, _ => "#FFFFFF" });
                s.SetGlow(silenced ? null : pc.Spells.IsActive(i) ? UiTheme.EmberGlow : state == SlotState.Ready ? UiTheme.RuneGlow : null);
            }
            if (remaining != _shownRemaining[i])
            {
                _shownRemaining[i] = remaining;
                s.Center.Text(remaining);
            }
        }

        double max = Math.Max(1, SpellCaster.MaxEnergy);
        double cur = Math.Clamp(energy, 0, max);
        HudKit.SetFill(_energyFill, (float)(cur / max));
        string text = $"{cur:0} / {max:0}";
        if (text != _energyShown)
        {
            _energyShown = text;
            _energyText.Text(text);
        }
    }
}
