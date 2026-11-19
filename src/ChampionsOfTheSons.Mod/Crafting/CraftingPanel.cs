using System.Text;
using ChampionsOfTheSons.Core;
using ChampionsOfTheSons.Core.Rules;
using ChampionsOfTheSons.Mod.UI;
using SUI;
using TMPro;
using UnityEngine;
using static ChampionsOfTheSons.Mod.UI.PanelKit;
using static SUI.SUI;

namespace ChampionsOfTheSons.Mod.Crafting;

/// <summary>
/// The crafting window: all five modes as buttons, the bag to pick from (first click = the item to craft, further clicks =
/// ingredients), a stat picker for the modes that need one, a preview and the Craft button.
/// </summary>
internal sealed class CraftingPanel
{
    public const string PanelId = "cots-crafting";

    private const float BodyTop = 72, BodyH = WinH - BodyTop - 20;
    private const float LeftX = 20, LeftW = 700;
    private const float RightX = 736, RightW = WinW - RightX - 20;
    private const float Cell = 52, CellGap = 4;
    private const int Cols = 12;
    private const int MaxStatRows = 10;

    public static readonly CraftMode[] Modes =
    {
        CraftMode.Reroll, CraftMode.Reforge, CraftMode.Polish, CraftMode.Empower, CraftMode.IndividualReroll,
    };

    private sealed class Cellw
    {
        public string Key = "";
        public SBgButtonOptions Button = null!;
        public SLabelOptions Level = null!;
        public ItemInstance? Shown;
        public string? ShownRole;
        public float X, Y;
    }

    public static CraftingPanel Instance { get; private set; } = null!;

    private readonly LootRoller _loot = new(ModContent.Db);
    private readonly Core.Rules.Crafting _crafting;

    private SPanelOptions _root = null!;
    private SContainerOptions _window = null!;
    private readonly Dictionary<CraftMode, SBgButtonOptions> _modeButtons = new();
    private readonly List<Cellw> _cells = new();
    private readonly Dictionary<int, string> _hoverIds = new();
    private SLabelOptions _targetLabel = null!, _modeDesc = null!, _ingredients = null!, _message = null!;
    private SLabelOptions _itemTitle = null!, _itemSub = null!, _itemBody = null!;
    private SBgButtonOptions _craft = null!, _clear = null!;
    private readonly List<SBgButtonOptions> _statRows = new();
    private SLabelOptions _statHeader = null!;

    private CraftMode _mode = CraftMode.Reroll;
    private string? _target;
    private readonly List<string> _picked = new();
    private int _statIndex = -1;
    private string? _hover, _simulatedHover;
    private bool _useSimulatedHover;
    private string _messageText = "";
    private bool _dirty = true;
    private int _lastToggleFrame = -1;

    /// <summary>Replaceable so self-tests are deterministic.</summary>
    public Rng Rng { get; set; } = Rng.FromTime();

    public bool IsOpen => _root != null && _root.Root.activeSelf;
    public CraftMode Mode => _mode;
    public string? Target => _target;
    public IReadOnlyList<string> Picked => _picked;
    public int StatIndex => _statIndex;
    public string MessageText => _messageText;
    public ItemInstance? LastResult { get; private set; }
    public int LastStatIndex { get; private set; }

    private static PlayerCharacter Pc => PlayerState.Character;

    public CraftingPanel() => _crafting = new Core.Rules.Crafting(ModContent.Db, _loot);

    // ------------------------------------------------------------------ build

    public void Build()
    {
        Instance = this;
        _root = RegisterNewPanel(PanelId, enableInput: true);
        _root.Add(SContainer.Anchor(AnchorType.Fill).Background(C(UiTheme.Dim), EBackground.None));
        _window = SContainer.Anchor(AnchorType.MiddleCenter).Pivot(0.5f, 0.5f).Size(WinW, WinH).Background(C(UiTheme.Window), EBackground.Round10);
        _root.Add(_window);

        _window.Add(Label(UiText.T("ui.crafting.title", "Crafting"), 30, UiTheme.Gold, TextAlignmentOptions.MidlineLeft).TopLeft(28, 14).Size(400, 44));
        _window.Add(Label(UiText.T("ui.crafting.hint", "Pick a mode, click the item to craft, then its ingredients."), 17, UiTheme.TextDim,
            TextAlignmentOptions.MidlineRight).TopLeft(WinW - 64 - 28 - 800, 14).Size(800, 44));
        _window.Add(SBgButton.Text("X").FontSize(24).FontColor(UiTheme.Text).Color(C("#2B333E"), C("#4A3036")).Notify(Close)
            .TopLeft(WinW - 64, 16).Size(40, 40));

        // left: mode buttons and the bag
        var left = SContainer.TopLeft(LeftX, BodyTop).Size(LeftW, BodyH).Background(C(UiTheme.Panel), EBackground.Round10);
        _window.Add(left);
        float bx = 16;
        foreach (var mode in Modes)
        {
            var m = mode;
            var btn = Button(ModeName(mode), bx, 14, 128, 44, () => SetMode(m), 11, 16);
            left.Add(btn);
            _modeButtons[mode] = btn;
            bx += 134;
        }
        left.Add(Label(UiText.T("ui.bag.title", "Bag"), 18, UiTheme.Gold, TextAlignmentOptions.MidlineLeft).UpperCase().TopLeft(18, 66).Size(300, 26));
        float gx = (LeftW - (Cols * Cell + (Cols - 1) * CellGap)) / 2;
        for (int i = 0; i < LootRoller.BagSlots; i++)
        {
            string key = Inventory.Bag(i);
            float x = gx + i % Cols * (Cell + CellGap), y = 98 + i / Cols * (Cell + CellGap);
            var btn = Button("", x, y, Cell, Cell, () => ClickBag(key), 7, 11);
            InnerPlate(btn);
            btn.TextObject.margin = new Vector4(2, 11, 2, 2);
            var level = Label("", 11, UiTheme.TextDim, TextAlignmentOptions.TopLeft).Anchor(AnchorType.Fill);
            level.TextObject.margin = new Vector4(4, 2, 2, 2);
            btn.Add(level);
            left.Add(btn);
            _hoverIds[btn.Root.GetInstanceID()] = key;
            _cells.Add(new Cellw { Key = key, Button = btn, Level = level, X = LeftX + x, Y = BodyTop + y });
        }

        // right: target, ingredients, stat picker, craft, message, item description
        var right = SContainer.TopLeft(RightX, BodyTop).Size(RightW, BodyH).Background(C(UiTheme.Panel), EBackground.Round10);
        _window.Add(right);
        _targetLabel = Label("", 22, UiTheme.Text, TextAlignmentOptions.MidlineLeft).TopLeft(18, 12).Size(RightW - 36, 34);
        _modeDesc = Label("", 16, UiTheme.Description, TextAlignmentOptions.TopLeft, wrap: true).TopLeft(18, 50).Size(RightW - 36, 56);
        _ingredients = Label("", 17, UiTheme.Text, TextAlignmentOptions.TopLeft, wrap: true).TopLeft(18, 110).Size(RightW - 36, 80);
        _statHeader = Label(UiText.T("ui.crafting.pick_stat", "Pick the stat:"), 16, UiTheme.Gold, TextAlignmentOptions.MidlineLeft).TopLeft(18, 194).Size(RightW - 36, 24);
        right.Add(_targetLabel).Add(_modeDesc).Add(_ingredients).Add(_statHeader);
        for (int i = 0; i < MaxStatRows; i++)
        {
            int idx = i;
            var row = Button("", 18 + i % 2 * ((RightW - 36) / 2 + 4), 220 + i / 2 * 34, (RightW - 36) / 2 - 4, 30, () => PickStat(idx), 10, 15);
            row.TextObject.alignment = TextAlignmentOptions.MidlineLeft;
            row.TextObject.margin = new Vector4(8, 0, 4, 0);
            right.Add(row);
            _statRows.Add(row);
        }
        _craft = Button(UiText.T("ui.crafting.craft", "Craft"), 18, 400, 220, 50, () => DoCraft(), 14, 22);
        _clear = Button(UiText.T("ui.crafting.clear", "Clear"), 250, 400, 140, 50, Clear, 12, 18);
        _message = Label("", 17, UiTheme.Text, TextAlignmentOptions.MidlineLeft, wrap: true).TopLeft(404, 396).Size(RightW - 422, 58);
        right.Add(_craft).Add(_clear).Add(_message);
        right.Add(SContainer.TopLeft(18, 466).Size(RightW - 36, 1).Background(C(UiTheme.Divider), EBackground.None));
        _itemTitle = Label("", 22, UiTheme.Text, TextAlignmentOptions.TopLeft, wrap: true).TopLeft(18, 476).Size(RightW - 36, 32);
        _itemSub = Label("", 16, UiTheme.TextDim, TextAlignmentOptions.TopLeft, wrap: true).TopLeft(18, 508).Size(RightW - 36, 26);
        _itemBody = Label("", 16, UiTheme.Text, TextAlignmentOptions.TopLeft, wrap: true).TopLeft(18, 538).Size(RightW - 36, BodyH - 548);
        right.Add(_itemTitle).Add(_itemSub).Add(_itemBody);

        Pc.Inventory.Changed += MarkDirty;
        _root.Active(false);
        SetMode(_mode);
    }

    public static string ModeName(CraftMode m) => UiText.T("ui.crafting.mode." + m.ToString().ToLowerInvariant(), m switch
    {
        CraftMode.Reroll => "Reroll",
        CraftMode.Reforge => "Reforge",
        CraftMode.Polish => "Polish",
        CraftMode.Empower => "Empower",
        _ => "Reroll one stat",
    });

    private static string ModeDescription(CraftMode m) => UiText.T("ui.crafting.desc." + m.ToString().ToLowerInvariant(), m switch
    {
        CraftMode.Reroll => "Rerolls every stat of the item at its own level. Needs 2 ingredients of the same rarity or better. They are consumed.",
        CraftMode.Reforge => "Turns the item into a different item of the same type and rarity, with new stats. Needs 3 ingredients of the same rarity or better.",
        CraftMode.Polish => "Rerolls the value of one stat (or clears one socket). Needs 1 ingredient of the same rarity or better.",
        CraftMode.Empower => "Raises the item to your level and rerolls its values. Needs 9 ingredients of the same rarity or better.",
        _ => "Replaces one stat with a different stat from the same pool. Needs 1 ingredient of the same rarity or better.",
    });

    private static bool NeedsStat(CraftMode m) => m is CraftMode.Polish or CraftMode.IndividualReroll;

    // ------------------------------------------------------------------ show / hide

    public void Toggle()
    {
        if (Time.frameCount == _lastToggleFrame) return;
        _lastToggleFrame = Time.frameCount;
        if (IsOpen) Close(); else Open();
    }

    public void Open()
    {
        _lastToggleFrame = Time.frameCount;
        CotsUi.Inventory?.Close();
        Perks.PerkPanel.Instance?.Close();
        _root.Active(true);
        _dirty = false;
        Refresh();
    }

    public void Close()
    {
        _hover = null;
        _root.Active(false);
    }

    private void MarkDirty() => _dirty = true;

    public void Tick()
    {
        if (!IsOpen) return;
        if (_dirty)
        {
            _dirty = false;
            Refresh();
        }
        string? hovered = _useSimulatedHover ? _simulatedHover : RaycastKey(_hoverIds, Input.mousePosition);
        if (hovered != _hover)
        {
            _hover = hovered;
            RefreshItemText();
        }
    }

    public void SimulateHover(string? key)
    {
        _useSimulatedHover = true;
        _simulatedHover = key;
        _hover = key;
        RefreshItemText();
    }

    public void ReleaseSimulatedHover()
    {
        _useSimulatedHover = false;
        _simulatedHover = null;
        _hover = null;
    }

    public string? RaycastHover(Vector2 screenPoint) => RaycastKey(_hoverIds, screenPoint);

    public Vector2 ScreenPointOfBag(string key)
    {
        var c = _cells.First(x => x.Key == key);
        return ScreenPointInWindow(c.X + Cell / 2, c.Y + Cell / 2);
    }

    // ------------------------------------------------------------------ actions

    public bool ModeButtonVisible(CraftMode m) => _modeButtons.TryGetValue(m, out var b) && b.Root.activeInHierarchy;

    public void SetMode(CraftMode m)
    {
        _mode = m;
        _picked.Clear();
        _statIndex = -1;
        _messageText = "";
        foreach (var (mode, btn) in _modeButtons) btn.Color(C(mode == m ? "#5C4A1E" : UiTheme.SlotFrameEmpty), C("#3C4756"));
        if (IsOpen) Refresh();
    }

    public void Clear()
    {
        _target = null;
        _picked.Clear();
        _statIndex = -1;
        _messageText = "";
        Refresh();
    }

    public void ClickBag(string key)
    {
        var inv = Pc.Inventory;
        var item = inv.Get(key);
        if (item == null) return;
        _messageText = "";
        if (_target == null)
        {
            if (!ModContent.Db.Items.TryGetValue(item.BaseId, out var def) || !LootRoller.IsEquippable(def))
            {
                _messageText = UiText.T("ui.crafting.not_equippable", "Only equipment can be crafted.");
            }
            else
            {
                _target = key;
                _statIndex = -1;
            }
        }
        else if (key == _target)
        {
            _target = null;
            _picked.Clear();
            _statIndex = -1;
        }
        else if (_picked.Remove(key))
        {
        }
        else if (_picked.Count >= Core.Rules.Crafting.IngredientCount(_mode))
        {
            _messageText = UiText.T("ui.crafting.enough", "Enough ingredients picked.");
        }
        else if (item.Rarity < inv.Get(_target)!.Rarity)
        {
            _messageText = UiText.T("ui.crafting.rarity_low", "Ingredients must be the same rarity or better.");
        }
        else
        {
            _picked.Add(key);
        }
        Refresh();
    }

    public void PickStat(int index)
    {
        _statIndex = index;
        Refresh();
    }

    /// <summary>Why Craft is unavailable right now, or null when it can run.</summary>
    public string? Blocker()
    {
        var inv = Pc.Inventory;
        if (_target == null || inv.Get(_target) == null) return UiText.T("ui.crafting.need_target", "Pick the item to craft.");
        int need = Core.Rules.Crafting.IngredientCount(_mode);
        if (_picked.Count != need) return string.Format(UiText.T("ui.crafting.need_ingredients", "Pick {0} ingredients ({1} so far)."), need, _picked.Count);
        if (NeedsStat(_mode) && _statIndex < 0) return UiText.T("ui.crafting.need_stat", "Pick a stat.");
        return null;
    }

    public CraftResult? DoCraft()
    {
        var blocker = Blocker();
        if (blocker != null)
        {
            _messageText = $"<color={UiTheme.Bad}>{blocker}</color>";
            RefreshMessage();
            return null;
        }
        var inv = Pc.Inventory;
        var target = inv.Get(_target!)!;
        var ingredients = _picked.Select(k => inv.Get(k)!).ToList();
        var result = _crafting.Craft(_mode, target, ingredients, Rng, _statIndex, Pc.Progress.Level);
        if (!result.Ok)
        {
            _messageText = $"<color={UiTheme.Bad}>{ErrorText(result.Error)}</color>";
            RefreshMessage();
            return result;
        }

        string targetKey = _target!;
        var consumed = new HashSet<string>(_picked);
        var slots = inv.Slots.Where(kv => !consumed.Contains(kv.Key)).Select(kv => kv.Key == targetKey ? new KeyValuePair<string, ItemInstance>(targetKey, result.Item!) : kv).ToList();
        inv.Restore(slots);
        LastResult = result.Item;
        LastStatIndex = _statIndex;
        _picked.Clear();
        _statIndex = -1;
        _messageText = $"<color={UiTheme.Good}>{UiText.T("ui.crafting.done", "Crafted!")}</color>";
        _dirty = false;
        Refresh();
        return result;
    }

    private static string ErrorText(CraftError e) => e switch
    {
        CraftError.WrongIngredientCount => UiText.T("ui.crafting.err.count", "Wrong number of ingredients."),
        CraftError.IngredientRarityTooLow => UiText.T("ui.crafting.rarity_low", "Ingredients must be the same rarity or better."),
        CraftError.NotEquippable => UiText.T("ui.crafting.not_equippable", "Only equipment can be crafted."),
        CraftError.BadStatIndex => UiText.T("ui.crafting.need_stat", "Pick a stat."),
        CraftError.NothingToReroll => UiText.T("ui.crafting.err.nothing", "This stat can't be rerolled."),
        CraftError.NoAlternativeBase => UiText.T("ui.crafting.err.no_alt", "There is no other item of this type and rarity."),
        _ => e.ToString(),
    };

    // ------------------------------------------------------------------ refresh

    public void Refresh()
    {
        var inv = Pc.Inventory;
        if (_target != null && inv.Get(_target) == null) { _target = null; _picked.Clear(); _statIndex = -1; }
        _picked.RemoveAll(k => inv.Get(k) == null);

        foreach (var c in _cells)
        {
            var item = inv.Get(c.Key);
            string role = c.Key == _target ? "target" : _picked.Contains(c.Key) ? "picked" : "";
            if (c.Shown != null && c.Shown.Equals(item) && c.ShownRole == role && c.ShownRole != null) continue;
            c.Shown = item;
            c.ShownRole = role;
            if (item == null)
            {
                c.Button.Color(C(UiTheme.SlotFrameEmpty), C(UiTheme.SlotFrameEmpty));
                c.Button.Text("");
                c.Level.Text("");
                continue;
            }
            var frame = role == "target" ? C(UiTheme.Gold) : role == "picked" ? C("#4D9DFF") : C(UiTheme.RarityHex(item.Rarity));
            c.Button.Color(frame, Color.Lerp(frame, Color.white, 0.4f));
            c.Button.Text(UiText.ItemName(item)).FontColor(role.Length > 0 ? UiTheme.Text : UiTheme.TextDim);
            bool leveled = ModContent.Db.Items.TryGetValue(item.BaseId, out var def) && !LootRoller.IsMaterialOrOther(def);
            c.Level.Text(leveled ? item.Level.ToString() : "");
        }

        var target = _target != null ? inv.Get(_target) : null;
        _targetLabel.Text(target == null
            ? $"<color={UiTheme.TextFaint}>{UiText.T("ui.crafting.no_target", "No item picked")}</color>"
            : $"{UiText.T("ui.crafting.target", "Item")}: <color={UiTheme.RarityHex(target.Rarity)}><b>{UiText.ItemName(target)}</b></color>  <color={UiTheme.TextDim}>{UiText.T("ui.item.level", "Level")} {target.Level}</color>");
        _modeDesc.Text(ModeDescription(_mode));

        int need = Core.Rules.Crafting.IngredientCount(_mode);
        var sb = new StringBuilder();
        sb.Append($"<color={UiTheme.Gold}>{UiText.T("ui.crafting.ingredients", "Ingredients")} {_picked.Count} / {need}</color>");
        foreach (var k in _picked)
        {
            var it = inv.Get(k);
            if (it != null) sb.Append("\n").Append($"<color={UiTheme.RarityHex(it.Rarity)}>{UiText.ItemName(it)}</color> <color={UiTheme.TextDim}>({UiText.T("ui.item.level", "Level")} {it.Level})</color>");
            if (sb.Length > 700) break;
        }
        _ingredients.Text(sb.ToString());

        bool statMode = NeedsStat(_mode) && target != null;
        _statHeader.Active(statMode);
        for (int i = 0; i < _statRows.Count; i++)
        {
            var row = _statRows[i];
            bool show = statMode && i < target!.Stats.Count;
            row.Active(show);
            if (!show) continue;
            var st = target!.Stats[i];
            bool socket = st.StatId.StartsWith("stat.socket", StringComparison.Ordinal) || st.StatId == LootRoller.EmptySocketStat;
            row.Text(socket ? UiText.StatName(st.StatId) : $"{UiText.StatName(st.StatId)}  {UiText.StatValue(st.StatId, st.Value)}");
            var c = C(i == _statIndex ? "#5C4A1E" : UiTheme.SlotFrameEmpty);
            row.Color(c, C("#3C4756"));
        }

        var blocker = Blocker();
        var cc = C(blocker == null ? "#2F6B3A" : "#232A33");
        _craft.Color(cc, Color.Lerp(cc, Color.white, blocker == null ? 0.3f : 0));
        RefreshMessage();
        RefreshItemText();
    }

    private void RefreshMessage() => _message.Text(_messageText.Length > 0 ? _messageText : $"<color={UiTheme.TextDim}>{Blocker() ?? ""}</color>");

    private void RefreshItemText()
    {
        var inv = Pc.Inventory;
        string? key = _hover != null && inv.Get(_hover) != null ? _hover : _target;
        var item = key != null ? inv.Get(key) : null;
        if (item == null)
        {
            _itemTitle.Text(""); _itemSub.Text(""); _itemBody.Text("");
            return;
        }
        var d = ItemDescriber.Describe(item, Pc, null);
        _itemTitle.Text(d.Title);
        _itemSub.Text(d.Subtitle);
        _itemBody.Text(d.Body);
    }
}
