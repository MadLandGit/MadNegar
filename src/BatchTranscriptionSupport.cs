using System.Text;

namespace MadNegar;

internal sealed record BatchAudioFileOutputState(
    string OutputDirectory,
    string OutputStem,
    IReadOnlySet<string> CompletedFormats);

internal sealed record BatchAudioOutputOptions(
    string OutputDirectory,
    bool ExportTxt,
    bool ExportSrt,
    bool ExportVtt,
    IReadOnlyDictionary<string, BatchAudioFileOutputState>? PriorOutputStates = null);

internal sealed record BatchAudioFileResult(
    string InputPath,
    string Status,
    string Details,
    string? OutputStem = null,
    IReadOnlySet<string>? CompletedFormats = null);

internal sealed record BatchAudioProgress(
    int Index,
    int Total,
    string InputPath,
    string Status,
    string Details);

internal static class BatchTranscriptionSupport
{
    private static readonly HashSet<string> SupportedAudioExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".wav", ".mp3", ".aif", ".aiff" };
    private static readonly HashSet<string> SupportedOutputExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".txt", ".srt", ".vtt" };
    private static readonly HashSet<string> ReservedWindowsNames = BuildReservedNames();

    public static bool IsSupportedAudioPath(string? path) =>
        !string.IsNullOrWhiteSpace(path) && SupportedAudioExtensions.Contains(Path.GetExtension(path));

    public static bool HasAnyOutput(BatchAudioOutputOptions options) =>
        options.ExportTxt || options.ExportSrt || options.ExportVtt;

    public static IReadOnlyList<string> GetSelectedExtensions(BatchAudioOutputOptions options)
    {
        var extensions = new List<string>();
        if (options.ExportTxt) extensions.Add(".txt");
        if (options.ExportSrt) extensions.Add(".srt");
        if (options.ExportVtt) extensions.Add(".vtt");
        return extensions;
    }

    /// <summary>Builds a safe output stem shared by all selected formats, without overwriting existing outputs.</summary>
    public static string GetUniqueOutputStem(string outputDirectory, string inputPath, IReadOnlyList<string> extensions)
    {
        if (string.IsNullOrWhiteSpace(outputDirectory)) throw new ArgumentException("An output directory is required.", nameof(outputDirectory));
        if (string.IsNullOrWhiteSpace(inputPath)) throw new ArgumentException("An input file path is required.", nameof(inputPath));
        ArgumentNullException.ThrowIfNull(extensions);
        if (extensions.Count == 0 || extensions.Any(extension => !SupportedOutputExtensions.Contains(NormalizeExtension(extension))))
            throw new ArgumentException("At least one supported output extension (TXT, SRT, VTT) is required.", nameof(extensions));

        var fullOutputDirectory = Path.GetFullPath(outputDirectory);
        var stem = SanitizeFileStem(Path.GetFileNameWithoutExtension(inputPath));
        for (var suffix = 1; suffix <= 10000; suffix++)
        {
            var candidate = suffix == 1 ? stem : $"{stem} ({suffix})";
            var collision = extensions.Any(extension =>
                File.Exists(Path.Combine(fullOutputDirectory, candidate + NormalizeExtension(extension))) ||
                Directory.Exists(Path.Combine(fullOutputDirectory, candidate + NormalizeExtension(extension))));
            if (!collision) return candidate;
        }
        throw new IOException("Could not allocate a unique output name after 10,000 attempts.");
    }

    public static async Task WriteTextAtomicallyAsync(string destinationPath, string content, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(destinationPath)) throw new ArgumentException("A destination path is required.", nameof(destinationPath));
        if (content is null) throw new ArgumentNullException(nameof(content));

        var fullPath = Path.GetFullPath(destinationPath);
        var directory = Path.GetDirectoryName(fullPath) ?? throw new InvalidOperationException("The output path has no parent directory.");
        Directory.CreateDirectory(directory);
        var temporaryPath = fullPath + $".{Guid.NewGuid():N}.part";
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            await File.WriteAllTextAsync(temporaryPath, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true), cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            // Do not overwrite an existing file; the caller must allocate a unique stem first.
            File.Move(temporaryPath, fullPath, overwrite: false);
        }
        finally
        {
            try { if (File.Exists(temporaryPath)) File.Delete(temporaryPath); } catch { }
        }
    }

    public static string NormalizeExtension(string extension)
    {
        if (string.IsNullOrWhiteSpace(extension)) return string.Empty;
        return extension[0] == '.' ? extension.ToLowerInvariant() : "." + extension.ToLowerInvariant();
    }

    public static string SanitizeFileStem(string? fileName)
    {
        var stem = (fileName ?? string.Empty).Trim();
        var invalid = Path.GetInvalidFileNameChars().Concat("<>:\"/\\|?*").ToHashSet();
        stem = new string(stem.Select(character => invalid.Contains(character) || char.IsControl(character) ? '_' : character).ToArray()).Trim().TrimEnd('.', ' ');
        if (stem.Length > 100) stem = stem[..100].TrimEnd('.', ' ');
        if (string.IsNullOrWhiteSpace(stem)) stem = "Audio";
        var devicePart = stem.Split('.')[0];
        if (ReservedWindowsNames.Contains(devicePart)) stem = "_" + stem;
        return stem;
    }

    private static HashSet<string> BuildReservedNames()
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "CON", "PRN", "AUX", "NUL" };
        for (var index = 1; index <= 9; index++)
        {
            names.Add($"COM{index}");
            names.Add($"LPT{index}");
        }
        return names;
    }
}
