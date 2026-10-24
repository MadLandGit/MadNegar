namespace MadNegar;

/// <summary>Unified UI entry for downloaded models across providers.</summary>
internal sealed record SpeechModelChoice(ModelInfo? WhisperModel, ProviderModelInfo? ProviderModel)
{
    public bool IsWhisper => WhisperModel is not null;
    public bool IsVosk => ProviderModel?.Provider == "Vosk";
    public bool IsDiba => ProviderModel?.Provider == "Diba-STT";
    public bool IsFasterWhisper => ProviderModel?.Provider == "faster-whisper";
    public bool UsesVoskRuntime => IsVosk || IsDiba;
    public bool IsSupportedForFinal => IsWhisper || UsesVoskRuntime || IsFasterWhisper;
    // Live preview supports Whisper.cpp, Vosk-compatible folders, and experimental chunk-based
    // faster-whisper preview. faster-whisper starts a file adapter per chunk, so it can be slower;
    // callers must verify that its local runtime is already installed before recording starts.
    public bool IsSupportedForLive => IsWhisper || UsesVoskRuntime || IsFasterWhisper;
    public string Id => WhisperModel?.Id ?? ProviderModel!.Id;
    public string Provider => WhisperModel is not null ? "Whisper.cpp" : ProviderModel!.Provider;
    public string ApproxSize => WhisperModel?.ApproxSize ?? ProviderModel!.ApproxSize;
    public string Languages => WhisperModel is not null
        ? (WhisperModel.EnglishOnly ? "English only" : "Persian + English / multilingual")
        : ProviderModel!.Languages;
    public bool EnglishOnly => WhisperModel?.EnglishOnly ??
        (ProviderModel is not null && ProviderModel.Languages.Contains("English", StringComparison.OrdinalIgnoreCase) &&
         !ProviderModel.Languages.Contains("Persian", StringComparison.OrdinalIgnoreCase));
    public string Key => WhisperModel is not null ? $"Whisper.cpp::{WhisperModel.Id}" : $"{ProviderModel!.Provider}::{ProviderModel.Id}";
    public string DisplayName => WhisperModel?.Id ?? ProviderModel!.DisplayName;
    public override string ToString() => IsSupportedForFinal
        ? $"{DisplayName}  ·  {Provider}"
        : $"{DisplayName}  ·  {Provider} (runtime not available yet)";
}

internal sealed record ModelSelectionOption(string Key, string Label)
{
    public override string ToString() => Label;
}

internal sealed record DualModelChoiceOption(string Key, string Label)
{
    public override string ToString() => Label;
}
