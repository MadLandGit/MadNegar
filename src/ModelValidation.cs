using System.Buffers.Binary;
using System.Text.Json;

namespace MadNegar;

/// <summary>
/// Shared, deliberately conservative checks for local model folders. A download-complete
/// marker is written only after validation, while structural checks keep older valid
/// installs usable when they predate the marker.
/// </summary>
internal static class ModelValidation
{
    private const uint WhisperGgmlMagic = 0x67676d6c;
    private const long MinimumWhisperModelBytes = 8L * 1024 * 1024;
    public const string ProviderCompletionMarkerName = ".madnegar-download-complete";

    public static string WhisperCompletionMarker(string modelPath) => modelPath + ".madnegar-download-complete";

    public static bool IsWhisperModelReady(string modelPath) =>
        !File.Exists(modelPath + ".partial") && IsWhisperModelFileValid(modelPath);

    public static bool IsWhisperModelFileValid(string modelPath)
    {
        try
        {
            var info = new FileInfo(modelPath);
            if (!info.Exists || info.Length < MinimumWhisperModelBytes) return false;

            using var stream = new FileStream(modelPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            Span<byte> header = stackalloc byte[4];
            if (stream.Read(header) != header.Length) return false;
            // whisper.cpp ggml model files start with GGML_FILE_MAGIC (little-endian).
            // This also rejects HTTP error pages that were accidentally saved as model files.
            return BinaryPrimitives.ReadUInt32LittleEndian(header) == WhisperGgmlMagic;
        }
        catch
        {
            return false;
        }
    }

    public static void WriteWhisperCompletionMarker(string modelPath)
    {
        File.WriteAllText(WhisperCompletionMarker(modelPath), DateTimeOffset.UtcNow.ToString("O"));
    }

    public static bool HasKaldiModelStructure(string directory)
    {
        try
        {
            var am = Path.Combine(directory, "am");
            var conf = Path.Combine(directory, "conf");
            var graph = Path.Combine(directory, "graph");
            if (!Directory.Exists(am) || !Directory.Exists(conf) || !Directory.Exists(graph)) return false;
            if (HasPartialArtifacts(directory)) return false;

            // Directories alone are not sufficient: truncated archives may create these early.
            var hasAcousticModel = Directory.EnumerateFiles(am, "*.mdl", SearchOption.AllDirectories)
                .Any(path => SafeLength(path) > 0);
            var hasDecodeGraph = Directory.EnumerateFiles(graph, "*.fst", SearchOption.AllDirectories)
                .Any(path => SafeLength(path) > 0);
            var hasConfig = Directory.EnumerateFiles(conf, "*", SearchOption.AllDirectories)
                .Any(path => SafeLength(path) > 0);
            return hasAcousticModel && hasDecodeGraph && hasConfig;
        }
        catch
        {
            return false;
        }
    }

    public static bool IsFasterWhisperModelReady(string directory)
    {
        try
        {
            if (!Directory.Exists(directory) || HasPartialArtifacts(directory)) return false;
            if (!HasNonEmptyFile(Path.Combine(directory, "model.bin")) ||
                !IsJsonObjectFile(Path.Combine(directory, "config.json")))
                return false;

            // CTranslate2 models need tokenizer/vocabulary assets as well as model.bin/config.json.
            var hasTokenizer = HasNonEmptyFile(Path.Combine(directory, "tokenizer.json")) ||
                               HasNonEmptyFile(Path.Combine(directory, "vocabulary.json")) ||
                               HasNonEmptyFile(Path.Combine(directory, "vocabulary.txt"));
            return hasTokenizer;
        }
        catch
        {
            return false;
        }
    }

    public static bool HasPartialArtifacts(string directory)
    {
        try
        {
            return Directory.Exists(directory) &&
                   (Directory.EnumerateFiles(directory, "*.partial", SearchOption.AllDirectories).Any() ||
                    Directory.EnumerateFiles(directory, "*.part", SearchOption.AllDirectories).Any());
        }
        catch
        {
            return true;
        }
    }

    public static bool HasNonEmptyFile(string path) => SafeLength(path) > 0;

    public static void WriteProviderCompletionMarker(string directory)
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, ProviderCompletionMarkerName), DateTimeOffset.UtcNow.ToString("O"));
    }

    public static bool IsJsonObjectFile(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            using var document = JsonDocument.Parse(stream);
            return document.RootElement.ValueKind == JsonValueKind.Object;
        }
        catch
        {
            return false;
        }
    }

    private static long SafeLength(string path)
    {
        try { return File.Exists(path) ? new FileInfo(path).Length : 0; }
        catch { return 0; }
    }
}
