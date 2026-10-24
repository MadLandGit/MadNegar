using System.Globalization;
using System.Text.RegularExpressions;

namespace MadNegar;

/// <summary>Validation helpers for locally imported audio and engine-generated subtitle files.</summary>
internal static class SubtitleExportSupport
{
    private static readonly Regex SrtTimeLine = new(
        @"^(?<start>\d{2,}:\d{2}:\d{2},\d{3})[ \t]+-->[ \t]+(?<end>\d{2,}:\d{2}:\d{2},\d{3})[ \t]*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex VttTimeLine = new(
        @"^(?<start>(?:\d{2,}:)?\d{2}:\d{2}\.\d{3})[ \t]+-->[ \t]+(?<end>(?:\d{2,}:)?\d{2}:\d{2}\.\d{3})(?:[ \t]+.*)?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static bool IsSupportedAudioPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        return Path.GetExtension(path).ToLowerInvariant() is ".wav" or ".mp3" or ".aif" or ".aiff";
    }

    public static bool IsSupportedSubtitleFormat(string? format) =>
        string.Equals(format, "srt", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(format, "vtt", StringComparison.OrdinalIgnoreCase);

    public static bool IsValidSubtitle(string? format, string? content, out string reason)
    {
        reason = "";
        if (!IsSupportedSubtitleFormat(format))
        {
            reason = "The subtitle format is not supported. Choose SRT or WebVTT.";
            return false;
        }
        if (string.IsNullOrWhiteSpace(content))
        {
            reason = "The speech engine produced an empty subtitle file.";
            return false;
        }

        var normalized = content.TrimStart('\uFEFF').Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        var isVtt = string.Equals(format, "vtt", StringComparison.OrdinalIgnoreCase);
        var lines = normalized.Split('\n');
        if (isVtt)
        {
            var header = lines.Length > 0 ? lines[0].Trim() : string.Empty;
            if (!string.Equals(header, "WEBVTT", StringComparison.Ordinal) &&
                !header.StartsWith("WEBVTT ", StringComparison.Ordinal) &&
                !header.StartsWith("WEBVTT\t", StringComparison.Ordinal))
            {
                reason = "The output does not contain a valid WEBVTT header.";
                return false;
            }
        }

        var cueCount = 0;
        var timingRegex = isVtt ? VttTimeLine : SrtTimeLine;
        for (var index = 0; index < lines.Length; index++)
        {
            var currentLine = lines[index].Trim();
            var match = timingRegex.Match(currentLine);
            if (!match.Success)
            {
                // Do not silently ignore a malformed time-looking cue if another valid cue exists.
                if (currentLine.Length > 0 && char.IsAsciiDigit(currentLine[0]) && currentLine.Contains("-->", StringComparison.Ordinal))
                {
                    reason = "The subtitle contains a malformed timestamp line.";
                    return false;
                }
                continue;
            }
            if (!TryParseTimestamp(match.Groups["start"].Value, isVtt, out var start) ||
                !TryParseTimestamp(match.Groups["end"].Value, isVtt, out var end) || end < start)
            {
                reason = "The subtitle contains an invalid or reversed timestamp.";
                return false;
            }

            var hasText = false;
            for (var next = index + 1; next < lines.Length && !string.IsNullOrWhiteSpace(lines[next]); next++)
            {
                // SRT/VTT metadata may follow the timing line, but a cue must contain visible text.
                if (!lines[next].Trim().StartsWith("NOTE", StringComparison.OrdinalIgnoreCase) &&
                    !lines[next].Trim().StartsWith("STYLE", StringComparison.OrdinalIgnoreCase))
                {
                    hasText = true;
                    break;
                }
            }
            if (!hasText)
            {
                reason = "The subtitle contains a cue with no text.";
                return false;
            }
            cueCount++;
        }

        if (cueCount == 0)
        {
            reason = "No valid timestamped subtitle cues were found.";
            return false;
        }
        return true;
    }

    /// <summary>Applies the same local Persian cleanup and dictionary rules to cue text only; timestamps and headers are preserved.</summary>
    public static string ApplyCueTextProcessing(
        string content,
        string format,
        bool normalizeYeh,
        bool normalizeKaf,
        bool normalizeHalfSpaces,
        bool normalizePunctuation,
        IReadOnlyList<LocalDictionaryEntry> dictionaryEntries,
        string? spokenLanguage)
    {
        if (!IsSupportedSubtitleFormat(format)) throw new ArgumentException("Subtitle format must be srt or vtt.", nameof(format));
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(dictionaryEntries);

        var isVtt = string.Equals(format, "vtt", StringComparison.OrdinalIgnoreCase);
        var timingRegex = isVtt ? VttTimeLine : SrtTimeLine;
        var normalizedNewlines = content.TrimStart('\uFEFF').Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        var output = new List<string>();
        var block = new List<string>();
        foreach (var line in normalizedNewlines.Split('\n'))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                output.AddRange(ProcessCueBlock(block, timingRegex, normalizeYeh, normalizeKaf,
                    normalizeHalfSpaces, normalizePunctuation, dictionaryEntries, spokenLanguage));
                block.Clear();
                output.Add(string.Empty);
            }
            else
            {
                block.Add(line);
            }
        }
        output.AddRange(ProcessCueBlock(block, timingRegex, normalizeYeh, normalizeKaf,
            normalizeHalfSpaces, normalizePunctuation, dictionaryEntries, spokenLanguage));

        return string.Join("\r\n", output).TrimEnd('\r', '\n') + "\r\n";
    }

    private static IEnumerable<string> ProcessCueBlock(
        IReadOnlyList<string> block,
        Regex timingRegex,
        bool normalizeYeh,
        bool normalizeKaf,
        bool normalizeHalfSpaces,
        bool normalizePunctuation,
        IReadOnlyList<LocalDictionaryEntry> dictionaryEntries,
        string? spokenLanguage)
    {
        if (block.Count == 0) return Array.Empty<string>();
        var timingIndex = -1;
        for (var index = 0; index < block.Count; index++)
        {
            if (timingRegex.IsMatch(block[index].Trim()))
            {
                timingIndex = index;
                break;
            }
        }
        if (timingIndex < 0 || timingIndex >= block.Count - 1) return block.ToArray();

        // Keep cue number, timing line and any metadata untouched; only transform visible cue text.
        var result = block.Take(timingIndex + 1).ToList();
        var cueText = string.Join("\n", block.Skip(timingIndex + 1));
        cueText = PersianTextNormalizer.Normalize(cueText, normalizeYeh, normalizeKaf, normalizeHalfSpaces, normalizePunctuation);
        cueText = LocalDictionaryText.Apply(cueText, dictionaryEntries, spokenLanguage);
        result.AddRange(cueText.Split('\n'));
        return result;
    }

    private static bool TryParseTimestamp(string value, bool isVtt, out TimeSpan result)
    {
        result = TimeSpan.Zero;
        var normalized = value.Replace(',', '.');
        var parts = normalized.Split(':');
        if (parts.Length is not (2 or 3) || (!isVtt && parts.Length != 3)) return false;

        var hour = 0;
        var minutePart = parts.Length == 3 ? parts[1] : parts[0];
        var secondPart = parts.Length == 3 ? parts[2] : parts[1];
        if (parts.Length == 3 && !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out hour)) return false;
        if (!int.TryParse(minutePart, NumberStyles.None, CultureInfo.InvariantCulture, out var minute)) return false;
        var secondBits = secondPart.Split('.');
        if (secondBits.Length != 2 || !int.TryParse(secondBits[0], NumberStyles.None, CultureInfo.InvariantCulture, out var seconds) ||
            !int.TryParse(secondBits[1], NumberStyles.None, CultureInfo.InvariantCulture, out var milliseconds)) return false;
        if (hour < 0 || minute is < 0 or > 59 || seconds is < 0 or > 59 || milliseconds is < 0 or > 999) return false;
        result = TimeSpan.FromHours(hour) + TimeSpan.FromMinutes(minute) + TimeSpan.FromSeconds(seconds) + TimeSpan.FromMilliseconds(milliseconds);
        return true;
    }
}
