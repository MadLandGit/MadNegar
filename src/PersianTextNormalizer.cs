using System.Text;
using System.Text.RegularExpressions;

namespace MadNegar;

/// <summary>
/// Small, deterministic Persian text cleanups. It deliberately avoids rewriting URLs,
/// e-mail addresses, and inline/fenced code. This is not an AI paraphraser.
/// </summary>
internal static class PersianTextNormalizer
{
    private static readonly Regex ProtectedSegments = new(
        @"```[\s\S]*?```|`[^`\r\n]*`|https?://[^\s<>]+|www\.[^\s<>]+|[\w.+-]+@[\w.-]+\.[A-Za-z]{2,}",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex CommonSuffixes = new(
        @"(?<word>[\u0600-\u06FF\u200C\u064B-\u065F]+)[ \t]+(?<suffix>هایی|های|ها|ترین|تر)(?=$|[ \t\r\n،؛؟!,.])",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex MiPrefix = new(
        @"(?<![\u0600-\u06FF])(?<prefix>ن?می)[ \t]+(?<first>[\u0600-\u06FF])",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex WhitespaceBeforePersianPunctuation = new(
        @"[ \t]+(?=[،؛؟])", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex WhitespaceAfterPersianPunctuation = new(
        @"([،؛؟])(?=[^\s)\]}>])", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static string Normalize(
        string text,
        bool normalizeYeh,
        bool normalizeKaf,
        bool normalizeHalfSpaces,
        bool normalizePunctuation)
    {
        if (string.IsNullOrEmpty(text)
            || (!normalizeYeh && !normalizeKaf && !normalizeHalfSpaces && !normalizePunctuation))
            return text;

        var output = new StringBuilder(text.Length + 8);
        var previousEnd = 0;
        foreach (Match protectedMatch in ProtectedSegments.Matches(text))
        {
            if (protectedMatch.Index > previousEnd)
                output.Append(NormalizePlainSegment(text[previousEnd..protectedMatch.Index],
                    normalizeYeh, normalizeKaf, normalizeHalfSpaces, normalizePunctuation));
            output.Append(protectedMatch.Value);
            previousEnd = protectedMatch.Index + protectedMatch.Length;
        }

        if (previousEnd < text.Length)
            output.Append(NormalizePlainSegment(text[previousEnd..],
                normalizeYeh, normalizeKaf, normalizeHalfSpaces, normalizePunctuation));

        return output.ToString();
    }

    private static string NormalizePlainSegment(
        string value,
        bool normalizeYeh,
        bool normalizeKaf,
        bool normalizeHalfSpaces,
        bool normalizePunctuation)
    {
        if (normalizeYeh) value = value.Replace('\u064A', '\u06CC'); // Arabic Yeh -> Persian Yeh
        if (normalizeKaf) value = value.Replace('\u0643', '\u06A9'); // Arabic Kaf -> Persian Kaf

        if (normalizeHalfSpaces)
        {
            value = MiPrefix.Replace(value, match =>
                match.Groups["prefix"].Value + '\u200C' + match.Groups["first"].Value);
            value = CommonSuffixes.Replace(value, match =>
                match.Groups["word"].Value + '\u200C' + match.Groups["suffix"].Value);
        }

        if (normalizePunctuation && ContainsPersianText(value))
        {
            value = WhitespaceBeforePersianPunctuation.Replace(value, string.Empty);
            value = WhitespaceAfterPersianPunctuation.Replace(value, "$1 ");
        }

        return value;
    }

    private static bool ContainsPersianText(string value) => value.Any(ch =>
        (ch >= '\u0600' && ch <= '\u06FF') || ch == '\u200C' || ch == '\u200F');
}
