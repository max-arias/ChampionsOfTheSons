using ChampionsOfTheSons.Mod.Bridge;
using ChampionsOfTheSons.Mod.Hud;
using ChampionsOfTheSons.Mod.Spells;
using ChampionsOfTheSons.Mod.UI;
using SUI;
using static SUI.SUI;

namespace ChampionsOfTheSons.Mod.ActionBar;

/// <summary>
/// The action bar along the bottom edge (COS-151): the game's weapon hotkeys on the left, the six spells in the middle, the game's
/// consumable hotkeys on the right, on a stretched-hide backing with bone clasps between the groups. Display only: the keys keep
/// doing what they do (1-9 are the game's, Alt+1..6 the mod's); the pad gets <see cref="NextWeapon"/>, <see cref="NextConsumable"/>
/// and <see cref="UseConsumable"/>.
/// </summary>
internal sealed class ActionBar
{
    public const string PanelId = "cots-actionbar";
    /// <summary>Weapon bar keys (COS-192) and consumable keys.</summary>
    public const int WeaponSlots = 6, ConsumableSlots = 4;
    public const float ItemCell = 52, ItemGap = 5, ClaspW = 28, Pad = 36, PadY = 12;
    public const float WeaponGroupW = WeaponSlots * ItemCell + (WeaponSlots - 1) * ItemGap;
    public const float ConsumableGroupW = ConsumableSlots * ItemCell + (ConsumableSlots - 1) * ItemGap;
    /// <summary>Width of the groups and clasps; the XP strap below has the same width.</summary>
    public const float InnerWidth = WeaponGroupW + ConsumableGroupW + 2 * ClaspW + SpellBar.Width;
    public const float Width = InnerWidth + 2 * Pad;
    public const float Height = SpellBar.Height + 2 * PadY;
    /// <summary>Bottom edge above the screen bottom: just over the XP strap (<see cref="ProgressHud"/>).</summary>
    public const float Bottom = 34;

    public static ActionBar? Instance { get; private set; }

    private SPanelOptions _root = null!;
    private readonly SpellBar _spells = new();
    public readonly ItemGroup Weapons = new(weapons: true), Consumables = new(weapons: false);
    private float _itemTimer;
    private SLabelOptions _vitals = null!;
    private string _vitalsShown = "";

    public bool Visible => _root.Root.activeSelf;

    /// <summary>The health and shield line above the bar (COS-95; self-test).</summary>
    public string VitalsText => _vitalsShown;

    public void Build()
    {
        Instance = this;
        _root = RegisterNewPanel(PanelId, enableInput: false);
        var bar = SContainer.Anchor(AnchorType.BottomCenter).Pivot(0.5f, 0f).Size(Width, Height).Position(0, Bottom);
        _root.Add(bar);
        bar.Add(HudArt.Piece(HudArt.Backing, 0.7f, "#1A110CD8").Anchor(AnchorType.Fill));

        float x = Pad, itemY = (Height - ItemCell) / 2;
        float weaponsX = x;
        Weapons.Build(bar, x, itemY);
        x += WeaponGroupW;
        bar.Add(HudArt.Piece(HudArt.Clasp, 1, UiTheme.BoneDark).TopLeft(x + 4, 4).Size(ClaspW - 8, Height - 8));
        x += ClaspW;
        _spells.Build(bar, x, PadY, _root.Root);
        x += SpellBar.Width;
        bar.Add(HudArt.Piece(HudArt.Clasp, 1, UiTheme.BoneDark).TopLeft(x + 4, 4).Size(ClaspW - 8, Height - 8));
        x += ClaspW;
        Consumables.Build(bar, x, itemY);
        // COS-95: health and the absorb shield as numbers, a thin line over the spell group (the game's own gauges stay as they are).
        _vitals = HudKit.Label("", 13, UiTheme.Bone, TMPro.TextAlignmentOptions.Center).TopLeft(weaponsX + WeaponGroupW + ClaspW, -20).Size(SpellBar.Width, 18);
        _vitals.TextObject.fontStyle = TMPro.FontStyles.Bold;
        BarSlot.Outline(_vitals, 0.3f);
        bar.Add(_vitals);
        HudKit.NoRaycast(_root.Root);
        _root.Active(false);
    }

    public void Update(float dt, bool show)
    {
        if (_root.Root.activeSelf != show) _root.Active(show);
        if (!show) return;
        _spells.Refresh();
        // The game's hotkeys change rarely; 10 reads a second keep the bar current without per-frame inventory queries.
        _itemTimer -= dt;
        if (_itemTimer > 0) return;
        _itemTimer = 0.1f;
        RefreshVitals();
        Weapons.Refresh();
        Consumables.Refresh();
    }

    private void RefreshVitals()
    {
        var health = TheForest.Utils.LocalPlayer.Vitals?.Health;
        if (health == null) return;
        double shield = SpellWorld.Shield.Amount + Followups.FollowupsFeature.Shield.Amount;
        string text = VitalsLine(health._currentValue, health.GetMax(), shield);
        if (text == _vitalsShown) return;
        _vitalsShown = text;
        _vitals.Text(text);
    }

    public static string VitalsLine(double health, double max, double shield)
    {
        string line = $"<color={UiTheme.HealthText}>{UiText.T("ui.hud.health", "Health")} {Math.Max(0, health):0} / {max:0}</color>";
        if (shield >= 1) line += $"   <color={UiTheme.ShieldText}>{UiText.T("ui.hud.shield", "Shield")} {shield:0}</color>";
        return line;
    }

    /// <summary>Forces the item groups to re-read the game's hotkeys on the next update (tests, pad actions).</summary>
    public void Dirty() => _itemTimer = 0;

    /// <summary>Pad: equips the next weapon on the weapon group after the one in hand.</summary>
    public void NextWeapon()
    {
        int index = Weapons.NextFilled(Weapons.HeldPosition());
        if (index >= 0 && !Mod.Weapons.WeaponBarKeys.Press(index)) HotkeyBridge.Trigger(index);
        Dirty();
    }

    /// <summary>Pad: moves the consumable pick to the next filled slot.</summary>
    public void NextConsumable()
    {
        Consumables.Pick(Consumables.NextFilledPosition(Consumables.Picked));
        Dirty();
    }

    /// <summary>Pad: uses the picked consumable (what pressing its number does).</summary>
    public void UseConsumable()
    {
        int index = Consumables.PickedIndex();
        if (index >= 0) HotkeyBridge.Trigger(index);
        Dirty();
    }
}

/// <summary>
/// The keys named in the config (<see cref="ActionBarConfig"/>). Consumables mirror the game's item hotkeys; the weapon group is the
/// weapon bar (COS-192): a key with a bound mod weapon shows that weapon, any other key the game's hotkey.
/// </summary>
internal sealed class ItemGroup
{
    private readonly bool _weapons;
    private readonly BarSlot[] _slots;
    private readonly HotkeyBridge.Slot[] _shown;
    private readonly int[] _shownIndex;
    private readonly string?[] _boundShown;

    public ItemGroup(bool weapons)
    {
        _weapons = weapons;
        int n = weapons ? ActionBar.WeaponSlots : ActionBar.ConsumableSlots;
        _slots = new BarSlot[n];
        _shown = new HotkeyBridge.Slot[n];
        _shownIndex = new int[n];
        _boundShown = new string?[n];
    }

    /// <summary>The weapon bar item shown on position <paramref name="pos"/>'s cell (null: none, the game's hotkey shows).</summary>
    public string? BoundShown(int pos) => _boundShown[pos];

    /// <summary>The slot position the pad picked (consumables); -1 none.</summary>
    public int Picked { get; private set; } = -1;

    public BarSlot Slot(int pos) => _slots[pos];
    public HotkeyBridge.Slot Shown(int pos) => _shown[pos];

    /// <summary>The game hotkey index on position <paramref name="pos"/> (-1: unused).</summary>
    public int IndexAt(int pos)
    {
        var keys = _weapons ? ActionBarConfig.WeaponHotkeys : ActionBarConfig.ConsumableHotkeys;
        return pos < keys.Count ? keys[pos] : -1;
    }

    public void Build(SContainerOptions parent, float x, float y)
    {
        for (int i = 0; i < _slots.Length; i++)
        {
            _slots[i] = new BarSlot(parent, x + i * (ActionBar.ItemCell + ActionBar.ItemGap), y, ActionBar.ItemCell, 22);
            _shownIndex[i] = -2;
        }
    }

    public void Refresh()
    {
        for (int i = 0; i < _slots.Length; i++)
        {
            var s = _slots[i];
            int index = IndexAt(i);
            if (_weapons && RefreshBound(i, s, index)) continue;
            var now = index >= 0 ? HotkeyBridge.At(index) : default;
            if (index != _shownIndex[i])
            {
                _shownIndex[i] = index;
                s.Key.Text(index >= 0 ? HotkeyBridge.KeyLabel(index) : "");
                s.Plate.Active(index >= 0);
                _shown[i] = new HotkeyBridge.Slot(-1, -1, false); // force the item part below
            }
            if (now.ItemId != _shown[i].ItemId)
            {
                bool icon = !now.Empty && s.ShowTexture(HotkeyBridge.Icon(now.ItemId));
                if (now.Empty) s.ShowTexture(null);
                s.Name.Text(now.Empty ? "" : HotkeyBridge.Name(now.ItemId));
                s.Name.Active(!now.Empty && !icon);
                s.SetPlateDim(now.Empty);
            }
            if (now.Amount != _shown[i].Amount || now.ItemId != _shown[i].ItemId)
            {
                s.Count.Text(!now.Empty && now.Amount > 1 ? now.Amount.ToString() : "");
                s.SetIconColor(now.Empty || now.Amount > 0 ? "#FFFFFF" : "#FFFFFF59"); // none left: faded
            }
            _shown[i] = now;
            s.SetGlow(now.Empty ? null : now.Held ? UiTheme.EmberGlow : !_weapons && i == Picked ? UiTheme.PickGlow : null);
        }
        if (!_weapons && (Picked < 0 || _shown[Picked].Empty)) Picked = NextFilledPosition(Picked);
    }

    /// <summary>A weapon bar key with a bound mod weapon: shows the rolled item (icon, name), lit while it is in hand. False when
    /// the key has none (the game's hotkey shows).</summary>
    private bool RefreshBound(int i, BarSlot s, int index)
    {
        var bound = index >= 0 ? Weapons.WeaponBarKeys.Bound(index) : null;
        if (bound == null)
        {
            if (_boundShown[i] != null)
            {
                _boundShown[i] = null;
                _shown[i] = new HotkeyBridge.Slot(-1, -1, false); // the game's hotkey part redraws
                _shownIndex[i] = -2;
            }
            return false;
        }
        var (slot, item, held) = bound.Value;
        if (index != _shownIndex[i])
        {
            _shownIndex[i] = index;
            s.Key.Text(HotkeyBridge.KeyLabel(index));
            s.Plate.Active(true);
        }
        string id = $"{slot}|{item.BaseId}|{item.Level}|{(int)item.Rarity}";
        if (id != _boundShown[i])
        {
            _boundShown[i] = id;
            bool icon = s.ShowContentIcon(item.BaseId);
            s.Name.Text(UiText.ItemName(item));
            s.Name.Active(!icon);
            s.SetPlateDim(false);
            s.Count.Text("");
            s.SetIconColor("#FFFFFF");
        }
        _shown[i] = new HotkeyBridge.Slot(held ? Weapons.RealWeapons.ByType(Weapons.WeaponModels.Active?.Id)?.Id ?? 0 : 0, 1, held);
        s.SetGlow(held ? UiTheme.EmberGlow : null);
        return true;
    }

    /// <summary>Position of the held item in this group (-1 none).</summary>
    public int HeldPosition()
    {
        for (int i = 0; i < _slots.Length; i++)
        {
            int index = IndexAt(i);
            if (index >= 0 && (_weapons && Weapons.WeaponBarKeys.Bound(index) is { } b ? b.Held : HotkeyBridge.At(index).Held)) return i;
        }
        return -1;
    }

    /// <summary>The next position after <paramref name="from"/> (wrapping) whose hotkey holds an item the player has; -1 none.</summary>
    public int NextFilledPosition(int from)
    {
        for (int step = 1; step <= _slots.Length; step++)
        {
            int pos = ((from < 0 ? -1 : from) + step + _slots.Length) % _slots.Length;
            int index = IndexAt(pos);
            if (index < 0) continue;
            if (_weapons && Weapons.WeaponBarKeys.Bound(index) != null && pos != from) return pos;
            var slot = HotkeyBridge.At(index);
            if (!slot.Empty && slot.Amount > 0 && pos != from) return pos;
        }
        return -1;
    }

    /// <summary>The hotkey index of the next filled position after <paramref name="fromPos"/>; -1 none.</summary>
    public int NextFilled(int fromPos)
    {
        int pos = NextFilledPosition(fromPos);
        return pos < 0 ? -1 : IndexAt(pos);
    }

    public void Pick(int pos) => Picked = pos;

    public int PickedIndex() => Picked < 0 ? -1 : IndexAt(Picked);
}
