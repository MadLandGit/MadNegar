# MadNegar Manual Acceptance Test Plan

This checklist consolidates hands-on testing for Auto language handling, Settings and model selectors, batch transcription, and Windows release packaging. Static checks and mocked tests are useful, but they do not replace these manual tests.

## 1. Setup and automated checks

From the repository root on Windows with the .NET 10 SDK installed, run:

```powershell
dotnet --list-sdks
dotnet restore
dotnet build -c Release
dotnet run --project .\tests\PersianTextNormalizerChecks\PersianTextNormalizerChecks.csproj -c Release
python .\tests\run_source_checks.py
python -m unittest discover -s tests -p "test_*.py" -v
python -m py_compile .\FasterWhisperAdapter.py .\tests\test_faster_whisper_adapter.py .\tests\test_release_contract.py
```

Mark the build and C# harness as passing only when they actually run and return exit code 0. A structural C# check, a Python test run, or successful project parsing is not a substitute for a successful application build.

## 2. Auto mode and mixed Persian-English speech

### Prepare a test set

Collect at least 10 real recordings for each of the following categories, and prepare an exact reference transcript for every recording:

- **Persian only:** Standard and conversational Persian, short and long sentences, numbers, and proper names.
- **English only:** Clear English speech, technical phrases, and product names, with no Persian speech.
- **Mixed Persian-English:** Persian explanations that include spoken terms such as `API`, `Visual Studio`, `build`, `runtime`, and `GPU`, including mid-sentence language switches.

Add supplementary samples with rapid code-switching, pauses or silence, speech shorter than eight seconds, recognizable Finglish, and difficult recording conditions. These extra samples do not count toward the 30 core recordings.

### Configure and run

1. Set **Speak Language** to **Auto**. Test **Transcription Language Preference** separately with Persian-first and English-first settings.
2. Under **Settings → Transcription**, test **Standard Auto** and, when two compatible models are available, **Dual-model mixed Auto** separately.
3. Use complete, compatible models. Record the provider/model ID, Auto strategy, app version, and hardware for each run.
4. Repeat a mixed-language sentence such as «لطفاً این API را در Visual Studio باز کن و بعد build بگیر». Persian words should remain in Persian script, and spoken English technical terms should remain in Latin script. The sentence should not be translated.
5. Confirm that Persian-only speech remains Persian and English-only speech remains English. A few Latin-script words should not force the entire sentence into English. Persian-first preference should not lock out or discard English input.
6. Record the live preview and the final transcript after stopping as separate results. Inspect raw engine output only when needed and with the user's consent; do not retain personal text in logs unnecessarily.

### Record the results

For each run, capture the reference text, output text, reported language, language-selection errors, words omitted or duplicated at chunk boundaries, processing time, and notes. When practical, calculate WER/CER using consistent normalization. Do not describe Auto mode as fully accurate until the real-world test set has been completed and evaluated.

## 3. Settings and model selectors

1. Open **Settings → Transcription**. Confirm that settings cards do not overlap or extend beyond the usable window area.
2. Verify that **Speak Language = Auto** is independent of the transcript-language preference. Changing the UI language or reopening Settings must not unexpectedly change that preference.
3. Each model selector should show only complete models compatible with the selected role and runtime. An English-focused model should not be recommended as Persian-focused, or vice versa.
4. Select and save models for both roles. Close and reopen Settings and confirm that the selections persist. If the app disallows using one model for both roles, it should explain the restriction clearly.
5. Manually test the main window controls, live preview, Persian/English text direction, Clear/Undo, the global shortcut controls, and theme switching.

## 4. Audio batch processing

1. Add at least three valid files in WAV, MP3, and AIFF formats. Also test file paths containing spaces and Persian or other Unicode characters.
2. Add the same file twice and confirm that the duplicate is handled safely. A corrupt, empty, or unsupported file should not stop the entire queue without reporting the problem.
3. Confirm that TXT output is UTF-8 and the original audio files are unchanged. Two inputs with the same base filename in different folders must produce distinct output names.
4. Create a file with the expected output name before running a job again. MadNegar must not overwrite it. After a job succeeds or is canceled, no temporary `.part` file should remain.
5. With a compatible Whisper.cpp engine, test SRT and VTT separately and together. Validate timestamps and output-file structure. With an incompatible engine, these options should be disabled or their limitations should be explained.
6. Cancel a multi-file queue while it is running. Original audio files must remain intact, and each file's status should be clear.
7. Create a partial or failed output and test **Retry**. A successful earlier output should not be recreated or kept under a duplicate name; retry only the failed portion.
8. While a batch queue is running, press the global shortcut. A conflicting recording session should not start.

## 5. Windows publishing and installer

### Standalone executable

Run:

```powershell
.\Build-Release.cmd
```

The build should produce `publish\MadNegar.exe` and `publish\MadNegar.exe.sha256`. Verify that the executable's SHA-256 hash matches the sidecar file.

### Installer

Run:

```powershell
.\Build-Installer.cmd
```

Confirm that `output\MadNegar-Setup-1.0.1.exe` and its matching `.sha256` file are created and that the recorded hash is correct. If Inno Setup is not installed, the script should provide a clear error and explain how to set `ISCC_PATH`.

### Install, upgrade, and uninstall

1. Launch the installed app from the Start menu.
2. Test both a clean install and an upgrade from the previous version on Windows 10 or Windows 11.
3. Confirm that Settings and local models are retained according to the app's data-retention policy.
4. During uninstall, data that the project policy says should be retained must not be deleted without notice.
5. On a real Windows system, test the global hotkey, Notepad or Word, File Explorer rename behavior, microphone input, batch processing, and subtitle export.

## 6. Failure and acceptance reports

For every PASS/FAIL result, record the app version, Windows version, model/provider, Auto strategy, input type (microphone or file), live-preview status, technical-term options, non-sensitive output text, relevant logs if the user consents, processing time, and any error message.

Reports must distinguish between checks that actually ran, checks that were static only, and checks that still require Windows, a compatible model, or real audio.
