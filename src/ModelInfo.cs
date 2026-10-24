namespace MadNegar;

internal sealed record ModelInfo(
    string Id,
    string ApproxSize,
    bool EnglishOnly = false)
{
    public string FileName => $"ggml-{Id}.bin";

    public string DownloadUrl =>
        $"https://huggingface.co/ggerganov/whisper.cpp/resolve/main/{FileName}";

    public override string ToString()
    {
        var languageLabel = EnglishOnly ? "English only" : "Multilingual";
        return $"{Id}  ·  {ApproxSize}  ·  {languageLabel}";
    }
}
