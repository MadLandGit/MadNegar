using System.Text;
using System.Text.Json;

namespace MadNegar;

internal sealed class TranscriptHistoryEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public string Language { get; set; } = "auto";
    public string Model { get; set; } = "unknown";
    public string Text { get; set; } = string.Empty;
}

/// <summary>Opt-in, local-only transcript history. Audio is never stored by this class.</summary>
internal static class TranscriptHistoryStore
{
    public const int MaximumEntries = 100;
    public const int MaximumTranscriptCharacters = 20_000;
    public const long MaximumStoreBytes = 5L * 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static string GetPath(string rootDirectory) => Path.Combine(rootDirectory, "history.json");

    public static List<TranscriptHistoryEntry> Load(string rootDirectory)
    {
        var path = GetPath(rootDirectory);
        if (!File.Exists(path)) return new List<TranscriptHistoryEntry>();
        var info = new FileInfo(path);
        if (info.Length > MaximumStoreBytes)
            throw new InvalidDataException($"Transcript history exceeds the {MaximumStoreBytes / (1024 * 1024)} MB safety limit.");

        var entries = JsonSerializer.Deserialize<List<TranscriptHistoryEntry>>(File.ReadAllText(path, Encoding.UTF8))
            ?? new List<TranscriptHistoryEntry>();
        return entries
            .Where(IsValidEntry)
            .OrderByDescending(entry => entry.CreatedAtUtc)
            .Take(MaximumEntries)
            .Select(CloneNormalized)
            .ToList();
    }

    public static TranscriptHistoryEntry Add(string rootDirectory, string text, string language, string model)
    {
        if (string.IsNullOrWhiteSpace(text)) throw new InvalidDataException("An empty transcript cannot be added to history.");
        if (text.Length > MaximumTranscriptCharacters)
            throw new InvalidDataException($"This transcript is longer than the history limit ({MaximumTranscriptCharacters} characters). Export it to TXT instead.");

        var entries = Load(rootDirectory);
        var entry = new TranscriptHistoryEntry
        {
            Id = Guid.NewGuid().ToString("N"),
            CreatedAtUtc = DateTimeOffset.UtcNow,
            Language = NormalizeLanguage(language),
            Model = NormalizeLabel(model, 120),
            Text = text
        };
        entries.Insert(0, entry);
        while (entries.Count > MaximumEntries) entries.RemoveAt(entries.Count - 1);

        byte[] data = Serialize(entries);
        while (data.LongLength > MaximumStoreBytes && entries.Count > 1)
        {
            entries.RemoveAt(entries.Count - 1);
            data = Serialize(entries);
        }
        if (data.LongLength > MaximumStoreBytes)
            throw new InvalidDataException("This transcript cannot fit within the local history size limit. Export it to TXT instead.");

        AtomicWrite(GetPath(rootDirectory), data);
        return entry;
    }

    public static bool Delete(string rootDirectory, string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return false;
        var entries = Load(rootDirectory);
        var removed = entries.RemoveAll(entry => string.Equals(entry.Id, id, StringComparison.Ordinal)) > 0;
        if (removed) AtomicWrite(GetPath(rootDirectory), Serialize(entries));
        return removed;
    }

    public static void Clear(string rootDirectory)
    {
        var path = GetPath(rootDirectory);
        var temporary = path + ".tmp";
        if (File.Exists(path)) File.Delete(path);
        if (File.Exists(temporary)) File.Delete(temporary);
    }

    private static bool IsValidEntry(TranscriptHistoryEntry? entry) => entry is not null
        && !string.IsNullOrWhiteSpace(entry.Id)
        && !string.IsNullOrWhiteSpace(entry.Text)
        && entry.Text.Length <= MaximumTranscriptCharacters;

    private static TranscriptHistoryEntry CloneNormalized(TranscriptHistoryEntry entry) => new()
    {
        Id = NormalizeLabel(entry.Id, 64),
        CreatedAtUtc = entry.CreatedAtUtc == default ? DateTimeOffset.UtcNow : entry.CreatedAtUtc,
        Language = NormalizeLanguage(entry.Language),
        Model = NormalizeLabel(entry.Model, 120),
        Text = entry.Text
    };

    private static string NormalizeLanguage(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "fa" => "fa",
        "en" => "en",
        _ => "auto"
    };

    private static string NormalizeLabel(string? value, int maxLength)
    {
        var normalized = (value ?? string.Empty).Trim();
        return normalized.Length <= maxLength ? normalized : normalized[..maxLength];
    }

    private static byte[] Serialize(List<TranscriptHistoryEntry> entries) => JsonSerializer.SerializeToUtf8Bytes(entries, JsonOptions);

    private static void AtomicWrite(string path, byte[] data)
    {
        var directory = Path.GetDirectoryName(path);
        if (string.IsNullOrWhiteSpace(directory)) throw new InvalidOperationException("History storage path has no parent directory.");
        Directory.CreateDirectory(directory);
        var temporary = path + ".tmp";
        try
        {
            File.WriteAllBytes(temporary, data);
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); } catch { }
        }
    }
}
