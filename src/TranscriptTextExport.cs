using System.Text;

namespace MadNegar;

internal static class TranscriptTextExport
{
    public static void WriteUtf8(string path, string text)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("An export path is required.", nameof(path));
        if (string.IsNullOrWhiteSpace(text)) throw new InvalidDataException("There is no transcript text to export.");
        File.WriteAllText(path, text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
    }
}
