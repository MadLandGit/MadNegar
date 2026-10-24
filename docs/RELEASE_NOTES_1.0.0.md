# MadNegar v1.0.0 — Initial Windows x64 Release

MadNegar v1.0.0 is the first packaged Windows x64 release of the application. It brings together local speech transcription, voice typing, and practical text tools in an installer and a standalone executable.

## Downloads

This document records the original v1.0.0 release. A separate v1.0.0 binary release is not carried into this public repository. For the currently maintained package, download [MadNegar v1.0.1](https://github.com/MadLandGit/MadNegar/releases/tag/v1.0.1). SHA-256 sidecar files are provided beside the current downloads.

For source code, installation guidance, and build instructions, see the [README](https://github.com/MadLandGit/MadNegar/blob/main/README.md).

## What's included

- Microphone capture, live previews, and final transcripts with supported speech engines.
- Separate spoken-language and transcription-language preferences, with safeguards for Persian, English, and mixed-language speech.
- Optional preservation of recognized English technical terms in Latin script, plus a local phrase dictionary and Persian text cleanup.
- An optional global voice-typing shortcut, a **Copy Only** mode, and a final check of the target control before text is inserted.
- WAV, MP3, and AIFF file transcription, plus a sequential batch queue with per-file status, cancellation, retry, and protection against overwriting existing output files.
- TXT export, with SRT/VTT subtitle export in compatible Whisper.cpp workflows.
- Optional transcript history, disabled by default. Audio is not saved in transcript history, and diagnostic information is copied only when requested.

## Speech engines and requirements

- **Whisper.cpp:** Final transcription and live preview with compatible models.
- **faster-whisper:** Final transcription with local models. Initial runtime setup may require an internet connection.
- **Vosk:** Available features depend on the installed model.
- **Diba-STT:** Persian-focused speech recognition.
- **Oncilla / Nemotron:** Listed in the model catalog; final-transcription adapters are not implemented in this release.

Speech models are not included in the installer and must be set up separately. MadNegar targets Windows 10 and Windows 11 (x64). Its application binaries are self-contained for .NET and do not require a separate .NET Runtime installation. Some initial model or runtime setup steps may require an internet connection.

## Release validation

The v1.0.0 release workflow completed the following checks successfully:

- 242 static source-regression checks.
- 64 Python tests.
- 101 of 101 checks in the C# text, language, dictionary, history, and export harness.
- A Windows build using .NET 10.
- Standalone executable and installer builds, with SHA-256 checksums verified.

These checks cover packaging and selected text-processing behavior. They do not guarantee accurate recognition across every accent, recording environment, or speaking style. For important use cases, evaluate the app with representative Persian, English, and mixed-language recordings.

## Next release

[MadNegar v1.0.1](https://github.com/MadLandGit/MadNegar/releases/tag/v1.0.1) includes a focused fix for Persian half-space boundaries in technical-term replacement. The core feature set introduced in v1.0.0 remains the baseline.
