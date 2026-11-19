using System.Text.Json;

namespace ChampionsOfTheSons.Core.Content;

/// <summary>
/// Player-facing strings: one flat JSON object per language in content/text/&lt;lang&gt;.json
/// ("item.x.name", "item.x.lore", "perk.x.effect", ...). Missing keys fall back to English, then to the key.
/// </summary>
public sealed class TextDb
{
    public const string English = "en";

    /// <summary>Language codes we ship, matching the original mod's locales plus English.</summary>
    public static readonly IReadOnlyList<string> Languages = new[]
    {
        "en", "zh-Hans", "zh-Hant", "fi", "fr", "de", "it", "ja", "ko", "pl", "ru", "es", "tr",
    };

    private readonly Dictionary<string, Dictionary<string, string>> _tables = new();
    private readonly List<string> _errors = new();

    public string Language { get; set; } = English;
    public IReadOnlyList<string> Errors => _errors;
    public IEnumerable<string> LoadedLanguages => _tables.Keys;

    /// <summary>Files keyed by path relative to content/ ("text/en.json").</summary>
    public static TextDb Load(IReadOnlyDictionary<string, string> files)
    {
        var db = new TextDb();
        foreach (var (path, json) in files)
        {
            var normalized = path.Replace('\\', '/');
            if (!normalized.StartsWith("text/", StringComparison.Ordinal) || !normalized.EndsWith(".json", StringComparison.Ordinal)) continue;
            // "text/en.json" and fragments like "text/en.loot.json" all merge into language "en".
            var name = normalized.Substring(5, normalized.Length - 10);
            int dot = name.IndexOf('.');
            var lang = dot < 0 ? name : name.Substring(0, dot);
            try
            {
                var table = JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new();
                if (!db._tables.TryGetValue(lang, out var existing)) db._tables[lang] = existing = new();
                foreach (var (k, v) in table)
                {
                    if (existing.ContainsKey(k)) db._errors.Add($"{normalized}: duplicate key '{k}'");
                    existing[k] = v;
                }
            }
            catch (JsonException e)
            {
                db._errors.Add($"{normalized}: {e.Message}");
            }
        }
        if (!db._tables.ContainsKey(English)) db._errors.Add("text/en.json: missing");
        return db;
    }

    public static TextDb LoadFolder(string contentFolder)
    {
        var textDir = Path.Combine(contentFolder, "text");
        var files = Directory.Exists(textDir)
            ? Directory.GetFiles(textDir, "*.json").ToDictionary(f => "text/" + Path.GetFileName(f), File.ReadAllText)
            : new Dictionary<string, string>();
        return Load(files);
    }

    public bool Has(string key, string? language = null) =>
        _tables.TryGetValue(language ?? Language, out var t) && t.ContainsKey(key);

    /// <summary>The string in the current language, else English, else <paramref name="fallback"/>, else the key.</summary>
    public string Get(string key, string? fallback = null)
    {
        if (_tables.TryGetValue(Language, out var t) && t.TryGetValue(key, out var s)) return s;
        if (_tables.TryGetValue(English, out var en) && en.TryGetValue(key, out s)) return s;
        return fallback ?? key;
    }

    /// <summary>Keys present in English but missing in <paramref name="language"/>.</summary>
    public IEnumerable<string> MissingIn(string language)
    {
        if (!_tables.TryGetValue(English, out var en)) return Array.Empty<string>();
        _tables.TryGetValue(language, out var other);
        return en.Keys.Where(k => other == null || !other.ContainsKey(k));
    }
}
