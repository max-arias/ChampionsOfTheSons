namespace ChampionsOfTheSons.Core.Rules;

/// <summary>The rule behind the mod's one-time key move (COS-104): keys still on an old default that clashes with the game move to the new default.</summary>
public static class KeyMigration
{
    /// <summary>
    /// For each (id, current, old default, new default): the key it should have after the move. An entry moves only when it is still on its
    /// old default, and only if the new key is not left in use by an entry that keeps its key.
    /// </summary>
    public static Dictionary<string, string> Plan(IReadOnlyList<(string Id, string Current, string Old, string New)> entries)
    {
        var result = entries.ToDictionary(e => e.Id, e => e.Current);
        var movers = entries.Where(e => string.Equals(e.Current, e.Old, StringComparison.OrdinalIgnoreCase)).ToList();
        foreach (var m in movers) result[m.Id] = m.New;
        // Undo moves that land on a key a non-moving entry keeps (repeat: undoing one can free or block another).
        bool changed = true;
        while (changed)
        {
            changed = false;
            foreach (var m in movers)
            {
                if (result[m.Id] != m.New) continue;
                bool clash = entries.Any(o => o.Id != m.Id && string.Equals(result[o.Id], m.New, StringComparison.OrdinalIgnoreCase));
                if (!clash) continue;
                result[m.Id] = m.Current;
                changed = true;
            }
        }
        return result;
    }
}
