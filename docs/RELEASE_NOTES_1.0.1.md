# MadNegar v1.0.1 — Persian Half-Space Boundary Fix

MadNegar v1.0.1 is a focused patch release that fixes a technical-term replacement bug involving Persian compound words and the zero-width non-joiner (ZWNJ, U+200C). It does not add new speech-recognition engines or materially change the app's core transcription behavior.

## What's changed

- The Persian half-space is now treated as part of the surrounding compound word when checking word boundaries. For example, `گیت‌سازی` and `پایتون‌کار` should not be changed to `Git‌سازی` or `Python‌کار`.
- The fix is applied in both the C# text-processing path and the Python adapter used by faster-whisper, keeping their behavior aligned.
- Full technical names are still replaced as intended. For example, `گیت‌هاب` can normalize to `GitHub`, and `ویژوال‌استودیو` can normalize to `Visual Studio`.
- Regression tests cover both sides of the boundary: they prevent incorrect replacement inside compound words and confirm that complete technical names are still recognized when half-spaces are present.

## Downloads

- **Recommended installer:** [MadNegar-Setup-1.0.1.exe](https://github.com/MadLandGit/MadNegar/releases/download/v1.0.1/MadNegar-Setup-1.0.1.exe)
- **Standalone executable:** [MadNegar.exe](https://github.com/MadLandGit/MadNegar/releases/download/v1.0.1/MadNegar.exe)
- SHA-256 checksum files are provided alongside both downloads so you can verify file integrity.
- [README, installation instructions, and build guide](https://github.com/MadLandGit/MadNegar/blob/main/README.md)

## Release validation

The v1.0.1 release workflow completed the following checks successfully:

- 243 static source-regression checks, including the new half-space boundary checks.
- 66 Python tests.
- 103 of 103 checks in the C# text, language, dictionary, history, and export harness.
- A Windows build using .NET 10.
- Standalone executable and installer builds, with SHA-256 checksums verified.

These checks validate the targeted fix and the release packaging, but they do not guarantee transcription accuracy for every model or recording environment. Re-test with representative recordings before relying on the updated version for important work.

## Compatibility notes

- MadNegar targets Windows 10 and Windows 11 (x64).
- Speech models are not included in the installer and must be selected or downloaded separately.
- This release focuses on technical-term replacement boundaries. Overall ASR behavior and speech-engine support remain substantially unchanged from v1.0.0.
