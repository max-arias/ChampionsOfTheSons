using ChampionsOfTheSons.Core.Rules;
using ChampionsOfTheSons.Mod.Bridge;
using ChampionsOfTheSons.Mod.Icons;
using ChampionsOfTheSons.Mod.UI;
using SUI;
using TMPro;
using UnityEngine;
using static SUI.SUI;

namespace ChampionsOfTheSons.Mod.Hud;

/// <summary>
/// COS-89: a row of the player's active buffs and debuffs, bottom left: icon, amount (for stacking debuffs) and time left. Good ones
/// have a pale frame, bad ones a red one; a buff about to run out pulses. Hidden with the rest of the HUD while a panel is open.
/// </summary>
internal sealed class BuffHud
{
    private const int MaxShown = 12;
    private const float Size = 46, Gap = 6, Left = 24, Bottom = 240; // above the game's pickup messages

    private sealed class Cell
    {
        public SContainerOptions Root = null!;
        public SImageOptions Icon = null!;
        public SLabelOptions Time = null!, Amount = null!;
        public string Shown = "";
    }

    private readonly List<Cell> _cells = new();
    private SContainerOptions _row = null!;
    private float _pulse;

    /// <summary>Buff ids drawn last frame, in order (self-test).</summary>
    public IReadOnlyList<string> Shown => _cells.Where(c => c.Root.Root.activeSelf).Select(c => c.Shown).ToList();

    public void Build(SContainerOptions layer)
    {
        _row = SContainer.TopLeft(0, 0).Size(MaxShown * (Size + Gap), Size + 18);
        for (int i = 0; i < MaxShown; i++)
        {
            var cell = new Cell { Root = SContainer.TopLeft(i * (Size + Gap), 0).Size(Size, Size).Background(HudKit.C("#0A0D12C0"), EBackground.Round10) };
            cell.Icon = IconView.Create().Anchor(AnchorType.Fill).Margin(4);
            cell.Root.Add(cell.Icon);
            cell.Amount = HudKit.Label("", 12, UiTheme.Text, TextAlignmentOptions.TopRight).Anchor(AnchorType.Fill);
            cell.Amount.TextObject.margin = new Vector4(2, 1, 3, 2);
            cell.Amount.TextObject.fontStyle = FontStyles.Bold;
            cell.Root.Add(cell.Amount);
            cell.Time = HudKit.Label("", 12, UiTheme.Bone, TextAlignmentOptions.Center).TopLeft(-4, Size).Size(Size + 8, 16);
            cell.Root.Add(cell.Time);
            _row.Add(cell.Root);
            cell.Root.Active(false);
            _cells.Add(cell);
        }
        layer.Add(_row);
        HudKit.NoRaycast(_row.Root);
        HudKit.Place(_row.RectTransform, new Vector2(0, 0), new Vector2(0, 0), Left, Bottom);
    }

    public void Update(float dt)
    {
        _pulse += dt;
        var list = Entries().Take(MaxShown).ToList();
        for (int i = 0; i < _cells.Count; i++)
        {
            var cell = _cells[i];
            if (i >= list.Count)
            {
                if (cell.Root.Root.activeSelf) cell.Root.Active(false);
                continue;
            }
            var (id, icon, amount, left, positive) = list[i];
            if (!cell.Root.Root.activeSelf) cell.Root.Active(true);
            if (cell.Shown != id)
            {
                cell.Shown = id;
                IconView.Show(cell.Icon, icon);
            }
            // COS-213: a short buff that something keeps renewing (standing in a Healing Dome or Sun Flare) shows no countdown
            // and does not blink: its timer would jump between the same two values for as long as the player stays.
            _seen.TryGetValue(id, out var seen);
            if (left > seen.Left + 0.05 && left <= 2.5 && seen.Left > 0) seen.SteadyUntil = _pulse + 1.5f;
            seen.Left = left;
            _seen[id] = seen;
            bool steady = _pulse < seen.SteadyUntil;
            float blink = left < 3 && !steady ? 0.55f + 0.45f * Mathf.Abs(Mathf.Sin(_pulse * 5)) : 1f;
            var frame = HudKit.C(positive ? "#E8E2D0" : "#E05A4A");
            frame.a = blink;
            if (cell.Root.Root.GetComponent<UnityEngine.UI.Image>() is { } bg) bg.color = Color.Lerp(HudKit.C("#0A0D12C0"), frame, 0.35f);
            cell.Icon.ImageObject.color = new Color(1, 1, 1, blink);
            cell.Time.Text(steady ? "" : left >= 100 ? $"{left / 60:0}m" : left >= 10 ? $"{left:0}s" : $"{left:0.0}s");
            cell.Amount.Text(amount);
        }
    }

    /// <summary>Per shown buff: its time left at the last frame, and until when (on <see cref="_pulse"/>) it counts as kept renewed.</summary>
    private readonly Dictionary<string, (double Left, float SteadyUntil)> _seen = new();

    /// <summary>Spell and perk buffs (positive), then the buff system's entries, positive first.</summary>
    private static IEnumerable<(string Id, string? Icon, string Amount, double Left, bool Positive)> Entries()
    {
        foreach (var id in Spells.SpellWorld.Buffs.Active)
            yield return ("spell." + id, IconCache.GetTexture("spell." + id) != null ? "spell." + id : null, "", Spells.SpellWorld.Buffs.Remaining(id), true);
        var db = ModContent.Db.Buffs;
        foreach (var g in PlayerDebuffs.Buffs.Snapshot().GroupBy(b => b.BuffId).OrderByDescending(g => g.First().Positive))
        {
            db.TryGetValue(g.Key, out var def);
            double left = g.Max(b => b.Left);
            string amount = def?.Stack == BuffStack.Accumulate ? HudKit.Compact(g.Sum(b => b.Amount))
                : g.Count() > 1 ? "x" + g.Count() : "";
            yield return (g.Key, def?.Icon, amount, left, g.First().Positive);
        }
    }

    /// <summary>The buff's name in the player's language (tooltips, self-test).</summary>
    public static string NameOf(string buffId) =>
        UiText.T("ui.buffs." + buffId.Split('.').Last(), ModContent.Db.Buffs.TryGetValue(buffId, out var d) ? d.Name : buffId);
}
