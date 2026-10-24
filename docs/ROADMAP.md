# MadNegar Roadmap and Current Status

**Source version:** `1.0.1`  
**Purpose:** Track the current v1.0.1 baseline and remaining work without repeating the history of earlier releases.

## Current status

- MadNegar is a Windows desktop application built with WinForms and .NET 10. Production C# source files are organized under `src/`, while the project file and build entry points remain at the repository root.
- The source includes microphone capture and transcription, settings and model selection, live preview, single-file and batch audio transcription, TXT export, SRT/VTT export in compatible Whisper.cpp workflows, an optional global hotkey, and publish/installer scripts.
- In Auto mode, the Whisper.cpp path must explicitly request language detection with `-l auto`; otherwise, the tool's English default may be applied. The mixed-language selection strategy is deliberately conservative, and recognition errors can still occur.
- Informal user-level checks have reported better Persian/English handling in some scenarios. Those observations are not a substitute for systematic evaluation against reference transcripts.
- Version 1.0.1 fixes the boundary check for English technical terms in Persian compound words, treating the Persian half-space as part of the word. For example, `گیت‌سازی` and `پایتون‌کار` should not be changed to `Git‌سازی` or `Python‌کار`.
- Static source checks and mocked tests alone cannot establish real-world ASR accuracy, the appearance of the live UI, or the correctness of every end-to-end workflow.

## Next priorities

1. On Windows 10 or Windows 11, run `dotnet restore` and `dotnet build -c Release` with the .NET 10 SDK. Then run the C# harness, Python tests, and publish/installer scripts.
2. Evaluate Auto mode using real Persian-only, English-only, and mixed Persian-English recordings. Keep the audio samples and reference transcripts, and record WER/CER, language-selection errors, omissions or repetitions at chunk boundaries, and latency.
3. Verify that a Persian sentence containing technical terms such as `API`, `Visual Studio`, and `build` remains primarily in Persian; English-only input is not translated into Persian; and Persian-only speech is not rendered as Finglish.
4. Test batch processing, Cancel/Retry, unique output names, and TXT/SRT/VTT exports on Windows using the [manual acceptance test plan](../tests/MANUAL_ACCEPTANCE.md).
5. Test installer setup, upgrade, and uninstall in a Windows test environment. User data must not be removed without notice.

## Auto-mode acceptance criteria

Use at least 10 real recordings for each category: Persian only, English only, and mixed Persian-English. Add short utterances, rapid language switches, pauses or silence, and difficult recording conditions. The [manual acceptance test plan](../tests/MANUAL_ACCEPTANCE.md) defines how to record results and evaluate WER/CER.

Do not describe Auto-mode accuracy as verified until real-world results have been collected and reviewed.

## Reporting standards

Every report should distinguish among a test that actually ran, a static source check, a test that is only described in documentation, and a test that still requires Windows, a microphone, real audio, or a downloaded model. The roadmap intentionally does not duplicate the history of previous releases.
