using System.Text.Json;
using System.Text.RegularExpressions;

namespace MadNegar;

/// <summary>A local, user-controlled phrase replacement. No cloud service is involved.</summary>
internal sealed class LocalDictionaryEntry
{
    // Explicit public constructor keeps System.Text.Json import reliable for this internal model type.
    public LocalDictionaryEntry() { }

    public string Phrase { get; set; } = string.Empty;
    public string Replacement { get; set; } = string.Empty;
    public string Language { get; set; } = LocalDictionaryText.AnyLanguage;
    public bool Enabled { get; set; } = true;
}

/// <summary>Validates and applies phrase rules in a single pass, protecting URLs, e-mail and code.</summary>
internal static class LocalDictionaryText
{
    public const string AnyLanguage = "any";
    public const string PersianLanguage = "fa";
    public const string EnglishLanguage = "en";
    public const int MaximumEntries = 200;
    public const int MaximumPhraseLength = 100;
    public const int MaximumReplacementLength = 200;
    public const int MaximumImportBytes = 1024 * 1024;

    private static readonly Regex ProtectedSegments = new(
        @"```[\s\S]*?```|`[^`\r\n]*`|https?://[^\s<>]+|www\.[^\s<>]+|[\w.+-]+@[\w.-]+\.[A-Za-z]{2,}",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static string NormalizeLanguage(string? language) => language?.Trim().ToLowerInvariant() switch
    {
        PersianLanguage => PersianLanguage,
        EnglishLanguage => EnglishLanguage,
        _ => AnyLanguage
    };

    public static List<LocalDictionaryEntry> NormalizeEntries(IEnumerable<LocalDictionaryEntry>? entries)
    {
        var result = new List<LocalDictionaryEntry>();
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (entries is null) return result;

        foreach (var item in entries)
        {
            if (item is null || result.Count >= MaximumEntries) break;
            var phrase = (item.Phrase ?? string.Empty).Trim();
            var replacement = (item.Replacement ?? string.Empty).Trim();
            var language = NormalizeLanguage(item.Language);
            if (phrase.Length == 0 || replacement.Length == 0 ||
                phrase.Length > MaximumPhraseLength || replacement.Length > MaximumReplacementLength ||
                phrase.Contains('\r') || phrase.Contains('\n') || replacement.Contains('\r') || replacement.Contains('\n'))
                continue;
            if (string.Equals(phrase, replacement, StringComparison.Ordinal)) continue;

            // A phrase can have separate Persian/English rules, but duplicates in the same scope are ignored.
            var key = language + "\0" + phrase;
            if (!keys.Add(key)) continue;
            result.Add(new LocalDictionaryEntry
            {
                Phrase = phrase,
                Replacement = replacement,
                Language = language,
                Enabled = item.Enabled
            });
        }
        return result;
    }

    public static bool TryValidateEntry(string? phrase, string? replacement, string? language,
        out LocalDictionaryEntry entry, out string error)
    {
        entry = new LocalDictionaryEntry();
        phrase = phrase?.Trim();
        replacement = replacement?.Trim();
        language = NormalizeLanguage(language);
        if (string.IsNullOrWhiteSpace(phrase))
        {
            error = "Enter the phrase as it currently appears in the transcript.";
            return false;
        }
        if (string.IsNullOrWhiteSpace(replacement))
        {
            error = "Enter the replacement text. Empty replacements are not allowed to avoid accidental deletion.";
            return false;
        }
        if (phrase.Length > MaximumPhraseLength || replacement.Length > MaximumReplacementLength)
        {
            error = $"Phrase length must be at most {MaximumPhraseLength} characters and replacement length at most {MaximumReplacementLength}.";
            return false;
        }
        if (phrase.Contains('\r') || phrase.Contains('\n') || replacement.Contains('\r') || replacement.Contains('\n'))
        {
            error = "Dictionary phrases and replacements must be single-line text.";
            return false;
        }
        if (string.Equals(phrase, replacement, StringComparison.Ordinal))
        {
            error = "The phrase and replacement are identical; no rule is needed.";
            return false;
        }

        entry = new LocalDictionaryEntry { Phrase = phrase, Replacement = replacement, Language = language, Enabled = true };
        error = string.Empty;
        return true;
    }

    public static string Apply(string text, IEnumerable<LocalDictionaryEntry>? entries, string? spokenLanguage = "auto")
    {
        if (string.IsNullOrEmpty(text) || entries is null) return text;
        var activeLanguage = NormalizeLanguage(spokenLanguage);
        var active = NormalizeEntries(entries)
            .Where(entry => entry.Enabled && (entry.Language == AnyLanguage ||
                (activeLanguage != AnyLanguage && entry.Language == activeLanguage)))
            .GroupBy(entry => entry.Phrase, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(entry => entry.Language == activeLanguage && activeLanguage != AnyLanguage).First())
            .OrderByDescending(entry => entry.Phrase.Length)
            .ToList();
        if (active.Count == 0) return text;

        var replacements = active.ToDictionary(entry => entry.Phrase, entry => entry.Replacement, StringComparer.OrdinalIgnoreCase);
        var alternatives = string.Join("|", active.Select(entry => Regex.Escape(entry.Phrase)));
        var matcher = new Regex($@"(?<![\p{{L}}\p{{N}}_])(?:{alternatives})(?![\p{{L}}\p{{N}}_])",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        var output = new System.Text.StringBuilder(text.Length + 8);
        var previousEnd = 0;
        foreach (Match protectedMatch in ProtectedSegments.Matches(text))
        {
            if (protectedMatch.Index > previousEnd)
                output.Append(ApplyPlainSegment(text[previousEnd..protectedMatch.Index], matcher, replacements));
            output.Append(protectedMatch.Value);
            previousEnd = protectedMatch.Index + protectedMatch.Length;
        }
        if (previousEnd < text.Length)
            output.Append(ApplyPlainSegment(text[previousEnd..], matcher, replacements));
        return output.ToString();
    }

    public static List<LocalDictionaryEntry> DeserializeImport(string json)
    {
        if (System.Text.Encoding.UTF8.GetByteCount(json) > MaximumImportBytes)
            throw new InvalidDataException($"Dictionary import is too large; limit is {MaximumImportBytes / 1024} KB.");
        var entries = JsonSerializer.Deserialize<List<LocalDictionaryEntry>>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidDataException("The file does not contain a dictionary list.");
        if (entries.Count > MaximumEntries)
            throw new InvalidDataException($"The import contains {entries.Count} entries; the limit is {MaximumEntries}. Split the file into smaller dictionaries.");
        var normalized = NormalizeEntries(entries);
        if (entries.Count > 0 && normalized.Count == 0)
            throw new InvalidDataException("The file contains no valid dictionary entries.");
        return normalized;
    }

    private static string ApplyPlainSegment(string segment, Regex matcher, IReadOnlyDictionary<string, string> replacements) =>
        matcher.Replace(segment, match => replacements.TryGetValue(match.Value, out var replacement) ? replacement : match.Value);
}
