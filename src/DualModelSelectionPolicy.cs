namespace MadNegar;

/// <summary>Role and candidate policy for the experimental two-engine Auto transcription mode.</summary>
internal static class DualModelSelectionPolicy
{
    public static bool SupportsLanguageRole(SpeechModelChoice choice, string languageRole)
    {
        if (!choice.IsSupportedForFinal) return false;
        var languages = choice.Languages ?? string.Empty;
        var isPersianRole = languageRole.Equals("fa", StringComparison.OrdinalIgnoreCase) ||
                            languageRole.Equals("Persian", StringComparison.OrdinalIgnoreCase);
        var isEnglishRole = languageRole.Equals("en", StringComparison.OrdinalIgnoreCase) ||
                            languageRole.Equals("English", StringComparison.OrdinalIgnoreCase);
        if (!isPersianRole && !isEnglishRole) return false;
        if (isPersianRole && choice.EnglishOnly) return false;
        if (choice.IsFasterWhisper) return true; // all catalogued CTranslate2 choices are multilingual
        if (isPersianRole)
            return languages.Contains("Persian", StringComparison.OrdinalIgnoreCase) ||
                   languages.Contains("multilingual", StringComparison.OrdinalIgnoreCase);
        return languages.Contains("English", StringComparison.OrdinalIgnoreCase) ||
               languages.Contains("multilingual", StringComparison.OrdinalIgnoreCase);
    }

    public static string LegacyOrSavedKey(string? providerQualifiedKey, string? legacyId)
    {
        var key = providerQualifiedKey?.Trim();
        if (!string.IsNullOrWhiteSpace(key) && key.Contains("::", StringComparison.Ordinal)) return key;
        var id = string.IsNullOrWhiteSpace(key) ? legacyId?.Trim() : key;
        return string.IsNullOrWhiteSpace(id) ? string.Empty : $"faster-whisper::{id}";
    }

    /// <summary>
    /// Picks the best per-segment candidate. A genuinely mixed-script candidate wins; otherwise
    /// the scorer uses script fit, common language tokens, useful word count and transcript length.
    /// This is a heuristic, not a claim of word-level forced alignment.
    /// </summary>
    public static string ChooseCandidate(
        string? persianCandidate, string? englishCandidate,
        string? persianDetectedLanguage = null, string? englishDetectedLanguage = null,
        string? transcriptionLanguagePreference = null)
    {
        var fa = persianCandidate?.Trim() ?? string.Empty;
        var en = englishCandidate?.Trim() ?? string.Empty;
        if (fa.Length == 0) return en;
        if (en.Length == 0) return fa;

        var faPersianLetters = AutoLanguageText.CountPersianLetters(fa);
        var enPersianLetters = AutoLanguageText.CountPersianLetters(en);
        var faLatinLetters = AutoLanguageText.CountLatinLetters(fa);
        var enLatinLetters = AutoLanguageText.CountLatinLetters(en);
        var faMixed = AutoLanguageText.HasMeaningfulMixedScript(fa);
        var enMixed = AutoLanguageText.HasMeaningfulMixedScript(en);
        var faLanguage = NormalizeDetectedLanguage(persianDetectedLanguage);
        var enLanguage = NormalizeDetectedLanguage(englishDetectedLanguage);
        var faReportedEnglish = faLanguage == "en";
        var enReportedEnglish = enLanguage == "en";
        var faReportedPersian = faLanguage == "fa";
        var enReportedPersian = enLanguage == "fa";
        var faLooksEnglish = faLatinLetters >= 4 && faPersianLetters == 0;
        var enLooksEnglish = enLatinLetters >= 4 && enPersianLetters == 0;

        // A Persian-role decoder can translate a clear English chunk into Persian script and then
        // have a known technical token restored in Latin script. Require a strongly Persian-dominant
        // candidate plus English labels from both engines before treating that output as a translation.
        var faScriptLetters = Math.Max(1, faPersianLetters + faLatinLetters);
        var faPersianShare = (double)faPersianLetters / faScriptLetters;
        if (faReportedEnglish && faPersianLetters >= 3 && faPersianShare >= 0.78 &&
            !faMixed && enLooksEnglish && enReportedEnglish)
            return en;

        // A coherent mixed-script candidate otherwise remains strongest: it already preserves both the
        // Persian sentence and Latin technical terms, so don't replace it with a one-language paraphrase.
        if (faMixed) return fa;
        if (enMixed) return en;

        // Roles are preferences, not forced language labels. A genuinely Persian-detected base
        // remains Persian unless an already-mixed candidate above preserved both scripts.
        if (faReportedPersian && faPersianLetters >= 3) return fa;
        if (enReportedPersian && enPersianLetters >= 3 && faPersianLetters < 3) return en;

        // A role-labeled engine can still yield the most faithful candidate in the other language;
        // detector/script agreement takes priority.
        if (faReportedEnglish && faLooksEnglish && !enLooksEnglish) return fa;
        if (enReportedEnglish && enLooksEnglish && !faLooksEnglish) return en;
        if (faReportedEnglish && faLooksEnglish && enLooksEnglish && !enReportedEnglish) return fa;
        if (enReportedEnglish && enLooksEnglish && faLooksEnglish && !faReportedEnglish) return en;

        // Vosk/Diba don't expose per-chunk language confidence. In that case fall back to lexical
        // and script fit instead of blindly accepting every Persian-script output as correct.
        var faScore = CandidateScore(fa, "fa");
        var enScore = CandidateScore(en, "en");
        if (faReportedPersian) faScore += 1.8;
        if (faReportedEnglish) faScore -= 2.4;
        if (enReportedEnglish) enScore += 1.8;
        if (enReportedPersian) enScore -= 2.4;
        if (Math.Abs(faScore - enScore) >= 1.25) return faScore > enScore ? fa : en;

        // If the engines disagree only on writing system and neither has detected a language,
        // keep the candidate that preserves Persian script. English-first is not permission to
        // replace Persian speech with an English paraphrase; confident English evidence still wins above.
        if (faLanguage is null && enLanguage is null)
        {
            if (faPersianLetters >= 3 && enPersianLetters == 0) return fa;
            if (enPersianLetters >= 3 && faPersianLetters == 0) return en;
        }
        return fa.Length >= en.Length * 0.88 ? fa : en;
    }

    private static string? NormalizeDetectedLanguage(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "fa" or "persian" => "fa",
        "en" or "english" => "en",
        _ => null
    };

    private static double CandidateScore(string text, string role)
    {
        var persian = AutoLanguageText.CountPersianLetters(text);
        var latin = AutoLanguageText.CountLatinLetters(text);
        var tokens = System.Text.RegularExpressions.Regex.Split(text.ToLowerInvariant(), @"[^\p{L}\p{N}]+")
            .Where(token => token.Length > 0).ToArray();
        var uniqueTokens = tokens.Distinct(StringComparer.Ordinal).Count();
        var persianWords = new HashSet<string>(StringComparer.Ordinal)
        {
            "سلام", "دنیا", "و", "در", "به", "را", "از", "که", "این", "آن", "برای", "با", "بعد", "لطفاً", "کن", "است", "می", "باز", "بگو", "جمله", "متن", "لطفا"
        };
        var englishWords = new HashSet<string>(StringComparer.Ordinal)
        {
            "the", "a", "an", "and", "or", "to", "of", "in", "on", "for", "please", "open", "build", "is", "it", "then"
        };
        var lexicalHits = role == "fa" ? tokens.Count(persianWords.Contains) : tokens.Count(englishWords.Contains);
        var targetLetters = role == "fa" ? persian : latin;
        var otherLetters = role == "fa" ? latin : persian;
        var mixedBonus = AutoLanguageText.HasMeaningfulMixedScript(text) ? 22.0 : 0.0;
        return mixedBonus + Math.Min(targetLetters, 120) * 0.12 - Math.Min(otherLetters, 120) * 0.04
               + Math.Min(tokens.Length, 32) * 0.18 + Math.Min(uniqueTokens, 24) * 0.14
               + Math.Min(lexicalHits, 10) * 1.1;
    }
}
