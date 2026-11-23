using ChampionsOfTheSons.Core.Content;
using ChampionsOfTheSons.Core.Rules;
using ChampionsOfTheSons.Mod.Perks;
using ChampionsOfTheSons.Mod.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ChampionsOfTheSons.Mod.Book;

/// <summary>
/// The book's Perks page (COS-168): six tree chips, one tree drawn from the perks' x/y (COS-169 shapes), and free refunds of the
/// tree or of everything (COS-170). Click a perk to see it on the card; click it again (or right-click) to buy it.
/// </summary>
internal static class BookPerksPage
{
    private const float Left = 62, Right = 16, Header = 80;
    private const float ChipY = Header + 6, ChipH = 40, ChipGap = 4;
    private const float AreaX = Left, AreaY = ChipY + ChipH + 12, AreaW = PageKit.W - Left - Right;
    private const float FootY = PageKit.H - 92 - 50, FootH = 44, AreaH = FootY - 12 - AreaY;
    private const float RefundTreeX = Left, RefundTreeW = 190, RefundAllX = Left + RefundTreeW + 8, RefundAllW = 170;
    private const float ArmSeconds = 4;

    private sealed class Node
    {
        public PerkDef Def = null!;
        public float X, Y, Size;
        public Image Bg = null!, Icon = null!;
        public Image[] Frame = null!, Picked = null!;
        public TextMeshProUGUI Label = null!, Count = null!;
    }

    private sealed class Tree
    {
        public RectTransform Root = null!;
        public readonly List<Node> Nodes = new();
        public readonly List<(string From, Image Line)> Edges = new();
    }

    private static RectTransform _root = null!;
    private static TextMeshProUGUI _title = null!, _points = null!, _refundTreeText = null!, _refundAllText = null!;
    private static Image _refundTreeBg = null!, _refundAllBg = null!;
    private static readonly List<(Image Bg, TextMeshProUGUI Text)> Chips = new();
    private static readonly Dictionary<PerkCategory, Tree> Trees = new();
    private static string? _armed;
    private static float _armedAt;

    public static PerkCategory Current { get; private set; } = PerkCategory.MeleeOffense;

    /// <summary>The perk clicked last (shown with a red frame; a second click buys it).</summary>
    public static string? Selected { get; private set; }

    private static PlayerCharacter Pc => PlayerState.Character;

    public static RectTransform Build(RectTransform page)
    {
        _root = PageKit.Fill(page, "perks");
        PageKit.Box(_root, "titlebar", 0, 0, PageKit.W, Header - 14, PageKit.TitleBar);
        _title = PageKit.Text(_root, "title", Left, 12, 300, Header - 26, UiText.T("ui.book.tab.perks", "Perks").ToUpperInvariant(), 26, PageKit.TitleText);
        _points = PageKit.Text(_root, "points", Left + 260, 12, PageKit.W - Left - Right - 260, Header - 26, "", 22, PageKit.TitleText, TextAlignmentOptions.MidlineRight);
        float chipW = (AreaW - ChipGap * (PerkInfo.Trees.Length - 1)) / PerkInfo.Trees.Length;
        for (int i = 0; i < PerkInfo.Trees.Length; i++)
        {
            float x = Left + i * (chipW + ChipGap);
            Chips.Add((PageKit.Box(_root, "tree-chip-" + i, x, ChipY, chipW, ChipH, PageKit.PaperShade),
                PageKit.Text(_root, "tree-chip-text-" + i, x, ChipY, chipW, ChipH, PerkInfo.CategoryName(PerkInfo.Trees[i]), 18, PageKit.Ink, TextAlignmentOptions.Center)));
        }
        foreach (var cat in PerkInfo.Trees) Trees[cat] = BuildTree(cat);
        _refundTreeBg = PageKit.Box(_root, "refund-tree-bg", RefundTreeX, FootY, RefundTreeW, FootH, PageKit.TitleBar);
        _refundTreeText = PageKit.Text(_root, "refund-tree", RefundTreeX, FootY, RefundTreeW, FootH, "", 20, PageKit.TitleText, TextAlignmentOptions.Center);
        _refundAllBg = PageKit.Box(_root, "refund-all-bg", RefundAllX, FootY, RefundAllW, FootH, PageKit.TitleBar);
        _refundAllText = PageKit.Text(_root, "refund-all", RefundAllX, FootY, RefundAllW, FootH, "", 20, PageKit.TitleText, TextAlignmentOptions.Center);
        PageKit.Text(_root, "free-note", RefundAllX + RefundAllW + 10, FootY, PageKit.W - Right - RefundAllX - RefundAllW - 10, FootH,
            UiText.T("ui.book.perks.free", "Refunds are free"), 17, PageKit.InkFaint, TextAlignmentOptions.MidlineRight);
        return _root;
    }

    private static Tree BuildTree(PerkCategory cat)
    {
        var tree = new Tree { Root = PageKit.Fill(_root, "tree-" + cat) };
        var perks = ModContent.Db.Perks.Values.Where(p => p.Category == cat).ToList();
        if (perks.Count == 0) return tree;
        double minX = perks.Min(p => p.X), maxX = perks.Max(p => p.X), minY = perks.Min(p => p.Y), maxY = perks.Max(p => p.Y);
        double spanX = Math.Max(1, maxX - minX), spanY = Math.Max(1, maxY - minY);
        const float pad = 34;
        float unit = (float)Math.Min(Math.Min((AreaW - 2 * pad) / spanX, (AreaH - 2 * pad) / spanY), 80);
        float size = Mathf.Clamp(unit * 0.82f, 30, 58);
        float offX = AreaX + (float)((AreaW - spanX * unit) / 2), offY = AreaY + (float)((AreaH - spanY * unit) / 2);
        Vector2 Center(PerkDef p) => new(offX + (float)((p.X - minX) * unit), offY + (float)((p.Y - minY) * unit));

        foreach (var p in perks)
            foreach (var parentId in p.UnlockPath.Concat(p.Requires).Distinct())
                if (ModContent.Db.Perks.TryGetValue(parentId, out var parent) && parent.Category == cat)
                    tree.Edges.Add((parent.Id, Line(tree.Root, Center(parent), Center(p))));
        foreach (var p in perks)
        {
            var c = Center(p);
            float x = c.x - size / 2, y = c.y - size / 2;
            var n = new Node { Def = p, X = x, Y = y, Size = size };
            n.Bg = PageKit.Box(tree.Root, "node-" + p.Id, x, y, size, size, PageKit.PaperShade);
            n.Icon = PageKit.Box(tree.Root, "node-icon-" + p.Id, x + 4, y + 4, size - 8, size - 8, Color.white);
            n.Icon.preserveAspect = true;
            var sprite = Icons.IconCache.GetSprite(p.Id);
            n.Icon.sprite = sprite;
            n.Icon.enabled = sprite != null;
            n.Label = PageKit.Text(tree.Root, "node-label-" + p.Id, x + 2, y + 2, size - 4, size - 4, sprite == null ? PerkInfo.Name(p.Id, p.Name) : "", 10,
                PageKit.Ink, TextAlignmentOptions.Center, wrap: true);
            n.Frame = PageKit.Frame(tree.Root, x, y, size, size, PageKit.InkFaint, 3);
            n.Picked = PageKit.Frame(tree.Root, x - 4, y - 4, size + 8, size + 8, PageKit.Select, 3);
            foreach (var f in n.Picked) f.enabled = false;
            n.Count = PageKit.Text(tree.Root, "node-count-" + p.Id, x, y + size - 18, size - 3, 18, "", 14, PageKit.Ink, TextAlignmentOptions.BottomRight);
            tree.Nodes.Add(n);
        }
        tree.Root.gameObject.SetActive(false);
        return tree;
    }

    /// <summary>A thin rotated bar from a to b (page pixels).</summary>
    private static Image Line(Transform parent, Vector2 a, Vector2 b)
    {
        var img = PageKit.Box(parent, "edge", 0, 0, Vector2.Distance(a, b), 3, PageKit.InkFaint);
        var rt = img.rectTransform;
        rt.pivot = new Vector2(0.5f, 0.5f);
        var mid = (a + b) / 2;
        rt.anchoredPosition = new Vector2(mid.x, -mid.y);
        rt.localRotation = Quaternion.Euler(0, 0, -Mathf.Atan2(b.y - a.y, b.x - a.x) * Mathf.Rad2Deg);
        return img;
    }

    private static Color StateColor(PerkState s) => s switch
    {
        PerkState.Bought => PageKit.Hex("#B8923A"),
        PerkState.Available => PageKit.Good,
        PerkState.NeedPoints => PageKit.Hex("#8A7A4A"),
        _ => PageKit.InkFaint,
    };

    public static void Refresh(List<PageHit> hits)
    {
        if (_armed != null && Time.unscaledTime - _armedAt > ArmSeconds) _armed = null;
        int avail = Pc.Perks.AvailablePoints(Pc.Progress);
        _points.text = $"{UiText.T("ui.perks.points", "Points")}: <b>{avail}</b> / {Pc.Progress.TotalPerkPoints}";
        float chipW = (AreaW - ChipGap * (PerkInfo.Trees.Length - 1)) / PerkInfo.Trees.Length;
        for (int i = 0; i < PerkInfo.Trees.Length; i++)
        {
            bool on = PerkInfo.Trees[i] == Current;
            Chips[i].Bg.color = on ? PageKit.TitleBar : PageKit.PaperShade;
            Chips[i].Text.color = on ? PageKit.TitleText : PageKit.Ink;
            hits.Add(new PageHit("tree." + i, Left + i * (chipW + ChipGap), ChipY, chipW, ChipH));
        }
        foreach (var (cat, tree) in Trees) tree.Root.gameObject.SetActive(cat == Current);
        var t = Trees[Current];
        foreach (var n in t.Nodes)
        {
            int times = Pc.Perks.TimesBought(n.Def.Id);
            var state = PerkInfo.StateOf(n.Def.Id);
            var frame = StateColor(state);
            foreach (var f in n.Frame) f.color = frame;
            foreach (var f in n.Picked) f.enabled = Selected == n.Def.Id;
            n.Bg.color = state == PerkState.Bought ? PageKit.Paper : PageKit.PaperShade;
            n.Icon.color = state == PerkState.Locked ? new Color(0.45f, 0.45f, 0.45f, 0.8f) : Color.white;
            n.Count.text = times > 1 || (times == 1 && n.Def.Stackable) ? "×" + times : "";
            hits.Add(new PageHit("perk." + n.Def.Id, n.X, n.Y, n.Size, n.Size));
        }
        foreach (var (from, line) in t.Edges) line.color = Pc.Perks.TimesBought(from) > 0 ? PageKit.Hex("#B8923A") : PageKit.InkFaint;

        _refundTreeText.text = _armed == "tree" ? UiText.T("ui.book.perks.confirm", "Click again to refund")
            : string.Format(UiText.T("ui.book.perks.refund_tree", "Refund {0}"), PerkInfo.CategoryName(Current));
        _refundAllText.text = _armed == "all" ? UiText.T("ui.book.perks.confirm", "Click again to refund") : UiText.T("ui.book.perks.refund_all", "Refund all perks");
        _refundTreeBg.color = _armed == "tree" ? PageKit.Select : PageKit.TitleBar;
        _refundAllBg.color = _armed == "all" ? PageKit.Select : PageKit.TitleBar;
        hits.Add(new PageHit("perks.refund_tree", RefundTreeX, FootY, RefundTreeW, FootH));
        hits.Add(new PageHit("perks.refund_all", RefundAllX, FootY, RefundAllW, FootH));
    }

    /// <summary>A click on this page; null when the key is not ours.</summary>
    public static string? Click(string key, bool right)
    {
        if (key.StartsWith("tree.", StringComparison.Ordinal) && int.TryParse(key.AsSpan(5), out int i) && i >= 0 && i < PerkInfo.Trees.Length)
        {
            Current = PerkInfo.Trees[i];
            Selected = null;
            _armed = null;
            return "tree";
        }
        if (key.StartsWith("perk.", StringComparison.Ordinal))
        {
            string id = key;
            if (right || Selected == id)
            {
                var r = Pc.BuyPerk(id);
                Selected = id;
                if (r == BuyResult.Ok) Audio.ModAudio.Play(Audio.SoundCues.PerkBought);
                return r == BuyResult.Ok ? "bought" : "refused " + r;
            }
            Selected = id;
            return "select";
        }
        switch (key)
        {
            case "perks.refund_tree": return Arm("tree", () => Pc.RespecTree(Current));
            case "perks.refund_all":
                return Arm("all", () =>
                {
                    int n = Pc.Perks.Bought.Count;
                    foreach (var cat in PerkInfo.Trees) Pc.RespecTree(cat);
                    return n;
                });
        }
        return null;
    }

    /// <summary>First click arms a refund; a second one within <see cref="ArmSeconds"/> runs it. Refunds are free (COS-170).</summary>
    private static string Arm(string which, Func<int> run)
    {
        if (_armed != which || Time.unscaledTime - _armedAt > ArmSeconds)
        {
            _armed = which;
            _armedAt = Time.unscaledTime;
            return "armed";
        }
        _armed = null;
        int n = run();
        Selected = null;
        if (n > 0) Audio.ModAudio.Play(Audio.SoundCues.Craft);
        return "refunded " + n;
    }

    /// <summary>Self-tests: picks a tree and clears the selection.</summary>
    public static void ShowTree(PerkCategory cat)
    {
        Current = cat;
        Selected = null;
        _armed = null;
    }
}
