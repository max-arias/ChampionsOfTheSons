using ChampionsOfTheSons.Core;
using ChampionsOfTheSons.Core.Content;
using ChampionsOfTheSons.Core.Rules;
using ChampionsOfTheSons.Mod.Icons;
using ChampionsOfTheSons.Mod.UI;
using SUI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static ChampionsOfTheSons.Mod.UI.PanelKit;
using static SUI.SUI;

namespace ChampionsOfTheSons.Mod.Perks;

public enum PerkState
{
    Bought,
    Available,
    NeedPoints,
    Locked,
}

/// <summary>
/// The perk window: one tab per perk category laid out by the perks' own x/y with unlock-path lines, plus a spell book tab.
/// A detail pane on the right follows the hovered node (or the clicked one) and holds the Buy button.
/// Respec consumes a Untainted Heart from the bag.
/// </summary>
internal sealed class PerkPanel
{
    public const string PanelId = "cots-perks";
    public const string HeartOfPurity = "item.heart_of_purity";
    public const string SpellTab = "spells";

    private const float BodyTop = 116, BodyH = WinH - BodyTop - 20;
    private const float TreeX = 20, TreeW = 1250;
    private const float DetailX = 1286, DetailW = 334;
    private const float TreePad = 36;

    private static readonly PerkCategory[] Categories =
    {
        PerkCategory.MeleeOffense, PerkCategory.RangedOffense, PerkCategory.MagicOffense,
        PerkCategory.Defense, PerkCategory.Support, PerkCategory.Utility,
    };

    private sealed class Node
    {
        public PerkDef Def = null!;
        public SBgButtonOptions Button = null!;
        public SImageOptions? Icon;
        public SLabelOptions Count = null!;
        public float X, Y, Size; // window coordinates, top-left origin
        public PerkState? Shown;
        public int ShownTimes = -1;
    }

    private sealed class Edge
    {
        public string From = "", To = "";
        public SContainerOptions Line = null!;
        public bool? Lit;
    }

    private sealed class SpellRow
    {
        public SpellDef Def = null!;
        public SContainerOptions Row = null!;
        public SImageOptions Icon = null!;
        public SLabelOptions Name = null!, Info = null!;
        public SBgButtonOptions Unlock = null!;
        public SBgButtonOptions[] Slots = new SBgButtonOptions[SpellBook.Slots];
        public float X, Y, W, H;
    }

    public static PerkPanel Instance { get; private set; } = null!;

    private SPanelOptions _root = null!;
    private SContainerOptions _window = null!;
    private SImageOptions _dIcon = null!;
    private SLabelOptions _points = null!;
    private SBgButtonOptions _respec = null!;
    private SLabelOptions _respecNote = null!;
    private readonly Dictionary<string, SContainerOptions> _tabs = new();
    private readonly Dictionary<string, SBgButtonOptions> _tabButtons = new();
    private readonly Dictionary<string, Node> _nodes = new();
    private readonly List<Edge> _edges = new();
    private readonly List<SpellRow> _spellRows = new();
    private readonly Dictionary<int, string> _hoverIds = new();

    private SLabelOptions _dName = null!, _dMeta = null!, _dFlavor = null!, _dEffect = null!, _dStatus = null!;
    private SBgButtonOptions _buy = null!;

    private string _tab = PerkCategory.MeleeOffense.ToString();
    private string? _selected, _hover, _simulatedHover;
    private bool _useSimulatedHover;
    private bool _dirty = true;
    private float _timer;
    private int _lastToggleFrame = -1;
    private string _lastMessage = "";

    public bool IsOpen => _root != null && _root.Root.activeSelf;
    public GameObject? PadRoot => _root?.Root;
    public string CurrentTab => _tab;
    /// <summary>The tab button of a category or the spell book (gamepad self-test).</summary>
    public GameObject? TabButton(string id) => _tabButtons.TryGetValue(id, out var b) ? b.Root : null;
    public string? Selected => _selected;
    public IReadOnlyCollection<string> NodeIds => _nodes.Keys;
    public string DetailName => _dName.TextObject.text;
    public string DetailEffect => _dEffect.TextObject.text;
    public string DetailFlavor => _dFlavor.TextObject.text;
    public string PointsText => _points.TextObject.text;

    private static PlayerCharacter Pc => PlayerState.Character;

    // ------------------------------------------------------------------ build

    public void Build()
    {
        Instance = this;
        _root = RegisterNewPanel(PanelId, enableInput: true);
        _root.Add(SContainer.Anchor(AnchorType.Fill).Background(C(UiTheme.Dim), EBackground.None));
        _window = SContainer.Anchor(AnchorType.MiddleCenter).Pivot(0.5f, 0.5f).Size(WinW, WinH)
            .Background(C(UiTheme.Window), EBackground.Round10);
        _root.Add(_window);

        _window.Add(Label(UiText.T("ui.perks.title", "Perks"), 30, UiTheme.Gold, TextAlignmentOptions.MidlineLeft).TopLeft(28, 14).Size(300, 44));
        _points = Label("", 22, UiTheme.Text, TextAlignmentOptions.MidlineLeft).TopLeft(330, 14).Size(520, 44);
        _window.Add(_points);
        _respecNote = Label("", 16, UiTheme.TextDim, TextAlignmentOptions.MidlineRight).TopLeft(860, 14).Size(380, 44);
        _window.Add(_respecNote);
        _respec = Button(UiText.T("ui.perks.respec", "Respec"), WinW - 64 - 16 - 150, 16, 150, 40, () => TryRespec(), 12, 18);
        _window.Add(_respec);
        _window.Add(SBgButton.Text("X").FontSize(24).FontColor(UiTheme.Text).Color(C("#2B333E"), C("#4A3036")).Notify(Close)
            .TopLeft(WinW - 64, 16).Size(40, 40));

        float tx = 20;
        foreach (var cat in Categories)
        {
            AddTab(cat.ToString(), CategoryName(cat), tx);
            tx += 152;
        }
        AddTab(SpellTab, UiText.T("ui.perks.tab.spells", "Spell book"), tx);

        foreach (var cat in Categories) BuildTree(cat);
        BuildSpellBook();
        BuildDetail();

        Pc.Stats.Changed += MarkDirty;
        Pc.Inventory.Changed += MarkDirty;
        _root.Active(false);
        ShowTab(_tab);
    }

    private static string CategoryName(PerkCategory c) => UiText.T("ui.perks.cat." + c.ToString().ToLowerInvariant(), c switch
    {
        PerkCategory.MeleeOffense => "Melee",
        PerkCategory.RangedOffense => "Ranged",
        PerkCategory.MagicOffense => "Magic",
        PerkCategory.Defense => "Defense",
        PerkCategory.Support => "Support",
        _ => "Utility",
    });

    private void AddTab(string id, string title, float x)
    {
        var btn = Button(title, x, 66, 146, 40, () => ShowTab(id), 12, 18);
        _tabButtons[id] = btn;
        _window.Add(btn);
    }

    private SContainerOptions NewArea(string id, float x, float w)
    {
        var area = SContainer.TopLeft(x, BodyTop).Size(w, BodyH).Background(C(UiTheme.Panel), EBackground.Round10);
        _window.Add(area);
        _tabs[id] = area;
        return area;
    }

    private void BuildTree(PerkCategory cat)
    {
        var area = NewArea(cat.ToString(), TreeX, TreeW);
        var perks = ModContent.Db.Perks.Values.Where(p => p.Category == cat).ToList();
        double minX = perks.Min(p => p.X), maxX = perks.Max(p => p.X), minY = perks.Min(p => p.Y), maxY = perks.Max(p => p.Y);
        double spanX = Math.Max(1, maxX - minX), spanY = Math.Max(1, maxY - minY);
        float unit = (float)Math.Min(Math.Min((TreeW - 2 * TreePad - 60) / spanX, (BodyH - 2 * TreePad - 60) / spanY), 96);
        float size = Mathf.Clamp(unit * 0.74f, 34, 66);
        float offX = (float)((TreeW - spanX * unit) / 2), offY = (float)((BodyH - spanY * unit) / 2);

        Vector2 Center(PerkDef p) => new(offX + (float)((p.X - minX) * unit), offY + (float)((p.Y - minY) * unit));

        var edges = new List<(PerkDef From, PerkDef To)>();
        foreach (var p in perks)
        {
            foreach (var parentId in p.UnlockPath.Concat(p.Requires).Distinct())
                if (ModContent.Db.Perks.TryGetValue(parentId, out var parent) && parent.Category == cat) edges.Add((parent, p));
        }
        foreach (var (from, to) in edges)
        {
            var a = Center(from);
            var b = Center(to);
            var line = Line(a.x, a.y, b.x, b.y, UiTheme.Divider, 3);
            area.Add(line);
            _edges.Add(new Edge { From = from.Id, To = to.Id, Line = line });
        }

        foreach (var p in perks)
        {
            var c = Center(p);
            var id = p.Id;
            var btn = Button(ShortName(p), c.x - size / 2, c.y - size / 2, size, size, () => Select(id), 7, 12);
            InnerPlate(btn);
            btn.TextObject.margin = new Vector4(2, 2, 2, 2);
            var icon = IconView.Create().Anchor(AnchorType.Fill).Margin(3f);
            btn.Add(icon);
            if (IconView.Show(icon, p.Id)) btn.Text("");
            else icon = null;
            var count = Label("", 12, UiTheme.Gold, TextAlignmentOptions.BottomRight).Anchor(AnchorType.Fill);
            count.TextObject.margin = new Vector4(2, 2, 4, 1);
            btn.Add(count);
            area.Add(btn);
            _hoverIds[btn.Root.GetInstanceID()] = id;
            _nodes[id] = new Node { Def = p, Button = btn, Icon = icon, Count = count, X = TreeX + c.x - size / 2, Y = BodyTop + c.y - size / 2, Size = size };
        }
        area.Active(false);
    }

    private static string ShortName(PerkDef p) => ModContent.Text.Get(p.Id + ".name", p.Name);

    private void BuildSpellBook()
    {
        var area = NewArea(SpellTab, TreeX, TreeW);
        var spells = ModContent.Db.Spells.Values.OrderBy(s => s.LevelReq).ThenBy(s => s.Name, StringComparer.Ordinal).ToList();
        const float colW = 600, rowH = 52, gap = 4;
        const int rows = 14;
        for (int i = 0; i < spells.Count; i++)
        {
            int col = i / rows, r = i % rows;
            float x = 16 + col * (colW + 14), y = 14 + r * (rowH + gap);
            var spell = spells[i];
            var row = new SpellRow { Def = spell, X = TreeX + x, Y = BodyTop + y, W = colW, H = rowH };
            row.Row = SContainer.TopLeft(x, y).Size(colW, rowH).Background(C(UiTheme.SlotInner), EBackground.Round10);
            area.Add(row.Row);
            row.Row.Root.GetComponent<Image>().raycastTarget = true;
            _hoverIds[row.Row.Root.GetInstanceID()] = spell.Id;
            row.Icon = IconView.Create().TopLeft(6, 6).Size(40, 40);
            IconView.Show(row.Icon, spell.Id);
            row.Row.Add(row.Icon);
            row.Name = Label("", 19, UiTheme.Text, TextAlignmentOptions.MidlineLeft).TopLeft(54, 3).Size(278, 26);
            row.Info = Label("", 14, UiTheme.TextDim, TextAlignmentOptions.MidlineLeft).TopLeft(54, 27).Size(278, 22);
            row.Row.Add(row.Name);
            row.Row.Add(row.Info);
            string spellId = spell.Id;
            row.Unlock = Button("", 338, 6, 250, 40, () => UnlockSpell(spellId), 10, 16);
            row.Row.Add(row.Unlock);
            for (int s = 0; s < SpellBook.Slots; s++)
            {
                int slot = s;
                row.Slots[s] = Button((s + 1).ToString(), 338 + s * 42, 6, 38, 40, () => SlotSpell(spellId, slot), 12, 20);
                row.Row.Add(row.Slots[s]);
            }
            _spellRows.Add(row);
        }
        area.Active(false);
    }

    private void BuildDetail()
    {
        var p = SContainer.TopLeft(DetailX, BodyTop).Size(DetailW, BodyH).Background(C(UiTheme.Panel), EBackground.Round10);
        _window.Add(p);
        _dIcon = IconView.Create().TopLeft(16, 14).Size(64, 64);
        _dName = Label("", 24, UiTheme.Gold, TextAlignmentOptions.TopLeft, wrap: true).TopLeft(16, 14).Size(DetailW - 32, 64);
        _dMeta = Label("", 15, UiTheme.TextDim, TextAlignmentOptions.TopLeft, wrap: true).TopLeft(16, 80).Size(DetailW - 32, 44);
        _dFlavor = Label("", 16, UiTheme.Description, TextAlignmentOptions.TopLeft, wrap: true).TopLeft(16, 130).Size(DetailW - 32, 90);
        _dEffect = Label("", 18, UiTheme.Text, TextAlignmentOptions.TopLeft, wrap: true).TopLeft(16, 226).Size(DetailW - 32, 260);
        _dStatus = Label("", 16, UiTheme.Bad, TextAlignmentOptions.BottomLeft, wrap: true).TopLeft(16, BodyH - 150).Size(DetailW - 32, 80);
        _buy = Button("", 16, BodyH - 62, DetailW - 32, 46, BuySelected, 12, 20);
        p.Add(_dIcon).Add(_dName).Add(_dMeta).Add(_dFlavor).Add(_dEffect).Add(_dStatus).Add(_buy);
    }

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
        _root.Active(true);
        _dirty = false;
        Refresh();
    }

    public void Close()
    {
        _hover = null;
        _root.Active(false);
    }

    public void ShowTab(string id)
    {
        _tab = id;
        foreach (var (key, area) in _tabs) area.Active(key == id);
        foreach (var (key, btn) in _tabButtons) btn.Color(C(key == id ? "#5C4A1E" : UiTheme.SlotFrameEmpty), C("#3C4756"));
        if (IsOpen) Refresh();
    }

    private void MarkDirty() => _dirty = true;

    public void Tick()
    {
        if (!IsOpen) return;
        _timer += Time.unscaledDeltaTime;
        if (_dirty || _timer > 0.5f)
        {
            _dirty = false;
            _timer = 0;
            Refresh();
        }
        string? hovered = _useSimulatedHover ? _simulatedHover : RaycastKey(_hoverIds, Pad.PadPointer.Position);
        if (hovered != _hover)
        {
            _hover = hovered;
            RefreshDetail();
        }
    }

    public void SimulateHover(string? id)
    {
        _useSimulatedHover = true;
        _simulatedHover = id;
        _hover = id;
        RefreshDetail();
    }

    public void ReleaseSimulatedHover()
    {
        _useSimulatedHover = false;
        _simulatedHover = null;
        _hover = null;
    }

    public string? RaycastHover(Vector2 screenPoint) => RaycastKey(_hoverIds, screenPoint);

    /// <summary>Screen point (pixels, bottom-left origin) at the center of a perk node, for hit-testing.</summary>
    public Vector2 ScreenPointOfPerk(string id)
    {
        var n = _nodes[id];
        return ScreenPointInWindow(n.X + n.Size / 2, n.Y + n.Size / 2);
    }

    public Vector2 ScreenPointOfSpellRow(string id)
    {
        var r = _spellRows.First(s => s.Def.Id == id);
        return ScreenPointInWindow(r.X + 20, r.Y + r.H / 2);
    }

    // ------------------------------------------------------------------ state

    public PerkState StateOf(string id)
    {
        var pc = Pc;
        if (pc.Perks.TimesBought(id) > 0) return PerkState.Bought;
        return StateFor(pc.Perks.CanBuy(id, pc.Progress));
    }

    private static PerkState StateFor(BuyResult r) => r switch
    {
        BuyResult.Ok => PerkState.Available,
        BuyResult.NotEnoughPoints => PerkState.NeedPoints,
        BuyResult.AlreadyBought => PerkState.Bought,
        _ => PerkState.Locked,
    };

    private static string StateColor(PerkState s) => s switch
    {
        PerkState.Bought => UiTheme.Gold,
        PerkState.Available => "#5BD75B",
        PerkState.NeedPoints => "#7A6A3A",
        _ => UiTheme.SlotFrameEmpty,
    };

    public void Refresh()
    {
        var pc = Pc;
        int avail = pc.Perks.AvailablePoints(pc.Progress);
        _points.Text($"{UiText.T("ui.perks.points", "Points")}: <color={(avail > 0 ? UiTheme.Good : UiTheme.TextDim)}><b>{avail}</b></color> / {pc.Progress.TotalPerkPoints}   " +
                     $"<color={UiTheme.TextDim}>{UiText.T("ui.sheet.level", "Level")} {pc.Progress.Level}</color>");

        int hearts = Count(pc.Inventory, HeartOfPurity);
        bool spent = pc.Perks.SpentPoints > 0;
        _respec.Color(C(hearts > 0 && spent ? "#4A3036" : "#232A33"), C("#6A3A42"));
        _respecNote.Text(hearts > 0
            ? $"{UiText.T("ui.perks.heart_have", "Untainted Heart")} ×{hearts}"
            : UiText.T("ui.perks.heart_need", "Respec needs a Untainted Heart"));

        if (_tab == SpellTab) RefreshSpells();
        else RefreshTree(_tab);
        RefreshDetail();
    }

    private void RefreshTree(string tab)
    {
        var pc = Pc;
        foreach (var node in _nodes.Values)
        {
            if (node.Def.Category.ToString() != tab) continue;
            int times = pc.Perks.TimesBought(node.Def.Id);
            var state = times > 0 ? PerkState.Bought : StateFor(pc.Perks.CanBuy(node.Def.Id, pc.Progress));
            if (node.Shown == state && node.ShownTimes == times) continue;
            node.Shown = state;
            node.ShownTimes = times;
            var frame = C(StateColor(state));
            node.Button.Color(frame, Color.Lerp(frame, Color.white, 0.35f));
            node.Button.TextObject.color = C(state == PerkState.Locked ? UiTheme.TextFaint : UiTheme.Text);
            if (node.Icon != null) node.Icon.ImageObject.color = state == PerkState.Locked ? new Color(0.5f, 0.5f, 0.5f, 1f) : Color.white;
            node.Count.Text(times > 1 || (times == 1 && node.Def.Stackable) ? "×" + times : "");
        }
        foreach (var e in _edges)
        {
            if (ModContent.Db.Perks[e.From].Category.ToString() != tab) continue;
            bool lit = pc.Perks.TimesBought(e.From) > 0;
            if (e.Lit == lit) continue;
            e.Lit = lit;
            e.Line.Root.GetComponent<Image>().color = C(lit ? "#B8923A" : UiTheme.Divider);
        }
    }

    private void RefreshSpells()
    {
        var pc = Pc;
        foreach (var r in _spellRows)
        {
            bool unlocked = pc.Perks.UnlockedSpells.Contains(r.Def.Id);
            bool levelOk = pc.Progress.Level >= r.Def.LevelReq;
            r.Name.Text(ModContent.Text.Get(r.Def.Id + ".name", r.Def.Name));
            r.Info.Text($"{UiText.T("ui.sheet.level", "Level")} {r.Def.LevelReq}  ·  {r.Def.EnergyCost:0} {UiText.T("ui.spells.energy", "energy")}  ·  {r.Def.Cooldown:0.#} s");
            r.Name.TextObject.color = C(unlocked ? UiTheme.Gold : levelOk ? UiTheme.Text : UiTheme.TextFaint);
            r.Unlock.Active(!unlocked);
            foreach (var s in r.Slots) s.Active(unlocked);
            if (!unlocked)
            {
                var res = pc.Perks.CanUnlockSpell(r.Def.Id, pc.Progress);
                r.Unlock.Text(res == BuyResult.Ok
                    ? string.Format(UiText.T("ui.spells.unlock", "Unlock ({0} points)"), PerkTree.SpellUnlockCost)
                    : res == BuyResult.LevelTooLow
                        ? string.Format(UiText.T("ui.spells.need_level", "Needs level {0}"), r.Def.LevelReq)
                        : UiText.T("ui.spells.need_points", "Not enough points"));
                var c = C(res == BuyResult.Ok ? "#2F6B3A" : UiTheme.SlotFrameEmpty);
                r.Unlock.Color(c, Color.Lerp(c, Color.white, 0.3f));
            }
            else
            {
                for (int s = 0; s < SpellBook.Slots; s++)
                {
                    bool here = pc.Spells.Loadout[s] == r.Def.Id;
                    r.Slots[s].Color(C(here ? "#8A6A1E" : UiTheme.SlotFrameEmpty), C("#3C4756"));
                }
            }
        }
    }

    private void RefreshDetail()
    {
        string? id = _hover ?? _selected;
        _buy.Active(false);
        bool hasIcon = IconView.Show(_dIcon, id);
        _dName.TopLeft(hasIcon ? 88 : 16, 14).Size(hasIcon ? DetailW - 104 : DetailW - 32, 64);
        if (id == null)
        {
            _dName.Text(UiText.T("ui.perks.hint", "Hover or click a perk"));
            _dMeta.Text(""); _dFlavor.Text(""); _dEffect.Text(""); _dStatus.Text("");
            return;
        }
        if (ModContent.Db.Spells.TryGetValue(id, out var spell))
        {
            _dName.Text(ModContent.Text.Get(id + ".name", spell.Name));
            _dMeta.Text($"{UiText.T("ui.sheet.level", "Level")} {spell.LevelReq}  ·  {spell.EnergyCost:0} {UiText.T("ui.spells.energy", "energy")}  ·  {spell.Cooldown:0.#} s");
            _dFlavor.Text("");
            _dEffect.Text(ModContent.Text.Get(id + ".description", ""));
            _dStatus.Text(_lastMessage);
            return;
        }
        if (!ModContent.Db.Perks.TryGetValue(id, out var perk)) return;
        var pc = Pc;
        int times = pc.Perks.TimesBought(id);
        var result = pc.Perks.CanBuy(id, pc.Progress);
        _dName.Text(ShortName(perk) + (times > 0 ? $" <color={UiTheme.Gold}>×{times}</color>" : ""));
        _dMeta.Text($"{CategoryName(perk.Category)}  ·  {UiText.T("ui.perks.cost", "Cost")} {perk.Cost}  ·  {UiText.T("ui.sheet.level", "Level")} {perk.LevelReq}" +
                    (perk.Stackable ? $"  ·  {UiText.T("ui.perks.stackable", "Stackable")}" : ""));
        _dFlavor.Text($"<i>{ModContent.Text.Get(id + ".flavor", "")}</i>");
        var effect = ModContent.Text.Get(id + ".effect", "");
        if (perk.Manual && perk.Effects.Count == 0)
            effect += $"\n\n<color={UiTheme.TextFaint}>{UiText.T("ui.perks.manual", "Effect applied by the game hooks; not active yet.")}</color>";
        _dEffect.Text(effect);

        string status = result switch
        {
            BuyResult.Ok => "",
            BuyResult.AlreadyBought => UiText.T("ui.perks.owned", "Already unlocked."),
            BuyResult.LevelTooLow => string.Format(UiText.T("ui.perks.need_level", "Needs level {0}."), perk.LevelReq),
            BuyResult.PathLocked => string.Format(UiText.T("ui.perks.need_path", "Unlock first: {0}"), NameList(perk.UnlockPath)),
            BuyResult.RequirementsMissing => string.Format(UiText.T("ui.perks.need_perks", "Requires: {0}"), NameList(perk.Requires.Where(r => pc.Perks.TimesBought(r) == 0))),
            BuyResult.NotEnoughPoints => UiText.T("ui.perks.need_points", "Not enough perk points."),
            _ => UiText.T("ui.perks.not_buyable", "Can't be bought right now."),
        };
        _dStatus.Text(status.Length > 0 ? $"<color={UiTheme.Bad}>{status}</color>" : _lastMessage);
        bool canBuy = result == BuyResult.Ok;
        _buy.Active(canBuy);
        _buy.Text(string.Format(UiText.T("ui.perks.buy", "Buy ({0} pt)"), perk.Cost));
        var c = C("#2F6B3A");
        _buy.Color(c, Color.Lerp(c, Color.white, 0.3f));
    }

    private static string NameList(IEnumerable<string> ids) => string.Join(", ", ids.Select(i => ModContent.Text.Get(i + ".name", i)));

    // ------------------------------------------------------------------ actions

    public void Select(string id)
    {
        _selected = id;
        _lastMessage = "";
        RefreshDetail();
    }

    private void BuySelected()
    {
        if (_selected != null) Buy(_selected);
    }

    public BuyResult Buy(string id)
    {
        var r = Pc.BuyPerk(id);
        _lastMessage = r == BuyResult.Ok ? $"<color={UiTheme.Good}>{UiText.T("ui.perks.bought", "Unlocked!")}</color>" : "";
        _selected = id;
        _dirty = false;
        Refresh();
        return r;
    }

    public BuyResult UnlockSpell(string spellId)
    {
        var r = Pc.Perks.UnlockSpell(spellId, Pc.Progress);
        _lastMessage = "";
        _dirty = false;
        Refresh();
        return r;
    }

    public bool SlotSpell(string spellId, int slot)
    {
        if (!Pc.Perks.UnlockedSpells.Contains(spellId)) return false;
        bool ok = Pc.Spells.Equip(slot, spellId);
        _dirty = false;
        Refresh();
        return ok;
    }

    /// <summary>Respec: consumes one Untainted Heart and refunds every perk and spell. False when there is no heart or nothing to refund.</summary>
    public bool TryRespec()
    {
        var pc = Pc;
        if (pc.Perks.SpentPoints == 0) return false;
        if (!ConsumeOne(pc.Inventory, HeartOfPurity))
        {
            _lastMessage = $"<color={UiTheme.Bad}>{UiText.T("ui.perks.heart_need", "Respec needs a Untainted Heart")}</color>";
            RefreshDetail();
            return false;
        }
        pc.Respec();
        _dirty = false;
        foreach (var n in _nodes.Values) { n.Shown = null; n.ShownTimes = -1; }
        _edges.ForEach(e => e.Lit = null);
        Refresh();
        return true;
    }
}
