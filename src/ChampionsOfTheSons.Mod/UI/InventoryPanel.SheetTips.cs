using ChampionsOfTheSons.Core.Rules;
using SUI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static SUI.SUI;

namespace ChampionsOfTheSons.Mod.UI;

/// <summary>Character sheet extras (COS-36): hover tooltips on the stat rows and the unspent perk points next to the level.</summary>
internal sealed partial class InventoryPanel
{
    public const string RowPrefix = "row.";

    private SLabelOptions? _perkLabel;
    private readonly Dictionary<string, (float Y, float H)> _rowGeometry = new();

    /// <summary>Id of the sheet row whose tooltip is showing, or null.</summary>
    public string? RowTipShown { get; private set; }

    public string RowTipText { get; private set; } = "";

    /// <summary>Unspent perk points as the sheet shows them (empty when none are shown).</summary>
    public string PerkPointsText => _perkLabel?.TextObject.text ?? "";

    private void UpdatePerkLabel(SheetSummary sum)
    {
        if (_perkLabel == null)
        {
            _perkLabel = SLabel.Text("").FontSize(15).FontColor(UiTheme.Good).Alignment(TextAlignmentOptions.MidlineLeft)
                .TopLeft(150, 8).Size(130, 34);
            _perkLabel.TextObject.raycastTarget = false;
            _perkLabel.TextObject.richText = true;
            _sheetPanel.Add(_perkLabel);
        }
        string text = sum.PerkPoints <= 0
            ? ""
            : $"{sum.PerkPoints} {UiText.T(sum.PerkPoints == 1 ? "ui.sheet.perk_point" : "ui.sheet.perk_points", sum.PerkPoints == 1 ? "perk point" : "perk points")}";
        if (_perkLabel.TextObject.text != text) _perkLabel.Text(text);
    }

    /// <summary>A near-invisible raycast target over the row, so the same UI hit-test that finds slots finds rows.</summary>
    private void AddRowHit(SheetRow row, float y, float h)
    {
        if (row.Header) return;
        var hit = SContainer.TopLeft(10, y).Size(SheetW - 20, h).Background(new Color(0, 0, 0, 0.01f), EBackground.None);
        _sheetPanel.Add(hit);
        _slotByObjectId[hit.Root.GetInstanceID()] = RowPrefix + row.Id;
        _rowGeometry[row.Id] = (y, h);
    }

    private void ShowRowTooltip(string key)
    {
        string id = key.Substring(RowPrefix.Length);
        SheetRow? row = null;
        foreach (var r in _sheetData)
            if (r.Id == id) { row = r; break; }
        if (row == null || row.Value.Tip.Length == 0 || !_rowGeometry.TryGetValue(id, out var g))
        {
            HideTooltip();
            return;
        }

        TooltipShown = null;
        TooltipSlot = null;
        RowTipShown = id;
        RowTipText = row.Value.Tip;

        _tip.Root.GetComponent<Image>().color = C(UiTheme.Gold);
        _tipTitle.RichText($"<color={UiTheme.Gold}>{row.Value.Label}</color>  <color={UiTheme.Text}>{row.Value.Value}</color>");
        _tipIcon.Active(false);
        _tipSub.RichText("");
        _tipSub.Active(false);
        _tipBody.RichText($"<color={UiTheme.Description}>{row.Value.Tip}</color>");
        _tipBody.Active(true);
        _tipCompare.Active(false);
        _tipDivider.Active(false);

        _tip.Active(true);
        LayoutRebuilder.ForceRebuildLayoutImmediate(_tip.RectTransform);
        LayoutRebuilder.ForceRebuildLayoutImmediate(_tip.RectTransform);

        var canvas = SUIViewport.GetComponent<RectTransform>().rect;
        float cw = canvas.width, ch = canvas.height;
        float winLeft = (cw - WinW) / 2, winTop = WinTop(ch);
        float x = winLeft + SheetX + SheetW + 10;
        float sy = winTop + BodyTop + g.Y;
        float h = _tip.RectTransform.rect.height;
        float y = Mathf.Max(8, Mathf.Min(sy, ch - 8 - h));
        _tip.RectTransform.anchoredPosition = new Vector2(x, -y);
        TooltipOnLeft = false;
        TooltipBounds = new Rect(x, y, _tip.RectTransform.rect.width, h);
    }

    /// <summary>Screen point (pixels, origin bottom-left) at the center of a sheet row, for hit-testing.</summary>
    public Vector2 ScreenPointOfRow(string rowId)
    {
        var g = _rowGeometry[rowId];
        var canvas = SUIViewport.GetComponent<RectTransform>().rect;
        float cx = (canvas.width - WinW) / 2 + SheetX + SheetW / 2;
        float cy = WinTop(canvas.height) + BodyTop + g.Y + g.H / 2;
        float scale = SUIViewport.scaleFactor;
        return new Vector2(cx * scale, (canvas.height - cy) * scale);
    }
}
