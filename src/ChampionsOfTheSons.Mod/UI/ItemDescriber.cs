using System.Text;
using ChampionsOfTheSons.Core;
using ChampionsOfTheSons.Core.Content;
using ChampionsOfTheSons.Core.Rules;

namespace ChampionsOfTheSons.Mod.UI;

/// <summary>Everything a tooltip shows for one item, as TextMeshPro rich text. No Unity types here.</summary>
internal sealed class ItemDescription
{
    public Rarity Rarity { get; init; }

    /// <summary>Item name in its rarity color.</summary>
    public string Title { get; init; } = "";

    /// <summary>Rarity, type and level.</summary>
    public string Subtitle { get; init; } = "";

    /// <summary>Rolled stats, unique effect, set, description and lore.</summary>
    public string Body { get; init; } = "";

    /// <summary>Stat deltas against the item in the slot it would go to. Null when there is nothing to compare.</summary>
    public string? Compare { get; init; }

    public string RarityHex => UiTheme.RarityHex(Rarity);
}

internal static class ItemDescriber
{
    /// <summary>
    /// Describes <paramref name="item"/> for the tooltip. <paramref name="slot"/> is where it sits ("bag.3", "equip.ring1");
    /// bag items get a compare block against what their equipment slot holds now.
    /// </summary>
    public static ItemDescription Describe(ItemInstance item, PlayerCharacter pc, string? slot)
    {
        var db = pc.Content;
        db.Items.TryGetValue(item.BaseId, out var def);
        string rarityHex = UiTheme.RarityHex(item.Rarity);

        var rest = new StringBuilder();
        if (def != null)
        {
            rest.Append(" · ").Append(UiText.TypeName(def.Type));
            if (!LootRoller.IsMaterialOrOther(def))
                rest.Append(" · ").Append(UiText.T("ui.item.level", "Level")).Append(' ').Append(item.Level);
        }
        if (item.Amount > 1) rest.Append("  ×").Append(item.Amount);
        string subtitle = Color(rarityHex, UiText.RarityName(item.Rarity)) + Color(UiTheme.TextDim, rest.ToString());

        var body = new StringBuilder();
        AppendStats(body, item);
        AppendUnique(body, item);
        AppendSet(body, item, def, pc);
        AppendFlavor(body, item);

        return new ItemDescription
        {
            Rarity = item.Rarity,
            Title = $"<size=130%><b>{Color(rarityHex, UiText.ItemName(item))}</b></size>",
            Subtitle = subtitle,
            Body = body.ToString().TrimEnd('\n'),
            Compare = slot != null && slot.StartsWith("bag.", StringComparison.Ordinal) ? BuildCompare(item, def, pc) : null,
        };
    }

    private static void AppendStats(StringBuilder sb, ItemInstance item)
    {
        foreach (var s in item.Stats)
            if (!LootRoller.IsSocketStat(s.StatId))
                sb.Append(Color(UiTheme.Stat, $"{UiText.StatValue(s.StatId, s.Value)} {UiText.StatName(s.StatId)}")).Append('\n');
    }

    private static void AppendUnique(StringBuilder sb, ItemInstance item)
    {
        var text = ModContent.Text.Get(item.BaseId + ".unique", "");
        if (text.Length > 0) sb.Append(Color(UiTheme.Unique, text)).Append('\n');
    }

    private static void AppendSet(StringBuilder sb, ItemInstance item, ItemBaseDef? def, PlayerCharacter pc)
    {
        if (def?.Set == null || !pc.Content.Sets.TryGetValue(def.Set, out var set)) return;
        var counts = ItemEffects.SetCounts(pc.Inventory.Equipped.Select(kv => kv.Value), pc.Content);
        int have = counts.GetValueOrDefault(set.Id);
        int total = pc.Content.Items.Values.Count(i => i.Set == set.Id);
        string name = ModContent.Text.Get(set.Id + ".name", set.Name);

        sb.Append('\n').Append(Color(UiTheme.Gold, $"<b>{name}</b> ({have}/{total})")).Append('\n');
        foreach (var bonus in set.Bonuses.OrderBy(b => b.Pieces))
        {
            var text = ModContent.Text.Get($"{set.Id}.bonus.{bonus.Pieces}", "");
            if (text.Length == 0) continue;
            bool active = have >= bonus.Pieces;
            sb.Append(Color(active ? UiTheme.Good : UiTheme.TextFaint, $"({bonus.Pieces}) {text}")).Append('\n');
        }
    }

    private static void AppendFlavor(StringBuilder sb, ItemInstance item)
    {
        var desc = ModContent.Text.Get(item.BaseId + ".description", "");
        var lore = ModContent.Text.Get(item.BaseId + ".lore", "");
        if (desc.Length > 0 || lore.Length > 0) sb.Append('\n');
        if (desc.Length > 0) sb.Append("<i>").Append(Color(UiTheme.Description, desc)).Append("</i>\n");
        if (lore.Length > 0) sb.Append("<i>").Append(Color(UiTheme.TextFaint, lore)).Append("</i>\n");
    }

    /// <summary>The slot <see cref="Inventory.Equip"/> would put this item in: the first free one, else the first.</summary>
    public static string? TargetSlot(ItemBaseDef? def, Inventory inv)
    {
        if (def == null) return null;
        var slots = LootRoller.SlotsFor(def.Type);
        if (slots.Count == 0) return null;
        return slots.FirstOrDefault(s => inv.Get(s) == null) ?? slots[0];
    }

    private static string? BuildCompare(ItemInstance item, ItemBaseDef? def, PlayerCharacter pc)
    {
        var target = TargetSlot(def, pc.Inventory);
        if (target == null) return null;
        var current = pc.Inventory.Get(target);

        var sb = new StringBuilder();
        if (current == null)
            sb.Append(Color(UiTheme.TextDim, $"{UiText.T("ui.compare.empty", "Nothing equipped in")} {UiText.SlotName(target)}"));
        else
            sb.Append(Color(UiTheme.TextDim, UiText.T("ui.compare.vs", "Compared with equipped")))
              .Append(' ').Append(Color(UiTheme.RarityHex(current.Rarity), UiText.ItemName(current)));

        var now = Sum(item);
        var then = current == null ? new Dictionary<string, double>() : Sum(current);
        bool any = false;
        foreach (var id in now.Keys.Concat(then.Keys).Distinct())
        {
            double delta = now.GetValueOrDefault(id) - then.GetValueOrDefault(id);
            if (UiText.IsNegligible(id, delta)) continue;
            any = true;
            sb.Append('\n').Append(Color(delta > 0 ? UiTheme.Good : UiTheme.Bad, $"{UiText.StatValue(id, delta)} {UiText.StatName(id)}"));
        }
        if (!any && current != null) sb.Append('\n').Append(Color(UiTheme.TextDim, UiText.T("ui.compare.same", "No stat difference")));

        if (current != null && item.Level != current.Level)
        {
            int d = item.Level - current.Level;
            sb.Append('\n').Append(Color(d > 0 ? UiTheme.Good : UiTheme.Bad, $"{(d > 0 ? "+" : "")}{d} {UiText.T("ui.item.level", "Level")}"));
        }
        return sb.ToString();
    }

    /// <summary>Each stat's rolls on one item, combined by the stat's own rule (COS-96: dodge and reduction do not simply add).</summary>
    private static Dictionary<string, double> Sum(ItemInstance item) =>
        item.Stats.Where(s => !LootRoller.IsSocketStat(s.StatId)).GroupBy(s => s.StatId)
            .ToDictionary(g => g.Key, g => g.Count() == 1 ? g.First().Value
                : StatCombineMath.Combine(ModContent.Db.ItemStats.TryGetValue(g.Key, out var def) ? def.Combine : StatCombine.Sum, g.Select(x => (double)x.Value)));

    private static string Color(string hex, string text) => $"<color={hex}>{text}</color>";
}
