namespace MadNegar;

internal static class Program
{
    private static int Main()
    {
        var cases = new (string Name, string Input, string Expected, bool Yeh, bool Kaf, bool HalfSpaces, bool Punctuation)[]
        {
            ("Arabic Yeh/Kaf to Persian forms", "علي كریم", "علی کریم", true, true, false, false),
            ("common half-spaces", "می رود و کتاب ها", "می‌رود و کتاب‌ها", false, false, true, false),
            ("Persian punctuation spacing", "سلام ؟دنیا", "سلام؟ دنیا", false, false, false, true),
            ("remove spaces before Persian punctuation", "سلام ، دنیا", "سلام، دنیا", false, false, false, true),
            ("protect URLs while normalizing surrounding Persian text", "لینک https://example.com/ي و كریم", "لینک https://example.com/ي و کریم", false, true, false, false),
            ("protect inline code", "متن `ك ي` و كریم", "متن `ك ي` و کریم", false, true, false, false),
            ("leave English-only punctuation alone", "hello ,world", "hello ,world", true, true, true, true),
            ("keep line breaks", "خط اول\nخط دوم", "خط اول\nخط دوم", true, true, true, true),
            ("all rules disabled", "علي كریم می رود کتاب ها سلام ؟دنیا", "علي كریم می رود کتاب ها سلام ؟دنیا", false, false, false, false),
        };

        var failures = 0;
        var totalTests = cases.Length + 1;
        foreach (var test in cases)
        {
            var actual = PersianTextNormalizer.Normalize(test.Input, test.Yeh, test.Kaf, test.HalfSpaces, test.Punctuation);
            if (string.Equals(actual, test.Expected, StringComparison.Ordinal))
            {
                Console.WriteLine($"PASS: {test.Name}");
            }
            else
            {
                failures++;
                Console.Error.WriteLine($"FAIL: {test.Name}\n  expected: {test.Expected}\n  actual:   {actual}");
            }
        }

        const string idempotentInput = "علي كریم، می رود و کتاب ها";
        var once = PersianTextNormalizer.Normalize(idempotentInput, true, true, true, true);
        var twice = PersianTextNormalizer.Normalize(once, true, true, true, true);
        if (string.Equals(once, twice, StringComparison.Ordinal)) Console.WriteLine("PASS: normalization is idempotent");
        else
        {
            failures++;
            Console.Error.WriteLine($"FAIL: normalization is idempotent\n  once:  {once}\n  twice: {twice}");
        }

        var autoLanguageTests = new (string Name, bool Passed)[]
        {
            ("unknown Auto strategy safely normalizes to balanced", AutoLanguageText.NormalizeStrategy("unexpected") == AutoLanguageText.Balanced),
            ("mixed mode persists as a supported strategy", AutoLanguageText.NormalizeStrategy(AutoLanguageText.MixedSegments) == AutoLanguageText.MixedSegments),
            ("legacy Auto preferences migrate to Persian-first dual-model UI", AutoLanguageText.NormalizeForSettings(AutoLanguageText.PreferPersian) == AutoLanguageText.DualModelMixed && AutoLanguageText.NormalizeForSettings(AutoLanguageText.PreferEnglish) == AutoLanguageText.DualModelMixed && AutoLanguageText.NormalizeForSettings(AutoLanguageText.MixedSegments) == AutoLanguageText.DualModelMixed),
            ("Standard Auto keeps native live language detection", AutoLanguageText.ResolveLivePreviewLanguage("auto", AutoLanguageText.Balanced) is null),
            ("dual-model Auto does not force live preview to Persian", AutoLanguageText.ResolveLivePreviewLanguage("auto", AutoLanguageText.DualModelMixed) is null),
            ("explicit English remains English regardless of preference", AutoLanguageText.ResolveLivePreviewLanguage("en", AutoLanguageText.DualModelMixed) == "en"),
            ("explicit Persian remains Persian regardless of preference", AutoLanguageText.ResolveLivePreviewLanguage("fa", AutoLanguageText.Balanced) == "fa"),
            ("English-first preference normalizes independently", AutoLanguageText.NormalizeTranscriptionLanguagePreference("ENGLISH-FIRST") == AutoLanguageText.EnglishFirstPreference),
            ("unknown transcript preference safely defaults to Persian-first", AutoLanguageText.NormalizeTranscriptionLanguagePreference("unknown") == AutoLanguageText.PersianFirstPreference),
            ("Persian-preferred recovery avoids forcing clear English into Persian", !AutoLanguageText.ShouldTryPersianPreferredPass("hello world from model")),
            ("Persian-preferred recovery recognizes common Finglish", AutoLanguageText.ShouldTryPersianPreferredPass("salam chetori")),
            ("Persian-preferred recovery recognizes a mixed-technology Finglish phrase", AutoLanguageText.ShouldTryPersianPreferredPass("emrooz bayad in file ro upload konam", "en")),
            ("Persian-preferred recovery does not rerun clear English", !AutoLanguageText.ShouldTryPersianPreferredPass("please open the API settings", "en")),
            ("Persian-preferred recovery does not rerun already Persian output", !AutoLanguageText.ShouldTryPersianPreferredPass("سلام دنیا")),
            ("Persian-first restores common English loanwords to Latin spelling", AutoLanguageText.RestoreEnglishTechnicalTerms("امروز باید این فایل رو آپلود کنم") == "امروز باید این فایل رو Upload کنم"),
            ("English-preferred recovery requires strongly Persian-dominant output", AutoLanguageText.ShouldTryEnglishPreferredPass("این یک جملهٔ کاملاً فارسی است")),
            ("Persian candidates require actual Persian letters", AutoLanguageText.IsPersianCandidate("سلام دنیا", "hello world")),
            ("English candidates require Latin letters", AutoLanguageText.IsEnglishCandidate("this is a complete test", "سلام دنیا این یک جملهٔ فارسی است")),
            ("chunk merge removes exact overlap but preserves mixed scripts", AutoLanguageText.MergeSegmentTranscripts(["سلام this is", "this is a test"]) == "سلام this is a test"),
            ("chunk merge preserves text when no overlap is detectable", AutoLanguageText.MergeSegmentTranscripts(["سلام دنیا", "hello world"]) == "سلام دنیا hello world"),
        };
        foreach (var test in autoLanguageTests)
        {
            totalTests++;
            if (test.Passed) Console.WriteLine($"PASS: {test.Name}");
            else
            {
                failures++;
                Console.Error.WriteLine($"FAIL: {test.Name}");
            }
        }

        var multilingualWhisper = new SpeechModelChoice(new ModelInfo("tiny", "75 MiB"), null);
        var englishOnlyWhisper = new SpeechModelChoice(new ModelInfo("tiny.en", "75 MiB", true), null);
        var fasterWhisper = new SpeechModelChoice(null, ProviderModelCatalog.All.First(item => item.Provider == "faster-whisper"));
        var persianVosk = new SpeechModelChoice(null, ProviderModelCatalog.All.First(item => item.Provider == "Vosk" && item.Languages == "Persian"));
        var englishVosk = new SpeechModelChoice(null, ProviderModelCatalog.All.First(item => item.Provider == "Vosk" && item.Languages.StartsWith("English", StringComparison.OrdinalIgnoreCase)));
        var dibaPersian = new SpeechModelChoice(null, ProviderModelCatalog.All.First(item => item.Provider == "Diba-STT"));
        var nemotron = new SpeechModelChoice(null, ProviderModelCatalog.All.First(item => item.Provider == "Oncilla / Nemotron (Persian)"));
        var dualSelectionTests = new (string Name, bool Passed)[]
        {
            ("multilingual Whisper.cpp is eligible for both dual-model language roles", DualModelSelectionPolicy.SupportsLanguageRole(multilingualWhisper, "fa") && DualModelSelectionPolicy.SupportsLanguageRole(multilingualWhisper, "en")),
            ("English-only Whisper.cpp is filtered from Persian role but available for English", !DualModelSelectionPolicy.SupportsLanguageRole(englishOnlyWhisper, "fa") && DualModelSelectionPolicy.SupportsLanguageRole(englishOnlyWhisper, "en")),
            ("faster-whisper model is eligible for both language roles", DualModelSelectionPolicy.SupportsLanguageRole(fasterWhisper, "fa") && DualModelSelectionPolicy.SupportsLanguageRole(fasterWhisper, "en")),
            ("Persian Vosk model is only eligible for Persian role", DualModelSelectionPolicy.SupportsLanguageRole(persianVosk, "fa") && !DualModelSelectionPolicy.SupportsLanguageRole(persianVosk, "en")),
            ("English Vosk model is only eligible for English role", !DualModelSelectionPolicy.SupportsLanguageRole(englishVosk, "fa") && DualModelSelectionPolicy.SupportsLanguageRole(englishVosk, "en")),
            ("Diba Persian model is eligible for Persian role", DualModelSelectionPolicy.SupportsLanguageRole(dibaPersian, "fa") && !DualModelSelectionPolicy.SupportsLanguageRole(dibaPersian, "en")),
            ("live preview supports Vosk and Diba engines for manual language-specific selection", persianVosk.IsSupportedForLive && englishVosk.IsSupportedForLive && dibaPersian.IsSupportedForLive),
            ("runtime-not-connected Nemotron is excluded from both roles", !DualModelSelectionPolicy.SupportsLanguageRole(nemotron, "fa") && !DualModelSelectionPolicy.SupportsLanguageRole(nemotron, "en")),
            ("legacy model IDs migrate to provider-qualified faster-whisper keys", DualModelSelectionPolicy.LegacyOrSavedKey("", "small") == "faster-whisper::small"),
            ("provider-qualified keys are preserved during migration", DualModelSelectionPolicy.LegacyOrSavedKey("Vosk::vosk-model-small-en-us-0.15", "tiny") == "Vosk::vosk-model-small-en-us-0.15"),
            ("mixed-script candidate wins over a single-script comparison", DualModelSelectionPolicy.ChooseCandidate("لطفاً این API را در Visual Studio باز کن", "Please open the API in Visual Studio") == "لطفاً این API را در Visual Studio باز کن"),
            ("Persian lexical evidence can beat a longer English comparison", DualModelSelectionPolicy.ChooseCandidate("سلام دنیا", "hello from model") == "سلام دنیا"),
            ("English lexical evidence can beat Persian gibberish", DualModelSelectionPolicy.ChooseCandidate("خخخخ زززز", "please open the build window") == "please open the build window"),
            ("English comparison wins when Persian-role output conflicts with English audio detection", DualModelSelectionPolicy.ChooseCandidate("لطفاً تنظیمات را باز کن", "Please open the settings", "en", "en") == "Please open the settings"),
            ("mixed-script Persian candidate is preserved before language-label mismatch checks", DualModelSelectionPolicy.ChooseCandidate("لطفاً این API را در Visual Studio باز کن", "Please open the API in Visual Studio", "en", "en") == "لطفاً این API را در Visual Studio باز کن"),
            ("English-first does not erase Persian script when both language detections are unknown", DualModelSelectionPolicy.ChooseCandidate("لطفاً در را باز کن", "please open the door", null, null, AutoLanguageText.EnglishFirstPreference) == "لطفاً در را باز کن"),
            ("English-first does not replace Persian-dominant mixed script despite high English labels", DualModelSelectionPolicy.ChooseCandidate("لطفاً این فایل را Upload کن و در Google Drive ذخیره کن", "Please upload this file and save it in Google Drive", "en", "en", AutoLanguageText.EnglishFirstPreference) == "لطفاً این فایل را Upload کن و در Google Drive ذخیره کن"),
        };
        foreach (var test in dualSelectionTests)
        {
            totalTests++;
            if (test.Passed) Console.WriteLine($"PASS: {test.Name}");
            else
            {
                failures++;
                Console.Error.WriteLine($"FAIL: {test.Name}");
            }
        }

        var hasQuietPreset = AudioProfilePolicy.TryGetPreset(AudioProfilePolicy.QuietRoom, out var quietPreset);
        var hasNoisyPreset = AudioProfilePolicy.TryGetPreset(AudioProfilePolicy.NoisyRoom, out var noisyPreset);
        var hasCustomPreset = AudioProfilePolicy.TryGetPreset(AudioProfilePolicy.Custom, out _);
        var audioProfileTests = new (string Name, bool Passed)[]
        {
            ("Quiet Room preset sets conservative boost and enables auto-level", hasQuietPreset && quietPreset.MicrophoneBoostDb == 6 && quietPreset.AutoLevelQuietSpeech && !quietPreset.ReduceLowFrequencyRumble),
            ("Noisy Room preset avoids extra boost and enables rumble reduction", hasNoisyPreset && noisyPreset.MicrophoneBoostDb == 0 && noisyPreset.AutoLevelQuietSpeech && noisyPreset.ReduceLowFrequencyRumble),
            ("Custom audio profile is not an implicit noise-processing preset", !hasCustomPreset),
            ("unknown audio profile normalizes safely to Custom", AudioProfilePolicy.Normalize("future-profile") == AudioProfilePolicy.Custom),
        };
        foreach (var test in audioProfileTests)
        {
            totalTests++;
            if (test.Passed) Console.WriteLine($"PASS: {test.Name}");
            else { failures++; Console.Error.WriteLine($"FAIL: {test.Name}"); }
        }

        var oversizedDictionaryJson = System.Text.Json.JsonSerializer.Serialize(
            Enumerable.Range(0, LocalDictionaryText.MaximumEntries + 1)
                .Select(index => new LocalDictionaryEntry { Phrase = $"phrase-{index}", Replacement = $"replacement-{index}" }).ToList());
        var oversizedDictionaryImportRejected = false;
        try { _ = LocalDictionaryText.DeserializeImport(oversizedDictionaryJson); }
        catch (InvalidDataException) { oversizedDictionaryImportRejected = true; }

        var dictionaryTests = new (string Name, bool Passed)[]
        {
            ("dictionary import rejects a file exceeding the rule limit", oversizedDictionaryImportRejected),
            ("dictionary uses phrase boundaries and ignores substrings", LocalDictionaryText.Apply("API works; capital stays", [new LocalDictionaryEntry { Phrase = "API", Replacement = "application interface" }], "en") == "application interface works; capital stays"),
            ("dictionary protects URLs, email and code", LocalDictionaryText.Apply("Use API, https://example.com/API, mail me at api@example.com and `API`", [new LocalDictionaryEntry { Phrase = "API", Replacement = "interface" }], "en") == "Use interface, https://example.com/API, mail me at api@example.com and `API`"),
            ("dictionary replacements are single-pass and do not cascade", LocalDictionaryText.Apply("alpha", [new LocalDictionaryEntry { Phrase = "alpha", Replacement = "beta" }, new LocalDictionaryEntry { Phrase = "beta", Replacement = "gamma" }], "en") == "beta"),
            ("Auto scope uses Any rules for mixed language", LocalDictionaryText.Apply("Visual Studio", [new LocalDictionaryEntry { Phrase = "Visual Studio", Replacement = "VS", Language = "any" }, new LocalDictionaryEntry { Phrase = "Visual Studio", Replacement = "Editor", Language = "en" }], "auto") == "VS"),
            ("English scope is filtered when Persian is explicit", LocalDictionaryText.Apply("API", [new LocalDictionaryEntry { Phrase = "API", Replacement = "برنامه", Language = "fa" }, new LocalDictionaryEntry { Phrase = "API", Replacement = "interface", Language = "en" }], "fa") == "برنامه"),
            ("disabled dictionary rule is ignored", LocalDictionaryText.Apply("API", [new LocalDictionaryEntry { Phrase = "API", Replacement = "interface", Enabled = false }], "en") == "API"),
            ("dictionary import normalizes duplicates and invalid entries", LocalDictionaryText.NormalizeEntries([new LocalDictionaryEntry { Phrase = " API ", Replacement = "Interface" }, new LocalDictionaryEntry { Phrase = "api", Replacement = "Other" }, new LocalDictionaryEntry { Phrase = "empty", Replacement = "" }]).Count == 1),
            ("dictionary rejects empty replacement and multi-line entries", !LocalDictionaryText.TryValidateEntry("API", "", "any", out _, out _) && !LocalDictionaryText.TryValidateEntry("two\nlines", "one line", "any", out _, out _)),
        };
        foreach (var test in dictionaryTests)
        {
            totalTests++;
            if (test.Passed) Console.WriteLine($"PASS: {test.Name}");
            else
            {
                failures++;
                Console.Error.WriteLine($"FAIL: {test.Name}");
            }
        }

        var tempRoot = Path.Combine(Path.GetTempPath(), "MadNegar-History-Tests-" + Guid.NewGuid().ToString("N"));
        var exportPath = Path.Combine(tempRoot, "mixed-فارسی-English.txt");
        var historyTests = new List<(string Name, bool Passed)>();
        try
        {
            Directory.CreateDirectory(tempRoot);
            var savedEntry = TranscriptHistoryStore.Add(tempRoot, "سلام API را باز کن", "auto", "Whisper / test-model");
            var loadedEntries = TranscriptHistoryStore.Load(tempRoot);
            historyTests.Add(("history is local JSON and round-trips Persian/mixed text", loadedEntries.Count == 1 && loadedEntries[0].Text == "سلام API را باز کن" && loadedEntries[0].Language == "auto"));
            historyTests.Add(("history uses a bounded entry count and per-transcript limit", TranscriptHistoryStore.MaximumEntries <= 250 && TranscriptHistoryStore.MaximumTranscriptCharacters <= 50000));
            historyTests.Add(("history can delete a single entry", TranscriptHistoryStore.Delete(tempRoot, savedEntry.Id) && TranscriptHistoryStore.Load(tempRoot).Count == 0));
            _ = TranscriptHistoryStore.Add(tempRoot, "متن قابل حذف", "fa", "Whisper / test-model");
            TranscriptHistoryStore.Clear(tempRoot);
            historyTests.Add(("clearing history removes stored transcripts", TranscriptHistoryStore.Load(tempRoot).Count == 0));

            const string exportText = "سلام دنیا\nOpenAI API";
            TranscriptTextExport.WriteUtf8(exportPath, exportText);
            var exportedBytes = File.ReadAllBytes(exportPath);
            var hasUtf8Bom = exportedBytes.Length >= 3 && exportedBytes[0] == 0xEF && exportedBytes[1] == 0xBB && exportedBytes[2] == 0xBF;
            historyTests.Add(("TXT export writes a UTF-8 BOM and preserves Persian/mixed text", hasUtf8Bom && File.ReadAllText(exportPath, System.Text.Encoding.UTF8) == exportText));

            var rejectedEmptyExport = false;
            try { TranscriptTextExport.WriteUtf8(exportPath, "  "); }
            catch (InvalidDataException) { rejectedEmptyExport = true; }
            historyTests.Add(("TXT export refuses empty transcript content", rejectedEmptyExport));

            var batchOutputDirectory = Path.Combine(tempRoot, "batch-output");
            Directory.CreateDirectory(batchOutputDirectory);
            historyTests.Add(("batch accepts only WAV/MP3/AIFF inputs", BatchTranscriptionSupport.IsSupportedAudioPath("meeting.wav") && BatchTranscriptionSupport.IsSupportedAudioPath("جلسه.mp3") && BatchTranscriptionSupport.IsSupportedAudioPath("meeting.aiff") && !BatchTranscriptionSupport.IsSupportedAudioPath("movie.mp4")));
            historyTests.Add(("batch output stem sanitizes invalid names and Windows device names", BatchTranscriptionSupport.SanitizeFileStem("bad:name?.wav") == "bad_name_.wav" && BatchTranscriptionSupport.SanitizeFileStem("CON") == "_CON"));
            var initialStem = BatchTranscriptionSupport.GetUniqueOutputStem(batchOutputDirectory, "C:/input/Lecture.wav", [".txt", ".srt"]);
            historyTests.Add(("batch output stem stays shared across selected formats", initialStem == "Lecture"));
            File.WriteAllText(Path.Combine(batchOutputDirectory, "Lecture.txt"), "existing output");
            var uniqueStem = BatchTranscriptionSupport.GetUniqueOutputStem(batchOutputDirectory, "D:/other/Lecture.mp3", ["txt", ".srt"]);
            historyTests.Add(("batch avoids overwriting any existing requested output format", uniqueStem == "Lecture (2)"));
            var atomicBatchPath = Path.Combine(batchOutputDirectory, "فارسی-English.txt");
            BatchTranscriptionSupport.WriteTextAtomicallyAsync(atomicBatchPath, "سلام API", CancellationToken.None).GetAwaiter().GetResult();
            historyTests.Add(("batch TXT output is atomic and preserves mixed Unicode text", File.ReadAllText(atomicBatchPath, System.Text.Encoding.UTF8).TrimStart('\uFEFF') == "سلام API" && !Directory.GetFiles(batchOutputDirectory, "*.part", SearchOption.AllDirectories).Any()));
            using (var cancelledWrite = new CancellationTokenSource())
            {
                cancelledWrite.Cancel();
                var cancelledPath = Path.Combine(batchOutputDirectory, "cancelled.txt");
                var cancellationObserved = false;
                try { BatchTranscriptionSupport.WriteTextAtomicallyAsync(cancelledPath, "must not exist", cancelledWrite.Token).GetAwaiter().GetResult(); }
                catch (OperationCanceledException) { cancellationObserved = true; }
                historyTests.Add(("batch cancellation does not publish a partial TXT file", cancellationObserved && !File.Exists(cancelledPath) && !Directory.GetFiles(batchOutputDirectory, "*.part", SearchOption.AllDirectories).Any()));
            }
        }
        catch (Exception ex)
        {
            historyTests.Add(("history/export test setup and execution", false));
            Console.Error.WriteLine("History/export test exception: " + ex);
        }
        finally
        {
            try { if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, recursive: true); } catch { }
        }

        foreach (var test in historyTests)
        {
            totalTests++;
            if (test.Passed) Console.WriteLine($"PASS: {test.Name}");
            else
            {
                failures++;
                Console.Error.WriteLine($"FAIL: {test.Name}");
            }
        }

        var autoDirectionChecks = new (string Name, bool Passed)[]
        {
            ("mixed-language transcript with English technical terms keeps both scripts", AutoLanguageText.MergeSegmentTranscripts(["این API را باز کن", "را باز کن و بعد build بگیر"]) == "این API را باز کن و بعد build بگیر"),
            ("Finglish recovery is restricted to recognizable Persian tokens", AutoLanguageText.LooksLikeRomanizedPersian("salam chetori") && !AutoLanguageText.LooksLikeRomanizedPersian("please open the API settings")),
            ("clearly English text is not treated as Persian script", !AutoLanguageText.ContainsPersianScript("please open the API settings")),
            ("Auto retries English only for explicit Persian-label and Latin-only mismatch", AutoLanguageText.ShouldRetryEnglishForScriptMismatch("please open API settings", "fa") && !AutoLanguageText.ShouldRetryEnglishForScriptMismatch("salam chetori", "fa")),
            ("Auto retries Persian only for an English label with genuinely Persian-only output", AutoLanguageText.ShouldRetryPersianForScriptMismatch("این یک جملهٔ کاملاً فارسی است", "en") && !AutoLanguageText.ShouldRetryPersianForScriptMismatch("please open API settings", "en")),
            ("mixed Persian-English technical terms block whole-utterance Persian retry", !AutoLanguageText.ShouldRetryPersianForScriptMismatch("این API را باز کن، لطفاً", "en") && !AutoLanguageText.ShouldRetryPersianForScriptMismatch("این API را در Visual Studio باز کن", "en")),
            ("mixed Persian-English technical phrases do not trigger false whole-language retry", !AutoLanguageText.ShouldRetryEnglishForScriptMismatch("این API را در Visual Studio باز کن", "fa") && !AutoLanguageText.ShouldRetryPersianForScriptMismatch("این API را در Visual Studio باز کن", "en")),
            ("meaningful mixed script is recognized for retry protection", AutoLanguageText.HasMeaningfulMixedScript("این API را در Visual Studio باز کن") && !AutoLanguageText.HasMeaningfulMixedScript("لطفاً تنظیمات را باز کن") && !AutoLanguageText.HasMeaningfulMixedScript("please open the API settings")),
        };
        foreach (var test in autoDirectionChecks)
        {
            totalTests++;
            if (test.Passed) Console.WriteLine($"PASS: {test.Name}");
            else
            {
                failures++;
                Console.Error.WriteLine($"FAIL: {test.Name}");
            }
        }

        var technicalTermTests = new (string Name, bool Passed)[]
        {
            ("Auto restores the sample mixed-language technical sentence", AutoLanguageText.RestoreEnglishTechnicalTerms("لطفاً این ای پی آی را در ویژوال استودیو باز کن و بعد بیلد بگیر") == "لطفاً این API را در Visual Studio باز کن و بعد build بگیر"),
            ("Auto technical term normalization is idempotent", AutoLanguageText.RestoreEnglishTechnicalTerms(AutoLanguageText.RestoreEnglishTechnicalTerms("این API را در Visual Studio باز کن و بعد build بگیر")) == "این API را در Visual Studio باز کن و بعد build بگیر"),
            ("Auto term normalization protects URLs and inline code", AutoLanguageText.RestoreEnglishTechnicalTerms("ویژوال استودیو https://example.com/ویژوال `ویژوال استودیو`") == "Visual Studio https://example.com/ویژوال `ویژوال استودیو`"),
            ("Auto term normalization leaves clear English sentence unchanged", AutoLanguageText.RestoreEnglishTechnicalTerms("Please open Visual Studio and build") == "Please open Visual Studio and build"),
            ("Auto term normalization does not rewrite prefixes inside ZWNJ compound words", AutoLanguageText.RestoreEnglishTechnicalTerms("در گیت\u200Cسازی و پایتون\u200Cکار") == "در گیت\u200Cسازی و پایتون\u200Cکار"),
            ("Auto term normalization still restores full ZWNJ-separated aliases", AutoLanguageText.RestoreEnglishTechnicalTerms("گیت\u200Cهاب و ویژوال\u200Cاستودیو") == "GitHub و Visual Studio"),
        };
        foreach (var test in technicalTermTests)
        {
            totalTests++;
            if (test.Passed) Console.WriteLine($"PASS: {test.Name}");
            else
            {
                failures++;
                Console.Error.WriteLine($"FAIL: {test.Name}");
            }
        }

        DiagnosticsState.RecordingStarted("Test Microphone", 48000, 2, 32, "IeeeFloat");
        DiagnosticsState.UpdateAudioMeter(0.985f, 0.12f);
        var diagnosticsAppRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MadNegar");
        DiagnosticsState.SetTranscriptionContext("faster-whisper", "test-model", Path.Combine(diagnosticsAppRoot, "Models", "test-model"), "runtime ready", "auto", "balanced");
        DiagnosticsState.SetStage("Transcription completed");
        DiagnosticsState.RecordOperationDuration(TimeSpan.FromMilliseconds(1250));
        DiagnosticsState.RecordLivePreviewCompleted(TimeSpan.FromMilliseconds(410));
        DiagnosticsState.RecordLivePreviewSkipped();
        var diagnosticsText = DiagnosticsState.CreateReport(diagnosticsAppRoot, "Test Microphone", "test-model", "Auto", "balanced", false);
        var diagnosticsTests = new (string Name, bool Passed)[]
        {
            ("diagnostics report includes audio format, peak/RMS and clipping", diagnosticsText.Contains("48000 Hz") && diagnosticsText.Contains("Peak") && diagnosticsText.Contains("RMS") && diagnosticsText.Contains("Clipping observed (peak >= 0.98): Yes")),
            ("diagnostics report includes model, language, runtime and latency", diagnosticsText.Contains("faster-whisper / test-model") && diagnosticsText.Contains("Spoken language: Auto") && diagnosticsText.Contains("runtime ready") && diagnosticsText.Contains("1.25 s")),
            ("diagnostics report includes live preview duration and skipped ticks", diagnosticsText.Contains("Live preview chunks completed: 1") && diagnosticsText.Contains("Live preview ticks skipped while busy: 1") && diagnosticsText.Contains("0.410 s")),
            ("diagnostics masks local user path and excludes transcript/audio content", diagnosticsText.Contains("%LOCALAPPDATA%") && !diagnosticsText.Contains("Transcript text:") && !diagnosticsText.Contains("Audio bytes:")),
            ("diagnostics report states that it is not automatically written to disk", diagnosticsText.Contains("not written to disk automatically")),
        };
        foreach (var test in diagnosticsTests)
        {
            totalTests++;
            if (test.Passed) Console.WriteLine($"PASS: {test.Name}");
            else
            {
                failures++;
                Console.Error.WriteLine($"FAIL: {test.Name}");
            }
        }

        var subtitleTests = new (string Name, bool Passed)[]
        {
            ("WAV, MP3 and AIFF are supported for local file transcription", SubtitleExportSupport.IsSupportedAudioPath("C:/نمونه/sample.wav") && SubtitleExportSupport.IsSupportedAudioPath("sample.mp3") && SubtitleExportSupport.IsSupportedAudioPath("sample.aiff") && !SubtitleExportSupport.IsSupportedAudioPath("movie.mp4")),
            ("valid timestamped SRT is accepted", SubtitleExportSupport.IsValidSubtitle("srt", "1\r\n00:00:00,000 --> 00:00:01,250\r\nسلام API\r\n", out _)),
            ("SRT with reversed timestamps is rejected", !SubtitleExportSupport.IsValidSubtitle("srt", "1\n00:00:02,000 --> 00:00:01,000\nسلام\n", out _)),
            ("valid WebVTT with a header and timestamp is accepted", SubtitleExportSupport.IsValidSubtitle("vtt", "WEBVTT\n\n00:00:00.000 --> 00:00:01.500\nHello world\n", out _)),
            ("WebVTT without its required header is rejected", !SubtitleExportSupport.IsValidSubtitle("vtt", "00:00:00.000 --> 00:00:01.000\nHello\n", out _)),
            ("WebVTT rejects a malformed lookalike header", !SubtitleExportSupport.IsValidSubtitle("vtt", "WEBVTT_NOT_VALID\n\n00:00:00.000 --> 00:00:01.000\nHello\n", out _)),
            ("WebVTT rejects a malformed timestamp even when another cue is valid", !SubtitleExportSupport.IsValidSubtitle("vtt", "WEBVTT\n\n00:00:00.000 --> 00:00:01.000\nOK\n\n00:00:02.000 --> 00:00:03,000\nBad time\n", out _)),
            ("WebVTT accepts short mm:ss timestamps", SubtitleExportSupport.IsValidSubtitle("vtt", "WEBVTT\n\n01:02.000 --> 01:03.250\nHello\n", out _)),
            ("SRT rejects out-of-range time components", !SubtitleExportSupport.IsValidSubtitle("srt", "1\n00:61:00,000 --> 00:61:01,000\nHello\n", out _)),
            ("subtitle cue without text is rejected", !SubtitleExportSupport.IsValidSubtitle("srt", "1\n00:00:00,000 --> 00:00:01,000\n\n", out _)),
            ("subtitle text processing applies Persian cleanup and dictionary without changing timestamps", SubtitleExportSupport.ApplyCueTextProcessing(
                "1\n00:00:00,000 --> 00:00:01,000\nعلي كریم ؟API\n", "srt", true, true, false, true,
                new[] { new LocalDictionaryEntry { Phrase = "API", Replacement = "Application Programming Interface", Language = "any" } }, "auto")
                .Contains("00:00:00,000 --> 00:00:01,000", StringComparison.Ordinal) &&
                SubtitleExportSupport.ApplyCueTextProcessing(
                    "1\n00:00:00,000 --> 00:00:01,000\nعلي كریم ؟API\n", "srt", true, true, false, true,
                    new[] { new LocalDictionaryEntry { Phrase = "API", Replacement = "Application Programming Interface", Language = "any" } }, "auto")
                    .Contains("علی کریم؟ Application Programming Interface", StringComparison.Ordinal)),
            ("subtitle export format is restricted to SRT and VTT", SubtitleExportSupport.IsSupportedSubtitleFormat("srt") && SubtitleExportSupport.IsSupportedSubtitleFormat("vtt") && !SubtitleExportSupport.IsSupportedSubtitleFormat("txt")),
        };
        foreach (var test in subtitleTests)
        {
            totalTests++;
            if (test.Passed) Console.WriteLine($"PASS: {test.Name}");
            else { failures++; Console.Error.WriteLine($"FAIL: {test.Name}"); }
        }

        Console.WriteLine($"Result: {totalTests - failures}/{totalTests} text/language/dictionary/history/export tests passed");
        return failures == 0 ? 0 : 1;
    }
}
