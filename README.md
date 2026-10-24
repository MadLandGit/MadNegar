# MadNegar

**A local-first Windows app for speech transcription and voice typing, built for Persian, English, and mixed Persian-English speech.**

MadNegar is a Windows desktop application that transcribes microphone input, shows a live preview when the selected engine supports it, and can type the final transcript into the active application. It also supports audio-file transcription, batch processing, a local phrase dictionary, and optional Persian text cleanup.

**Latest release:** [MadNegar v1.0.1](https://github.com/MadLandGit/MadNegar/releases/tag/v1.0.1)

## Download

- **[Download the Windows installer — MadNegar-Setup-1.0.1.exe](https://github.com/MadLandGit/MadNegar/releases/download/v1.0.1/MadNegar-Setup-1.0.1.exe)** (recommended)
- [Download the standalone executable — MadNegar.exe](https://github.com/MadLandGit/MadNegar/releases/download/v1.0.1/MadNegar.exe)
- [View all v1.0.1 release files and SHA-256 checksums](https://github.com/MadLandGit/MadNegar/releases/tag/v1.0.1)

The application binaries are self-contained for .NET, so you do not need to install the .NET Runtime separately. **Speech models are not included in the installer.** Select or download a compatible model in the app. Initial model or runtime setup may require an internet connection.

## Features

- **Microphone transcription:** Capture speech, preview recognized text in supported engines, and generate a final transcript.
- **Persian, English, and mixed-language support:** Configure the spoken language separately from the preferred transcription language, with safeguards for code-switching and script mismatches.
- **Technical-term preservation:** Keep recognized English terms such as `API`, `Visual Studio`, and `build` in Latin script instead of converting them to phonetic Persian spellings.
- **Auto mode choices:** Choose Standard Auto for a single-model workflow or Persian-first mixed Auto to use two compatible models for mixed Persian-English speech. The two-model strategy can take longer and still depends on model quality.
- **Persian Text normalization and local tools:** Maintain your own phrase dictionary and optionally normalize Persian Yeh/Kaf characters, half-spaces, and punctuation.
- **Voice-typing shortcut:** Use an optional global hotkey to insert the final transcript into the active app. MadNegar checks the target control again before inserting text and does not press Enter. Use **Copy Only** when you do not want text inserted automatically. Optional start/stop tones provide feedback; the app does not show Windows tray-balloon notifications.
- **Audio files and batch processing (Batch…):** Transcribe WAV, MP3, and AIFF files. The batch queue shows per-file status and supports cancellation, retries, and safeguards against overwriting existing output files.
- **Exports:** Save transcripts as TXT. SRT and VTT subtitles are available in compatible Whisper.cpp workflows.
- **Privacy-conscious history and diagnostics:** Transcript history is optional and disabled by default. Audio is not stored in transcript history. Diagnostics are generated in memory and copied only when requested.

## Speech engine support

| Engine | Support in this release | Notes |
| --- | --- | --- |
| Whisper.cpp | Final transcription and live preview | Requires a compatible model. Auto mode explicitly requests automatic language detection. |
| faster-whisper | Final transcription only | Uses local CTranslate2 model files. With confirmation, MadNegar can set up a private Python runtime on first use; an internet connection may be needed for setup. Live preview is not supported by this engine in this release. |
| Vosk | Final transcription and supported live preview | Language and feature support depend on the installed model. |
| Diba-STT | Persian transcription and supported live preview | Persian only. |
| Oncilla / Nemotron | Listed in the model catalog only | Final-transcription adapters are not implemented in this release. |

After the required model and runtime are set up, transcription is handled by the selected local engine. Models and some runtimes are downloaded separately and are not bundled with the app installer.

## Install and get started

1. Download `MadNegar-Setup-1.0.1.exe` from the [latest release](https://github.com/MadLandGit/MadNegar/releases/tag/v1.0.1).
2. Run the installer and follow the on-screen instructions.
3. Launch MadNegar and select a compatible speech engine and model in **Settings**.
4. Complete any model or runtime setup the app requests. Then record a short sample and review the transcript.
5. To use voice typing, enable the global shortcut in **Settings**. Select **Copy Only** if you prefer not to insert text into another application automatically.

MadNegar targets Windows 10 and Windows 11 on x64 systems. Before uninstalling the app or switching versions, back up any user data you want to keep.

## Build from source

The project file stays at the repository root, while the application's C# source files are organized under `src/`. The existing build commands still work from the repository root.

### Requirements

- Windows 10 or Windows 11 (x64).
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).
- Python 3.12 or a compatible version for the Python regression suite.
- [Inno Setup](https://jrsoftware.org/isinfo.php) to build the installer.

### Build, run, and test

Open PowerShell in the repository root:

```powershell
# Restore dependencies and build the app
dotnet restore
dotnet build -c Release

# Run the app from source
dotnet run --project .\MadNegar.csproj

# Run the C# test harness and regression checks
dotnet run --project .\tests\PersianTextNormalizerChecks\PersianTextNormalizerChecks.csproj -c Release
python .\tests\run_source_checks.py
python .\tests\check_csharp_structure.py
python -m unittest discover -s tests -p "test_*.py" -v
python -m py_compile .\FasterWhisperAdapter.py .\tests\test_faster_whisper_adapter.py .\tests\test_release_contract.py

# Build the standalone executable
.\Build-Release.cmd

# Build the installer
.\Build-Installer.cmd
```

Expected outputs include `publish\MadNegar.exe` and its `.sha256` sidecar, along with `output\MadNegar-Setup-1.0.1.exe` and its checksum. Before distributing a build, compare each file with its matching `.sha256` file.

## Known limitations

- Automatic language detection is heuristic. Rapid code-switching, background noise, accents, and Finglish may cause omissions or script errors. Evaluation against real Persian-only, English-only, and mixed-language recordings and hands-on end-to-end tests remain required on Windows 10/11; automated checks alone do not establish recognition accuracy.
- Live preview depends on the selected engine and model. faster-whisper supports final transcription only in this release.
- SRT/VTT export is available only in compatible Whisper.cpp workflows.
- Oncilla and Nemotron appear in the model catalog but cannot yet be used for final transcription through MadNegar.
- Passing automated tests and building successfully do not guarantee accurate recognition for every recording. For a hands-on validation checklist, see the [manual acceptance test plan](tests/MANUAL_ACCEPTANCE.md).

## Release notes

- [v1.0.1 — Persian half-space boundary fix](docs/RELEASE_NOTES_1.0.1.md) · [Download v1.0.1](https://github.com/MadLandGit/MadNegar/releases/tag/v1.0.1)
- [v1.0.0 — Initial Windows release notes](docs/RELEASE_NOTES_1.0.0.md) · Historical notes; the current download is v1.0.1.

## Feedback and bug reports

When reporting a bug, include your MadNegar version, Windows version, selected engine and model, steps to reproduce the issue, and any error message. Do not attach private recordings or transcripts unless you have permission to share them. Review diagnostic output for personal file paths or other sensitive details before posting it.

## Third-party components

MadNegar works with local speech engines and related tools, including [Whisper.cpp](https://github.com/ggml-org/whisper.cpp), [faster-whisper](https://github.com/SYSTRAN/faster-whisper), [Vosk](https://alphacephei.com/vosk/), and [Diba-STT](https://huggingface.co/Dibachain/Diba-STT). Review each component's license and the applicable model terms before redistributing models or binaries.
