# Source Tree and Build Guide

This repository contains the source code and build tooling for MadNegar on Windows. Most users do not need the source tree to run the app; download the released installer or standalone executable instead.

## Files to know

- **`src/*.cs`:** Application UI, settings, audio handling, model integration, transcript history, and text processing. Keeping production C# files in `src/` keeps the repository root easier to scan. The SDK-style project automatically includes these files during the build.
- **`MadNegar.csproj` and `MadNegar.ico`:** The .NET project definition and application icon.
- **`FasterWhisperAdapter.py`:** Python adapter embedded in the project for the faster-whisper integration.
- **Build scripts and installer definition:** `Build-Release.cmd`, `build-release.ps1`, `Build-Installer.cmd`, `Build-Installer.ps1`, and `installer/MadNegar.iss` build the executable and installer.
- **`Run-MadNegar.cmd`:** Launches the app from source in a development environment.
- **`tests/`:** Python tests, static source checks, C# structural checks, and the C# test harness.
- **`README.md`:** The main user guide, including downloads, engine support, installation, and build instructions.

## Where project status and release history live

Redundant reports from earlier development phases, generated test outputs, `PROGRESS_*` and `PHASE*_PROGRESS_*` files, overlapping test plans, and obsolete icon-preview assets were removed or consolidated from the clean source tree.

The current project status is maintained in [the roadmap](ROADMAP.md). Version-specific changes are documented in [the v1.0.1 release notes](RELEASE_NOTES_1.0.1.md) and [the v1.0.0 release notes](RELEASE_NOTES_1.0.0.md). The hands-on Windows test checklist is in [the manual acceptance test plan](../tests/MANUAL_ACCEPTANCE.md).

## Generated files

Directories and files such as `__pycache__/`, `*.pyc`, `bin/`, `obj/`, `publish/`, and `.release-staging/` contain generated or temporary output. They should not be included in a clean source submission. The repository's `.gitignore` is configured to keep these files from being added accidentally.
