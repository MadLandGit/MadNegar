using System.Text.Json;

namespace MadNegar;

internal sealed class AppSettings
{
    public string? SelectedMicrophoneName { get; set; }
    // WASAPI endpoint IDs are stable across app launches; the old WinMM index is kept for migration only.
    public string? SelectedMicrophoneId { get; set; }
    public int? SelectedMicrophoneDeviceNumber { get; set; }
    public int MicrophoneBoostDb { get; set; } = 12;
    public string AudioProfile { get; set; } = AudioProfilePolicy.Custom;
    public bool AutoLevelQuietSpeech { get; set; } = true;
    public bool ReduceLowFrequencyRumble { get; set; } = true;
    public bool LiveTranscriptionEnabled { get; set; } = false;
    public bool FastLivePreviewEnabled { get; set; } = true;
    public string SpokenLanguage { get; set; } = "auto";
    // Recognition preference is independent from the UI language and the Speak Language selector.
    // It only biases ambiguous Auto-mode candidate selection; it must not lock a bilingual utterance.
    public string TranscriptionLanguagePreference { get; set; } = AutoLanguageText.PersianFirstPreference;
    // Controls how Auto mode resolves uncertain or mixed Persian/English speech.
    public string AutoLanguageStrategy { get; set; } = AutoLanguageText.Balanced;
    // Dual-model Auto can use any fully downloaded final-transcription engine compatible with the role.
    // Provider-qualified keys avoid collisions such as Whisper.cpp::tiny vs faster-whisper::tiny.
    public string AutoMixedPersianModelKey { get; set; } = "";
    public string AutoMixedEnglishModelKey { get; set; } = "";
    // Legacy model IDs are retained for settings migration from versions <= 0.6.32.
    public string AutoMixedPersianModelId { get; set; } = "small";
    public string AutoMixedEnglishModelId { get; set; } = "base";
    // Preserve common English technical terms in Latin script when Spoken language is Auto.
    public bool PreserveEnglishTechnicalTermsInAuto { get; set; } = true;
    public string? SelectedModelId { get; set; }
    public string? SelectedModelKey { get; set; }
    public string LivePreviewModelKey { get; set; } = "follow-final";
    public string ThemeMode { get; set; } = "Light";
    public bool GlobalHotkeyEnabled { get; set; } = false;
    public string GlobalHotkeyShortcut { get; set; } = GlobalHotkeySupport.DefaultShortcut;
    // When enabled, the final transcript is pasted into the captured destination.
    // When disabled, direct typing only copies the final transcript to the clipboard.
    public bool DirectTypingAutoInsert { get; set; } = true;
    public bool VoiceTypingSoundFeedback { get; set; } = true;
    public bool PersianTextNormalizeYeh { get; set; } = true;
    public bool PersianTextNormalizeKaf { get; set; } = true;
    public bool PersianTextNormalizeHalfSpaces { get; set; } = true;
    public bool PersianTextNormalizePunctuation { get; set; } = true;
    public bool ApplyPersianTextNormalizationToDirectTyping { get; set; } = true;
    public bool ApplyLocalDictionaryToDirectTyping { get; set; } = true;
    public List<LocalDictionaryEntry> LocalDictionaryEntries { get; set; } = new();
    // Explicit opt-in: when false, final transcripts are never written to local history. Audio is never stored.
    public bool TranscriptHistoryEnabled { get; set; } = false;
}

internal static class AppSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static AppSettings Load(string rootDirectory)
    {
        var path = Path.Combine(rootDirectory, "settings.json");
        try
        {
            if (File.Exists(path))
            {
                var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path)) ?? new AppSettings();
                var normalizedTheme = NormalizeThemeMode(settings.ThemeMode);
                var normalizedShortcut = GlobalHotkeySupport.NormalizeShortcut(settings.GlobalHotkeyShortcut);
                var normalizedAutoLanguageStrategy = AutoLanguageText.NormalizeForSettings(settings.AutoLanguageStrategy);
                var normalizedTranscriptionLanguagePreference = AutoLanguageText.NormalizeTranscriptionLanguagePreference(settings.TranscriptionLanguagePreference);
                var normalizedAudioProfile = AudioProfilePolicy.Normalize(settings.AudioProfile);
                var normalizedPersianDualKey = NormalizeDualModelKey(settings.AutoMixedPersianModelKey, settings.AutoMixedPersianModelId);
                var normalizedEnglishDualKey = NormalizeDualModelKey(settings.AutoMixedEnglishModelKey, settings.AutoMixedEnglishModelId);
                var oldDictionaryJson = JsonSerializer.Serialize(settings.LocalDictionaryEntries, JsonOptions);
                var normalizedDictionary = LocalDictionaryText.NormalizeEntries(settings.LocalDictionaryEntries);
                var dictionaryChanged = !string.Equals(oldDictionaryJson, JsonSerializer.Serialize(normalizedDictionary, JsonOptions), StringComparison.Ordinal);
                var needsMigration = !string.Equals(settings.ThemeMode, normalizedTheme, StringComparison.Ordinal)
                    || !string.Equals(settings.GlobalHotkeyShortcut, normalizedShortcut, StringComparison.Ordinal)
                    || !string.Equals(settings.AutoLanguageStrategy, normalizedAutoLanguageStrategy, StringComparison.Ordinal)
                    || !string.Equals(settings.TranscriptionLanguagePreference, normalizedTranscriptionLanguagePreference, StringComparison.Ordinal)
                    || !string.Equals(settings.AudioProfile, normalizedAudioProfile, StringComparison.Ordinal)
                    || !string.Equals(settings.AutoMixedPersianModelKey, normalizedPersianDualKey, StringComparison.Ordinal)
                    || !string.Equals(settings.AutoMixedEnglishModelKey, normalizedEnglishDualKey, StringComparison.Ordinal)
                    || dictionaryChanged;
                settings.ThemeMode = normalizedTheme;
                settings.GlobalHotkeyShortcut = normalizedShortcut;
                settings.AutoLanguageStrategy = normalizedAutoLanguageStrategy;
                settings.TranscriptionLanguagePreference = normalizedTranscriptionLanguagePreference;
                settings.AudioProfile = normalizedAudioProfile;
                settings.AutoMixedPersianModelKey = normalizedPersianDualKey;
                settings.AutoMixedEnglishModelKey = normalizedEnglishDualKey;
                settings.LocalDictionaryEntries = normalizedDictionary;
                if (needsMigration)
                {
                    // Older versions allowed "System". Migrate it to the explicit Light mode
                    // so this release only persists the two supported choices.
                    try { Save(rootDirectory, settings); } catch { }
                }
                return settings;
            }
        }
        catch
        {
            // Invalid or older settings should never prevent the application from starting.
        }
        return new AppSettings();
    }

    private static string NormalizeThemeMode(string? mode) =>
        string.Equals(mode, "Dark", StringComparison.OrdinalIgnoreCase) ? "Dark" : "Light";

    private static string NormalizeDualModelKey(string? key, string? legacyId)
    {
        var value = key?.Trim();
        if (!string.IsNullOrWhiteSpace(value) && value.Contains("::", StringComparison.Ordinal)) return value;
        var legacy = string.IsNullOrWhiteSpace(value) ? legacyId?.Trim() : value;
        return string.IsNullOrWhiteSpace(legacy) ? "" : $"faster-whisper::{legacy}";
    }

    public static void Save(string rootDirectory, AppSettings settings)
    {
        settings.ThemeMode = NormalizeThemeMode(settings.ThemeMode);
        settings.GlobalHotkeyShortcut = GlobalHotkeySupport.NormalizeShortcut(settings.GlobalHotkeyShortcut);
        settings.AutoLanguageStrategy = AutoLanguageText.NormalizeForSettings(settings.AutoLanguageStrategy);
        settings.TranscriptionLanguagePreference = AutoLanguageText.NormalizeTranscriptionLanguagePreference(settings.TranscriptionLanguagePreference);
        settings.AudioProfile = AudioProfilePolicy.Normalize(settings.AudioProfile);
        settings.AutoMixedPersianModelKey = NormalizeDualModelKey(settings.AutoMixedPersianModelKey, settings.AutoMixedPersianModelId);
        settings.AutoMixedEnglishModelKey = NormalizeDualModelKey(settings.AutoMixedEnglishModelKey, settings.AutoMixedEnglishModelId);
        settings.LocalDictionaryEntries = LocalDictionaryText.NormalizeEntries(settings.LocalDictionaryEntries);
        Directory.CreateDirectory(rootDirectory);
        var path = Path.Combine(rootDirectory, "settings.json");
        var temporaryPath = path + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(settings, JsonOptions));
        File.Move(temporaryPath, path, overwrite: true);
    }
}
