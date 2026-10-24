using System.Text;
using System.Text.RegularExpressions;

namespace MadNegar;

/// <summary>Text-only helpers for Auto language strategy selection and overlap-safe chunk merging.</summary>
internal static class AutoLanguageText
{
    public const string Balanced = "balanced";
    public const string PreferPersian = "prefer-persian";
    public const string PreferEnglish = "prefer-english";
    public const string MixedSegments = "mixed-segments";
    public const string DualModelMixed = "dual-model-mixed";
    public const string PersianFirstPreference = "persian-first";
    public const string EnglishFirstPreference = "english-first";

    public static string NormalizeTranscriptionLanguagePreference(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        EnglishFirstPreference => EnglishFirstPreference,
        _ => PersianFirstPreference
    };

    public static bool IsPersianFirstPreference(string? value) =>
        NormalizeTranscriptionLanguagePreference(value) == PersianFirstPreference;

    public static string NormalizeStrategy(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        PreferPersian => PreferPersian,
        PreferEnglish => PreferEnglish,
        MixedSegments => MixedSegments,
        DualModelMixed => DualModelMixed,
        _ => Balanced
    };

    /// <summary>Normalizes settings for the simplified UI: legacy preference modes now map to the newer Persian-first dual-model workflow.</summary>
    public static string NormalizeForSettings(string? value) => NormalizeStrategy(value) switch
    {
        PreferPersian or PreferEnglish or MixedSegments => DualModelMixed,
        DualModelMixed => DualModelMixed,
        _ => Balanced
    };

    /// <summary>Only an explicit Speak Language choice locks the preview decoder; Auto stays decoder-driven.</summary>
    public static string? ResolveLivePreviewLanguage(string? spokenLanguage, string? strategy)
    {
        var language = spokenLanguage?.Trim().ToLowerInvariant();
        if (language is "fa" or "persian") return "fa";
        if (language is "en" or "english") return "en";
        // A preference is not an instruction to decode a complete mixed-language chunk as one language.
        return null;
    }

    public static bool ContainsPersianScript(string? value) => !string.IsNullOrEmpty(value) && value.Any(ch =>
        (ch >= '\u0600' && ch <= '\u06FF') ||
        (ch >= '\u0750' && ch <= '\u077F') ||
        (ch >= '\u08A0' && ch <= '\u08FF') ||
        (ch >= '\uFB50' && ch <= '\uFDFF') ||
        (ch >= '\uFE70' && ch <= '\uFEFF'));

    public static int CountPersianLetters(string? value) => string.IsNullOrEmpty(value)
        ? 0
        : value.Count(ch => char.IsLetter(ch) &&
            ((ch >= '\u0600' && ch <= '\u06FF') ||
             (ch >= '\u0750' && ch <= '\u077F') ||
             (ch >= '\u08A0' && ch <= '\u08FF') ||
             (ch >= '\uFB50' && ch <= '\uFDFF') ||
             (ch >= '\uFE70' && ch <= '\uFEFF')));

    public static int CountLatinLetters(string? value) => string.IsNullOrEmpty(value)
        ? 0
        : value.Count(ch => char.IsLetter(ch) && ((ch >= 'A' && ch <= 'Z') || (ch >= 'a' && ch <= 'z')));

    public static bool ShouldTryPersianPreferredPass(string? automaticTranscript) =>
        LooksLikeRomanizedPersian(automaticTranscript);

    /// <summary>
    /// Preference-aware retry gate for Auto. Clear English is not forced to Persian; recognizable
    /// Finglish is retried even when Whisper mistakenly labels it English, while unknown Latin-only
    /// output is retried only when the language detector did not confidently choose either language.
    /// </summary>
    public static bool ShouldTryPersianPreferredPass(string? automaticTranscript, string? detectedLanguage)
    {
        if (LooksLikeRomanizedPersian(automaticTranscript)) return true;
        if (ContainsPersianScript(automaticTranscript) || CountLatinLetters(automaticTranscript) < 8) return false;
        var language = detectedLanguage?.Trim().ToLowerInvariant();
        return string.IsNullOrWhiteSpace(language) ||
               language is not ("en" or "english" or "fa" or "persian");
    }

    public static bool LooksLikeRomanizedPersian(string? text)
    {
        if (string.IsNullOrWhiteSpace(text) || ContainsPersianScript(text)) return false;
        var letters = text.Count(char.IsLetter);
        var latinLetters = CountLatinLetters(text);
        if (letters < 4 || latinLetters < 4 || latinLetters * 100 / Math.Max(1, letters) < 70) return false;

        var words = Regex.Split(text.ToLowerInvariant(), "[^a-z0-9]+")
            .Where(word => word.Length > 0)
            .ToHashSet(StringComparer.Ordinal);
        string[] distinctive =
        [
            "salam", "salaam", "chetori", "chetor", "chejuri", "chejory", "mikham", "mikhastam",
            "mikhay", "mishe", "mitoonam", "nemitoonam", "nemidunam", "nemidoonam", "khoobam",
            "khoobi", "khubam", "khobi", "khodafez", "baleh", "lotfan", "emrooz", "emruz",
            "khune", "khoone", "chera", "doroste", "dorost", "fekr", "daram", "dari", "hastam",
            "nistam", "khodam", "baram", "behem", "mamnoon", "khoshhal", "mikoni", "khabar",
            "emrooz", "emruz", "bayad", "konam", "konim", "konid", "kardan", "kardam", "kardi",
            "karde", "kard", "bokon", "begu", "begoo", "bego", "beshin", "berim", "beram", "beran",
            "inja", "oonja", "unja", "koja", "chon", "chonke", "vaghti", "alan", "hala", "ghablan",
            "badesh", "khahesh", "injoori", "hamishe", "hamechi", "migan", "migam", "mige", "migim",
            "mishan", "misham", "nemishe", "nemikham", "nemikhad", "nemikonam", "nemikonid", "midunam",
            "midonam", "azmayesh", "azmoon", "zabane", "zaban", "goftam", "gofti", "goftan", "shode",
            "misheh", "ast", "hast", "hastesh", "nisti", "nist", "hastin", "khuneh", "dige", "digar",
            "haminjoori", "baraye", "bara", "baad", "sobh", "shab"
        ];
        return distinctive.Any(words.Contains) || (words.Contains("farsi") && words.Contains("man"));
    }

    public static bool ShouldTryEnglishPreferredPass(string? automaticTranscript)
    {
        var persian = CountPersianLetters(automaticTranscript);
        var latin = CountLatinLetters(automaticTranscript);
        var letters = persian + latin;
        return letters >= 8 && persian >= 6 && persian * 100 / Math.Max(1, letters) >= 70;
    }

    /// <summary>Use the engine's language label only when the transcript script clearly conflicts with it.</summary>
    public static bool ShouldRetryEnglishForScriptMismatch(string? transcript, string? detectedLanguage)
    {
        var normalizedLanguage = detectedLanguage?.Trim().ToLowerInvariant();
        return (normalizedLanguage is "fa" or "persian")
            && CountPersianLetters(transcript) == 0
            && CountLatinLetters(transcript) >= 4
            && !LooksLikeRomanizedPersian(transcript);
    }

    public static bool ShouldRetryPersianForScriptMismatch(string? transcript, string? detectedLanguage)
    {
        var normalizedLanguage = detectedLanguage?.Trim().ToLowerInvariant();
        var persian = CountPersianLetters(transcript);
        var latin = CountLatinLetters(transcript);
        var total = persian + latin;
        // Do not force a whole-utterance Persian pass when meaningful Latin content is
        // present alongside Persian. This protects English technical terms in code-switched
        // speech (for example: "این API را باز کن") from being transliterated or replaced.
        return (normalizedLanguage is "en" or "english")
            && !HasMeaningfulMixedScript(transcript)
            && total >= 8 && persian >= 6 && persian * 100 / Math.Max(1, total) >= 70;
    }


    /// <summary>
    /// Protect ordinary code-switching from a whole-utterance language retry.
    /// A retry is only appropriate when one script clearly dominates or the engine label
    /// conflicts with the script; meaningful Persian and Latin content in the same phrase
    /// should normally be left intact for segment-level Mixed mode.
    /// </summary>
    public static bool HasMeaningfulMixedScript(string? transcript)
    {
        var persian = CountPersianLetters(transcript);
        var latin = CountLatinLetters(transcript);
        var total = persian + latin;
        if (total < 10 || persian < 3 || latin < 3) return false;
        return persian * 100 / total >= 12 && latin * 100 / total >= 12;
    }

    private static readonly (string Canonical, string[] Aliases)[] EnglishTechnicalTermAliases =
    [
        ("Visual Studio Code", ["ویژوال استودیو کد", "ویژوال‌استودیو کد", "ویژوال استودیوکد"]),
        ("Visual Studio", ["ویژوال استودیو", "ویژوال‌استودیو", "ویژوال استادیو", "ویژوال‌استادیو"]),
        ("faster-whisper", ["فستر ویسپر", "فستر‌ویسپر", "فسترویسپر"]),
        ("JavaScript", ["جاوا اسکریپت", "جاوااسکریپت", "جاوا‌اسکریپت"]),
        ("TypeScript", ["تایپ اسکریپت", "تایپ‌اسکریپت"]),
        ("PowerShell", ["پاور شل", "پاورشل", "پاور‌شل"]),
        ("PowerPoint", ["پاور پوینت", "پاورپوینت", "پاور‌پوینت"]),
        ("Node.js", ["نود جی اس", "نود‌جی‌اس", "نود جی‌اس"]),
        (".NET Core", ["دات نت کور", "دات‌نت کور", "دات نت‌کور", "دات‌نت‌کور"]),
        (".NET", ["دات نت", "دات‌نت"]),
        ("VS Code", ["وی اس کد", "وی‌اس کد", "وی اس‌کد", "وی‌اس‌کد"]),
        ("ChatGPT", ["چت جی پی تی", "چت‌جی‌پی‌تی", "چت جی‌پی‌تی", "چت‌جی پی تی"]),
        ("GitHub", ["گیت هاب", "گیت‌هاب", "گیت‌ هاب"]),
        ("GitLab", ["گیت لب", "گیت‌لب"]),
        ("Java", ["جاوا"]),
        ("Python", ["پایتون"]),
        ("Docker", ["داکر"]),
        ("Kubernetes", ["کوبرنتیز", "کوبِرنتیس", "کابرنتیز"]),
        ("React", ["ری اکت", "ری‌اکت", "ریاکت"]),
        ("Vue.js", ["ویو جی اس", "ویو‌جی‌اس", "ویو جی‌اس"]),
        ("Git", ["گیت"]),
        ("API", ["ای پی آی", "ای‌پی‌آی", "ای پی‌آی", "ای‌ پی آی", "ای پی ای"]),
        ("C++", ["سی پلاس پلاس", "سی‌پلاس‌پلاس"]),
        ("C#", ["سی شارپ", "سی‌شارپ"]),
        ("JSON", ["جی سان", "جی‌سان", "جیسون"]),
        ("XML", ["ایکس ام ال", "ایکس‌ام‌ال"]),
        ("HTML", ["اچ تی ام ال", "اچ‌تی‌ام‌ال"]),
        ("CSS", ["سی اس اس", "سی‌اس‌اس"]),
        ("SQL", ["اس کیو ال", "اس‌کیو‌ال"]),
        ("Notepad", ["نوت پد", "نوت‌پد"]),
        ("Windows", ["ویندوز"]),
        ("Whisper", ["ویسپر"]),
        ("NuGet", ["نیو گت", "نیوگت"]),
        ("FFmpeg", ["اف اف امپگ", "اف‌اف‌امپگ"]),
        ("OpenAI", ["اوپن ای آی", "اوپن‌ای‌آی"]),
        ("Google Drive", ["گوگل درایو", "گوگل‌درایو", "گوگلدرایو"]),
        ("Upload", ["آپلود", "آپ‌لود", "اپلود"]),
        ("Download", ["دانلود", "دان‌لود"]),
        ("Update", ["آپدیت", "آپ‌دیت", "اپدیت"]),
        ("Install", ["اینستال", "این‌استال"]),
        ("Login", ["لاگین", "لاگ این", "لاگ‌این"]),
        ("Logout", ["لاگ‌اوت", "لاگ اوت", "لاگوت"]),
        ("build", ["بیلد", "بایلد"]),
        ("debug", ["دیباگ"]),
        ("compile", ["کامپایل"]),
        ("deploy", ["دیپلوی"]),
        ("runtime", ["ران تایم", "ران‌تایم"]),
    ];

    private static readonly Regex ProtectedAutoTextRegex = new(
        @"https?://\S+|www\.\S+|[\w.+-]+@[\w.-]+\.[A-Za-z]{2,}|`[^`]*`",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>
    /// Converts a conservative list of common technical names spoken in English but
    /// rendered phonetically with Persian letters back to their canonical Latin spelling.
    /// This does not translate surrounding text and protects URLs, email and inline code.
    /// </summary>
    public static string RestoreEnglishTechnicalTerms(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return text ?? string.Empty;

        string NormalizeUnprotected(string part)
        {
            var result = part;
            foreach (var (canonical, aliases) in EnglishTechnicalTermAliases)
            {
                foreach (var alias in aliases.OrderByDescending(value => value.Length))
                {
                    var words = alias.Replace('\u200c', ' ').Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (words.Length == 0) continue;
                    var separator = @"[\s\u200C]+";
                    // Treat ZWNJ like a word character at the outer boundary so an alias cannot rewrite only the first half of a Persian compound.
                    var pattern = @"(?<![\p{L}\p{N}_\u200C])" + string.Join(separator, words.Select(Regex.Escape)) + @"(?![\p{L}\p{N}_\u200C])";
                    result = Regex.Replace(result, pattern, canonical, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                }
            }
            return result;
        }

        var matches = ProtectedAutoTextRegex.Matches(text);
        if (matches.Count == 0) return NormalizeUnprotected(text);

        var builder = new StringBuilder(text.Length + 16);
        var cursor = 0;
        foreach (Match match in matches)
        {
            builder.Append(NormalizeUnprotected(text[cursor..match.Index]));
            builder.Append(match.Value);
            cursor = match.Index + match.Length;
        }
        builder.Append(NormalizeUnprotected(text[cursor..]));
        return builder.ToString();
    }

    public static bool IsPersianCandidate(string? candidate, string? baseline)
    {
        var persian = CountPersianLetters(candidate);
        if (persian < 2) return false;
        var baselineLetters = CountPersianLetters(baseline) + CountLatinLetters(baseline);
        var candidateLetters = persian + CountLatinLetters(candidate);
        return candidateLetters >= Math.Max(4, (int)Math.Ceiling(baselineLetters * 0.45));
    }

    public static bool IsEnglishCandidate(string? candidate, string? baseline)
    {
        var latin = CountLatinLetters(candidate);
        var persian = CountPersianLetters(candidate);
        if (latin < 4 || persian > latin / 3) return false;
        var baselineLetters = CountPersianLetters(baseline) + CountLatinLetters(baseline);
        return latin >= Math.Max(4, (int)Math.Ceiling(baselineLetters * 0.45));
    }

    /// <summary>
    /// Joins overlapping segment transcripts and removes only exact normalized suffix/prefix word overlaps.
    /// It deliberately does not rewrite any word or guess missing text.
    /// </summary>
    public static string MergeSegmentTranscripts(IEnumerable<string?> segmentTranscripts)
    {
        var merged = new List<string>();
        foreach (var raw in segmentTranscripts)
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;
            var tokens = Regex.Split(raw.Trim(), @"\s+").Where(token => token.Length > 0).ToList();
            if (tokens.Count == 0) continue;

            var maxOverlap = Math.Min(18, Math.Min(merged.Count, tokens.Count));
            var overlap = 0;
            for (var candidate = maxOverlap; candidate >= 1; candidate--)
            {
                var matches = true;
                for (var index = 0; index < candidate; index++)
                {
                    var previous = NormalizeToken(merged[merged.Count - candidate + index]);
                    var next = NormalizeToken(tokens[index]);
                    if (previous.Length == 0 || next.Length == 0 ||
                        !string.Equals(previous, next, StringComparison.Ordinal))
                    {
                        matches = false;
                        break;
                    }
                }
                if (!matches) continue;
                overlap = candidate;
                break;
            }

            merged.AddRange(tokens.Skip(overlap));
        }

        return string.Join(" ", merged).Trim();
    }

    private static string NormalizeToken(string token) =>
        new(token.Normalize(NormalizationForm.FormC)
            .Where(char.IsLetterOrDigit)
            .Select(char.ToLowerInvariant)
            .ToArray());
}
