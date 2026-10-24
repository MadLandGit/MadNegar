namespace MadNegar;

internal enum ProviderDownloadKind
{
    WhisperBinary,
    HuggingFaceSnapshot,
    ZipArchive
}

internal sealed record ProviderModelInfo(
    string Id,
    string DisplayName,
    string Provider,
    string ApproxSize,
    string Languages,
    string License,
    ProviderDownloadKind DownloadKind,
    string SourceUrl,
    string DownloadUrl,
    ModelInfo? WhisperModel = null,
    string? HuggingFaceRepo = null,
    string? HuggingFaceFolderPrefix = null,
    bool HuggingFaceRootModelFilesOnly = false,
    string? Description = null,
    string? AlternativeDownloadUrl = null)
{
    public bool IsWhisper => DownloadKind == ProviderDownloadKind.WhisperBinary && WhisperModel is not null;
    public bool IsProviderAsset => !IsWhisper;
    public override string ToString() => DisplayName;
}

internal static class ProviderModelCatalog
{
    public static readonly string[] Providers =
    [
        "Whisper.cpp",
        "faster-whisper",
        "Diba-STT",
        "Vosk",
        "Oncilla / Nemotron (Persian)"
    ];

    public static readonly List<ProviderModelInfo> All = Build();

    private static List<ProviderModelInfo> Build()
    {
        var items = new List<ProviderModelInfo>();

        foreach (var model in ModelCatalog.Models)
        {
            items.Add(new ProviderModelInfo(
                model.Id, model.Id, "Whisper.cpp", model.ApproxSize,
                model.EnglishOnly ? "English" : "Persian + English / multilingual",
                "MIT / model-specific terms", ProviderDownloadKind.WhisperBinary,
                "https://huggingface.co/ggerganov/whisper.cpp/tree/main",
                model.DownloadUrl, model,
                Description: model.EnglishOnly
                    ? "English-only Whisper model; not suitable for Persian."
                    : "Multilingual Whisper.cpp model. This is the currently supported engine in MadNegar."));
        }

        foreach (var size in new[]
        {
            (Id: "tiny", Size: "~78 MB"),
            (Id: "base", Size: "~150 MB"),
            (Id: "small", Size: "~490 MB"),
            (Id: "medium", Size: "~1.5 GB"),
            (Id: "large-v3", Size: "~3.1 GB"),
            (Id: "large-v3-turbo", Size: "~1.6 GB")
        })
        {
            var repo = size.Id == "large-v3-turbo"
                ? "mobiuslabsgmbh/faster-whisper-large-v3-turbo"
                : $"Systran/faster-whisper-{size.Id}";
            items.Add(new ProviderModelInfo(
                size.Id, $"{size.Id} (CTranslate2)", "faster-whisper", size.Size,
                "Persian + English / multilingual", "MIT (engine); model terms may vary",
                ProviderDownloadKind.HuggingFaceSnapshot,
                $"https://huggingface.co/{repo}",
                $"https://huggingface.co/{repo}", HuggingFaceRepo: repo,
                HuggingFaceRootModelFilesOnly: true,
                Description: "CTranslate2 model files. MadNegar connects these local files to faster-whisper; its private Python runtime is installed on first use."));
        }

        AddDiba(items, "fa-small-0.42", "~53 MB model", "Persian · real-time", "fa-small-0.42/");
        AddDiba(items, "fa-0.42", "~1.6 GB", "Persian", "fa-0.42/");
        AddDiba(items, "fa-small-0.5", "~60 MB model", "Persian · real-time", "fa-small-0.5/");
        AddDiba(items, "fa-0.5", "~1 GB", "Persian", "fa-0.5/");

        AddVosk(items, "vosk-model-small-fa-0.42", "~53 MB", "Persian", "https://alphacephei.com/vosk/models/vosk-model-small-fa-0.42.zip", "https://huggingface.co/localstack/vosk-models/resolve/main/vosk-model-small-fa-0.42.zip");
        AddVosk(items, "vosk-model-fa-0.42", "~1.6 GB", "Persian", "https://alphacephei.com/vosk/models/vosk-model-fa-0.42.zip");
        AddVosk(items, "vosk-model-small-fa-0.5", "~60 MB", "Persian", "https://alphacephei.com/vosk/models/vosk-model-small-fa-0.5.zip", "https://huggingface.co/rhasspy/vosk-models/resolve/main/fa/vosk-model-small-fa-0.5.zip");
        AddVosk(items, "vosk-model-fa-0.5", "~1 GB", "Persian", "https://alphacephei.com/vosk/models/vosk-model-fa-0.5.zip", "https://huggingface.co/Ehsansouri/my-vosk-persian-model/resolve/main/vosk-model-fa-0.5.zip");
        AddVosk(items, "vosk-model-small-en-us-0.15", "~40 MB", "English (US)", "https://alphacephei.com/vosk/models/vosk-model-small-en-us-0.15.zip", "https://huggingface.co/localstack/vosk-models/resolve/main/vosk-model-small-en-us-0.15.zip");
        AddVosk(items, "vosk-model-en-us-0.22", "~1.8 GB", "English (US)", "https://alphacephei.com/vosk/models/vosk-model-en-us-0.22.zip", "https://huggingface.co/localstack/vosk-models/resolve/main/vosk-model-en-us-0.22.zip");

        const string oncillaRepo = "mehdi-hf/nemotron-asr-streaming-farsi";
        items.Add(new ProviderModelInfo(
            "nemotron-asr-streaming-farsi", "Nemotron ASR Streaming (Persian)",
            "Oncilla / Nemotron (Persian)", "Large model · size checked at download",
            "Persian only", "OpenMDW-1.1 (check terms before redistribution/use)",
            ProviderDownloadKind.HuggingFaceSnapshot,
            $"https://huggingface.co/{oncillaRepo}",
            $"https://huggingface.co/{oncillaRepo}", HuggingFaceRepo: oncillaRepo,
            HuggingFaceRootModelFilesOnly: true,
            Description: "Persian-only streaming model. This needs its own Transformers/NeMo runtime and is not yet wired into MadNegar transcription."));

        return items;
    }

    private static void AddDiba(List<ProviderModelInfo> items, string id, string size, string languages, string prefix)
    {
        const string repo = "Dibachain/Diba-STT";
        items.Add(new ProviderModelInfo(
            id, id, "Diba-STT", size, languages, "Apache-2.0",
            ProviderDownloadKind.HuggingFaceSnapshot,
            $"https://huggingface.co/{repo}/tree/main/{prefix.TrimEnd('/')}",
            $"https://huggingface.co/{repo}/tree/main/{prefix.TrimEnd('/')}",
            HuggingFaceRepo: repo, HuggingFaceFolderPrefix: prefix,
            Description: "Persian-only offline/streaming model. Diba publishes Kaldi/Vosk-format model folders; MadNegar uses its local Vosk decoder with the downloaded model."));
    }

    private static void AddVosk(List<ProviderModelInfo> items, string id, string size, string languages, string downloadUrl, string? alternativeDownloadUrl = null)
    {
        items.Add(new ProviderModelInfo(
            id, id, "Vosk", size, languages, "Apache-2.0",
            ProviderDownloadKind.ZipArchive,
            "https://alphacephei.com/vosk/models", downloadUrl,
            Description: "Vosk model archive. Downloaded models can be selected for final transcription and use the local Vosk .NET runtime.",
            AlternativeDownloadUrl: alternativeDownloadUrl));
    }
}
