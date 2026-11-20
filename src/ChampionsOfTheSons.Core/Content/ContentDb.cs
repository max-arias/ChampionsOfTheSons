using System.Text.Json;
using System.Text.Json.Serialization;

namespace ChampionsOfTheSons.Core.Content;

/// <summary>
/// All content definitions, loaded from the content JSON files and validated once.
/// Validation problems never throw: they are collected in <see cref="Errors"/> and the bad entries skipped,
/// so one broken definition can't stop the mod from loading.
/// </summary>
public sealed class ContentDb
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new LooseEnumConverterFactory() },
    };

    private readonly Dictionary<string, PlayerStatDef> _playerStats = new();
    private readonly Dictionary<string, ItemStatDef> _itemStats = new();
    private readonly Dictionary<string, ItemBaseDef> _items = new();
    private readonly Dictionary<string, PerkDef> _perks = new();
    private readonly Dictionary<string, SpellDef> _spells = new();
    private readonly Dictionary<string, SetDef> _sets = new();
    private readonly Dictionary<string, Rules.EnemyKind> _enemies = new();
    private readonly Dictionary<string, EliteNameDef> _eliteNames = new();
    private readonly List<string> _errors = new();

    public IReadOnlyDictionary<string, PlayerStatDef> PlayerStats => _playerStats;
    public IReadOnlyDictionary<string, ItemStatDef> ItemStats => _itemStats;
    public IReadOnlyDictionary<string, ItemBaseDef> Items => _items;
    public IReadOnlyDictionary<string, PerkDef> Perks => _perks;
    public IReadOnlyDictionary<string, SpellDef> Spells => _spells;
    public IReadOnlyDictionary<string, SetDef> Sets => _sets;
    /// <summary>Enemy kinds keyed by the game's actor type name ("Carl", "Fingers").</summary>
    public IReadOnlyDictionary<string, Rules.EnemyKind> Enemies => _enemies;
    /// <summary>Elite name parts by id (COS-68).</summary>
    public IReadOnlyDictionary<string, EliteNameDef> EliteNames => _eliteNames;
    /// <summary>Given-name ids, sorted (picks are random; the order only keeps a seeded pick repeatable).</summary>
    public IReadOnlyList<string> EliteGivenIds { get; private set; } = Array.Empty<string>();
    public IReadOnlyList<string> EliteEpithetIds { get; private set; } = Array.Empty<string>();
    public IReadOnlyList<string> Errors => _errors;

    /// <summary>Loads content from file name → JSON text (e.g. "items.json" → its text). Missing files are errors.</summary>
    public static ContentDb Load(IReadOnlyDictionary<string, string> files)
    {
        var db = new ContentDb();
        db.Add(files, "player_stats.json", "playerStats", db._playerStats, p => p.Id);
        db.Add(files, "item_stats.json", "itemStats", db._itemStats, s => s.Id);
        db.Add(files, "items.json", "items", db._items, i => i.Id);
        // Hand-made items live in their own files (items_<theme>.json) so re-extracting items.json never touches them.
        foreach (var extra in files.Keys.Where(k => k.StartsWith("items_", StringComparison.Ordinal) && k.EndsWith(".json", StringComparison.Ordinal)).OrderBy(k => k))
            db.Add(files, extra, "items", db._items, i => i.Id);
        db.Add(files, "perks.json", "perks", db._perks, p => p.Id, optional: true);
        db.Add(files, "spells.json", "spells", db._spells, s => s.Id, optional: true);
        db.Add(files, "sets.json", "sets", db._sets, s => s.Id, optional: true);
        db.Add(files, "enemies.json", "enemies", db._enemies, e => e.ActorType, optional: true);
        db.Add(files, "elite_names.json", "eliteNames", db._eliteNames, n => n.Id, optional: true);
        db.Validate();
        return db;
    }

    /// <summary>Loads every *.json in a folder.</summary>
    public static ContentDb LoadFolder(string folder) =>
        Load(Directory.GetFiles(folder, "*.json").ToDictionary(f => Path.GetFileName(f), File.ReadAllText));

    public string Summary() =>
        $"{_items.Count} items, {_itemStats.Count} item stats, {_playerStats.Count} player stats, {_perks.Count} perks, {_spells.Count} spells, {_sets.Count} sets, {_enemies.Count} enemy kinds, {_eliteNames.Count} elite name parts, {_errors.Count} content errors";

    private void Add<T>(IReadOnlyDictionary<string, string> files, string file, string key, Dictionary<string, T> into, Func<T, string> id, bool optional = false)
    {
        if (!files.TryGetValue(file, out var json))
        {
            if (!optional) _errors.Add($"{file}: missing");
            return;
        }
        List<T>? list;
        try
        {
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            if (!doc.RootElement.TryGetProperty(key, out var arr))
            {
                _errors.Add($"{file}: no '{key}' array");
                return;
            }
            list = arr.Deserialize<List<T>>(Options);
        }
        catch (JsonException e)
        {
            _errors.Add($"{file}: {e.Message}");
            return;
        }
        foreach (var entry in list ?? new List<T>())
        {
            var entryId = id(entry);
            if (string.IsNullOrWhiteSpace(entryId)) _errors.Add($"{file}: entry without id");
            else if (!into.TryAdd(entryId, entry)) _errors.Add($"{file}: duplicate id '{entryId}'");
        }
    }

    private void Validate()
    {
        foreach (var n in _eliteNames.Values.ToList())
        {
            if (n.Part != EliteNameDef.Given && n.Part != EliteNameDef.Epithet)
            {
                _errors.Add($"elite name {n.Id}: part must be 'given' or 'epithet', not '{n.Part}'");
                _eliteNames.Remove(n.Id);
            }
            else if (string.IsNullOrWhiteSpace(n.Name)) _errors.Add($"elite name {n.Id}: empty name");
        }
        EliteGivenIds = _eliteNames.Values.Where(n => n.Part == EliteNameDef.Given).Select(n => n.Id).OrderBy(i => i, StringComparer.Ordinal).ToArray();
        EliteEpithetIds = _eliteNames.Values.Where(n => n.Part == EliteNameDef.Epithet).Select(n => n.Id).OrderBy(i => i, StringComparer.Ordinal).ToArray();
        if (_eliteNames.Count > 0 && (EliteGivenIds.Count == 0 || EliteEpithetIds.Count == 0))
            _errors.Add("elite_names.json: needs at least one given name and one epithet");
        foreach (var s in _itemStats.Values.ToList())
        {
            foreach (var e in s.Effects)
                if (!_playerStats.ContainsKey(e.Stat)) _errors.Add($"item stat {s.Id}: unknown player stat '{e.Stat}'");
            if (s.Max < s.Min) _errors.Add($"item stat {s.Id}: max < min");
        }
        foreach (var item in _items.Values.ToList())
        {
            if (item.MaxLevel < item.MinLevel) _errors.Add($"item {item.Id}: maxLevel < minLevel");
            if (item.Rarity is < 0 or > 7) _errors.Add($"item {item.Id}: rarity {item.Rarity} out of range");
            foreach (var pool in item.StatPools)
            {
                if (pool.Count == 0) _errors.Add($"item {item.Id}: empty stat pool");
                foreach (var option in pool)
                    if (option != null && option != ItemBaseDef.AnyStat && !_itemStats.ContainsKey(option))
                        _errors.Add($"item {item.Id}: unknown stat '{option}'");
            }
            foreach (var e in item.UniqueEffects)
                if (!_playerStats.ContainsKey(e.Stat)) _errors.Add($"item {item.Id}: unknown player stat '{e.Stat}'");
        }
        foreach (var set in _sets.Values)
            foreach (var bonus in set.Bonuses)
                foreach (var e in bonus.Effects)
                    if (!_playerStats.ContainsKey(e.Stat)) _errors.Add($"set {set.Id}: unknown player stat '{e.Stat}'");
        foreach (var item in _items.Values)
            if (item.Set != null && _sets.Count > 0 && !_sets.ContainsKey(item.Set)) _errors.Add($"item {item.Id}: unknown set '{item.Set}'");
        foreach (var perk in _perks.Values)
        {
            foreach (var e in perk.Effects)
                if (!_playerStats.ContainsKey(e.Stat)) _errors.Add($"perk {perk.Id}: unknown player stat '{e.Stat}'");
            foreach (var other in perk.UnlockPath.Concat(perk.Requires))
                if (!_perks.ContainsKey(other)) _errors.Add($"perk {perk.Id}: unknown perk '{other}'");
        }
    }

    /// <summary>
    /// Reads enums from strings ignoring case and underscores ("one_minus_product" → OneMinusProduct).
    /// Written by hand because .NET 6's JsonStringEnumConverter ignores the naming policy when reading.
    /// </summary>
    private sealed class LooseEnumConverterFactory : JsonConverterFactory
    {
        public override bool CanConvert(Type t) => (Nullable.GetUnderlyingType(t) ?? t).IsEnum;

        public override JsonConverter CreateConverter(Type t, JsonSerializerOptions options)
        {
            var underlying = Nullable.GetUnderlyingType(t);
            return underlying == null
                ? (JsonConverter)Activator.CreateInstance(typeof(LooseEnumConverter<>).MakeGenericType(t))!
                : (JsonConverter)Activator.CreateInstance(typeof(NullableLooseEnumConverter<>).MakeGenericType(underlying))!;
        }
    }

    private sealed class LooseEnumConverter<T> : JsonConverter<T> where T : struct, Enum
    {
        public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Number) return (T)Enum.ToObject(typeof(T), reader.GetInt32());
            var text = reader.GetString()?.Replace("_", "");
            if (text != null && Enum.TryParse<T>(text, ignoreCase: true, out var value)) return value;
            throw new JsonException($"'{reader.GetString()}' is not a {typeof(T).Name}");
        }

        public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value.ToString());
    }

    private sealed class NullableLooseEnumConverter<T> : JsonConverter<T?> where T : struct, Enum
    {
        private readonly LooseEnumConverter<T> _inner = new();

        public override T? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            reader.TokenType == JsonTokenType.Null ? null : _inner.Read(ref reader, typeof(T), options);

        public override void Write(Utf8JsonWriter writer, T? value, JsonSerializerOptions options)
        {
            if (value is T v) _inner.Write(writer, v, options);
            else writer.WriteNullValue();
        }
    }
}
