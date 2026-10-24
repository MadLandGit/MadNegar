using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace MadNegar;

/// <summary>
/// In-memory, privacy-conscious runtime diagnostics. This class never stores transcript text or audio.
/// The user explicitly chooses when a report is copied from Settings.
/// </summary>
internal static class DiagnosticsState
{
    private static readonly object Gate = new();
    private static DateTime _snapshotUtc = DateTime.UtcNow;
    private static string _stage = "Idle";
    private static string _microphoneName = "Not recorded yet";
    private static string _audioFormat = "Not recorded yet";
    private static string _audioMeterStatus = "Waiting for microphone data";
    private static float _peakSinceStart;
    private static float _latestRms;
    private static bool _clippingObserved;
    private static string _provider = "Not selected";
    private static string _modelId = "Not selected";
    private static string _modelPath = "Not available";
    private static string _runtimeStatus = "Not checked";
    private static string _spokenLanguage = "auto";
    private static string _autoStrategy = "balanced";
    private static string _lastOperationDuration = "Not recorded yet";
    private static long _livePreviewCompletedCount;
    private static long _livePreviewSkippedCount;
    private static string _lastLivePreviewDuration = "Not recorded yet";
    private static string _lastError = "None";
    private static string _lastErrorStage = "None";

    public static void RecordingStarted(string microphoneName, int sampleRate, int channels, int bitsPerSample, string encoding)
    {
        lock (Gate)
        {
            _snapshotUtc = DateTime.UtcNow;
            _stage = "Recording";
            _microphoneName = SafeValue(microphoneName, "Windows default microphone");
            _audioFormat = $"{sampleRate} Hz, {channels} channel(s), {bitsPerSample}-bit, {SafeValue(encoding, "unknown encoding")}";
            _audioMeterStatus = "Measuring";
            _peakSinceStart = 0f;
            _latestRms = 0f;
            _clippingObserved = false;
            _livePreviewCompletedCount = 0;
            _livePreviewSkippedCount = 0;
            _lastLivePreviewDuration = "Not recorded yet";
        }
    }

    public static void UpdateAudioMeter(float peak, float rms)
    {
        lock (Gate)
        {
            _peakSinceStart = Math.Max(_peakSinceStart, Math.Clamp(float.IsFinite(peak) ? peak : 0f, 0f, 1f));
            _latestRms = Math.Clamp(float.IsFinite(rms) ? rms : 0f, 0f, 1f);
            if (_peakSinceStart >= 0.98f || peak >= 0.98f) _clippingObserved = true;
            _audioMeterStatus = "Supported";
            _snapshotUtc = DateTime.UtcNow;
        }
    }

    public static void SetAudioMeterUnsupported(string formatDescription)
    {
        lock (Gate)
        {
            _audioMeterStatus = "Unsupported format: " + SafeValue(formatDescription, "unknown");
            _snapshotUtc = DateTime.UtcNow;
        }
    }

    public static void SetTranscriptionContext(string provider, string modelId, string modelPath,
        string runtimeStatus, string spokenLanguage, string autoStrategy)
    {
        lock (Gate)
        {
            _provider = SafeValue(provider, "Unknown provider");
            _modelId = SafeValue(modelId, "Unknown model");
            _modelPath = SafeValue(modelPath, "Not available");
            _runtimeStatus = SafeValue(runtimeStatus, "Not checked");
            _spokenLanguage = SafeValue(spokenLanguage, "auto");
            _autoStrategy = SafeValue(autoStrategy, "balanced");
            _stage = "Preparing transcription";
            _snapshotUtc = DateTime.UtcNow;
        }
    }

    public static void SetStage(string stage)
    {
        lock (Gate)
        {
            _stage = SafeValue(stage, "Unknown");
            _snapshotUtc = DateTime.UtcNow;
        }
    }

    public static void RecordOperationDuration(TimeSpan elapsed)
    {
        lock (Gate)
        {
            _lastOperationDuration = Math.Max(0d, elapsed.TotalSeconds).ToString("0.00", CultureInfo.InvariantCulture) + " s";
            _snapshotUtc = DateTime.UtcNow;
        }
    }

    public static void RecordLivePreviewCompleted(TimeSpan elapsed)
    {
        lock (Gate)
        {
            _livePreviewCompletedCount++;
            _lastLivePreviewDuration = Math.Max(0d, elapsed.TotalSeconds).ToString("0.000", CultureInfo.InvariantCulture) + " s";
            _snapshotUtc = DateTime.UtcNow;
        }
    }

    public static void RecordLivePreviewSkipped()
    {
        lock (Gate)
        {
            _livePreviewSkippedCount++;
            _snapshotUtc = DateTime.UtcNow;
        }
    }

    public static void RecordError(string stage, string message)
    {
        lock (Gate)
        {
            _lastErrorStage = SafeValue(stage, "Unknown stage");
            _lastError = SafeValue(message, "Unspecified error");
            _stage = "Error: " + _lastErrorStage;
            _snapshotUtc = DateTime.UtcNow;
        }
    }

    public static string CreateReport(string appRoot, string selectedMicrophone, string selectedModel,
        string spokenLanguage, string autoStrategy, bool historyEnabled)
    {
        lock (Gate)
        {
            var root = SafePath(appRoot);
            var mic = SafeValue(selectedMicrophone, "Windows default microphone");
            var model = SafeValue(selectedModel, "Not selected");
            var language = SafeValue(spokenLanguage, "auto");
            var strategy = SafeValue(autoStrategy, "balanced");
            var peakDb = ToDb(_peakSinceStart);
            var rmsDb = ToDb(_latestRms);
            var builder = new StringBuilder();
            builder.AppendLine("MadNegar Diagnostics (in-memory snapshot)");
            builder.AppendLine("Generated UTC: " + DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
            builder.AppendLine("OS: " + Environment.OSVersion);
            builder.AppendLine("Runtime: " + RuntimeInformation.FrameworkDescription);
            builder.AppendLine("Process architecture: " + RuntimeInformation.ProcessArchitecture);
            builder.AppendLine("Application data: " + root);
            builder.AppendLine("Current stage: " + _stage);
            builder.AppendLine("Selected microphone setting: " + mic);
            builder.AppendLine("Last captured microphone: " + _microphoneName);
            builder.AppendLine("Last audio format: " + _audioFormat);
            builder.AppendLine("Audio meter: " + _audioMeterStatus);
            builder.AppendLine($"Peak (boosted meter): {_peakSinceStart.ToString("0.000", CultureInfo.InvariantCulture)} ({peakDb})");
            builder.AppendLine($"Latest RMS (boosted meter): {_latestRms.ToString("0.000", CultureInfo.InvariantCulture)} ({rmsDb})");
            builder.AppendLine("Clipping observed (peak >= 0.98): " + (_clippingObserved ? "Yes" : "No"));
            builder.AppendLine("Selected model setting: " + model);
            builder.AppendLine("Last runtime provider/model: " + _provider + " / " + _modelId);
            builder.AppendLine("Model path: " + SafePath(_modelPath));
            builder.AppendLine("Runtime status: " + _runtimeStatus);
            builder.AppendLine("Spoken language: " + language);
            builder.AppendLine("Auto language strategy: " + strategy);
            builder.AppendLine("Last final-transcription duration: " + _lastOperationDuration);
            builder.AppendLine("Live preview chunks completed: " + _livePreviewCompletedCount.ToString(CultureInfo.InvariantCulture));
            builder.AppendLine("Live preview ticks skipped while busy: " + _livePreviewSkippedCount.ToString(CultureInfo.InvariantCulture));
            builder.AppendLine("Last live preview processing duration: " + _lastLivePreviewDuration);
            builder.AppendLine("Last error stage: " + _lastErrorStage);
            builder.AppendLine("Last error: " + SafePath(_lastError));
            builder.AppendLine("Local transcript history: " + (historyEnabled ? "Enabled (explicit opt-in)" : "Off"));
            builder.AppendLine("Privacy: diagnostics are not written to disk automatically; this report contains no transcript or audio.");
            return builder.ToString();
        }
    }

    private static string ToDb(float value) => value <= 0.000001f
        ? "-infinity dBFS"
        : (20d * Math.Log10(value)).ToString("0.0", CultureInfo.InvariantCulture) + " dBFS";

    private static string SafeValue(string? value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value)) return fallback;
        var cleaned = value.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return cleaned.Length > 500 ? cleaned[..500] : cleaned;
    }

    private static string SafePath(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "Not available";
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var result = value.Replace('\r', ' ').Replace('\n', ' ');
        if (!string.IsNullOrEmpty(localAppData))
            result = result.Replace(localAppData, "%LOCALAPPDATA%", StringComparison.OrdinalIgnoreCase);
        return result.Length > 700 ? result[..700] : result;
    }
}
