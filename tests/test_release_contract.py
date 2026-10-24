"""Cross-platform static checks for MadNegar release metadata and packaging safety.

These tests validate the release scripts' source contract only. They do not execute
PowerShell, dotnet publish, Inno Setup, or an installed Windows build.
"""
from pathlib import Path
import re
import unittest
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]

def text(rel: str) -> str:
    path = ROOT / rel
    # Production C# files live under src/; keep their logical names concise in tests.
    if not path.is_file() and path.suffix == ".cs" and len(Path(rel).parts) == 1:
        path = ROOT / "src" / rel
    return path.read_text(encoding="utf-8")

class ReleaseContractTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.project_xml = ET.parse(ROOT / "MadNegar.csproj").getroot()
        cls.version = cls.project_xml.findtext("PropertyGroup/Version")
        cls.release = text("build-release.ps1")
        cls.installer_ps = text("Build-Installer.ps1")
        cls.installer_cmd = text("Build-Installer.cmd")
        cls.release_cmd = text("Build-Release.cmd")
        cls.iss = text("installer/MadNegar.iss")
        cls.main = text("MainForm.cs")
        cls.settings = text("SettingsForm.cs")
        cls.runtime = text("FasterWhisperRuntime.cs")
        cls.readme = text("README.md")
        cls.readme_legacy = text("README_FA.md")

    def test_application_csharp_sources_are_under_src(self):
        source_dir = ROOT / "src"
        self.assertTrue(source_dir.is_dir())
        self.assertGreater(len(list(source_dir.glob("*.cs"))), 0)
        self.assertEqual(list(ROOT.glob("*.cs")), [])

    def test_project_version_is_1_0_1_release_baseline(self):
        self.assertEqual(self.version, "1.0.1")
        self.assertEqual(self.project_xml.findtext("PropertyGroup/AssemblyVersion"), "1.0.1.0")
        self.assertEqual(self.project_xml.findtext("PropertyGroup/FileVersion"), "1.0.1.0")
        self.assertEqual(self.project_xml.findtext("PropertyGroup/InformationalVersion"), "1.0.1")
        self.assertEqual(text("VERSION").strip(), self.version)

    def test_all_runtime_user_agents_match_project_version(self):
        expected = f'ProductInfoHeaderValue("MadNegar", "{self.version}")'
        self.assertIn(expected, self.main)
        self.assertIn(expected, self.settings)
        self.assertIn(expected, self.runtime)
        self.assertIn(f'MadNegar/{self.version}', self.main)

    def test_english_readme_documents_version_and_checksums(self):
        self.assertIn(self.version, self.readme)
        self.assertIn("SHA-256", self.readme)
        self.assertIn("latest release", self.readme.lower())

    def test_legacy_readme_path_redirects_to_maintained_english_guide(self):
        self.assertIn("[README.md](README.md)", self.readme_legacy)
        self.assertIn(self.version, self.readme_legacy)

    def test_build_release_cmd_uses_process_scoped_bypass_and_returns_exit_code(self):
        self.assertIn("build-release.ps1", self.release_cmd)
        self.assertIn("-ExecutionPolicy Bypass", self.release_cmd)
        self.assertIn('set "BUILD_EXIT_CODE=%ERRORLEVEL%"', self.release_cmd)
        self.assertIn("exit /b %BUILD_EXIT_CODE%", self.release_cmd)

    def test_build_installer_cmd_is_a_thin_launcher_and_returns_exit_code(self):
        self.assertIn('Build-Installer.ps1', self.installer_cmd)
        self.assertIn('set "BUILD_EXIT_CODE=%ERRORLEVEL%"', self.installer_cmd)
        self.assertIn('exit /b %BUILD_EXIT_CODE%', self.installer_cmd)
        self.assertNotIn('MadNegar-Setup-0.6.28.exe', self.installer_cmd)

    def test_build_installer_reads_project_version_not_hardcoded_cmd_version(self):
        self.assertIn("SelectSingleNode('/Project/PropertyGroup/Version')", self.installer_ps)
        self.assertIn('"/DMyAppVersion=$version"', self.installer_ps)
        self.assertNotIn('0.6.28', self.installer_ps)

    def test_inno_compiler_detection_supports_user_scope_registry_and_explicit_path(self):
        self.assertIn("$env:ISCC_PATH", self.installer_ps)
        self.assertIn("$env:INNO_SETUP_HOME", self.installer_ps)
        self.assertIn("$env:LOCALAPPDATA", self.installer_ps)
        self.assertIn("CurrentVersion\\Uninstall\\*", self.installer_ps)
        self.assertIn("HKCU:\\Software\\Microsoft\\Windows\\CurrentVersion\\App Paths\\ISCC.exe", self.installer_ps)
        self.assertIn("Write-Host \"Using Inno Setup compiler:", self.installer_ps)

    def test_missing_iscc_error_explains_explicit_override(self):
        self.assertIn("Set `$env:ISCC_PATH", self.installer_ps)
        self.assertIn("No existing release artifacts were changed", self.installer_ps)

    def test_installer_requires_dotnet_10_and_checks_native_exit_codes(self):
        self.assertIn("dotnet --list-sdks", self.installer_ps)
        self.assertIn("10\\.", self.installer_ps)
        self.assertIn("$publishExitCode = $LASTEXITCODE", self.installer_ps)
        self.assertIn("$isccExitCode = $LASTEXITCODE", self.installer_ps)
        self.assertIn("failed with exit code", self.installer_ps)

    def test_installer_compiles_from_staged_publish_output(self):
        self.assertIn(".release-staging", self.installer_ps)
        self.assertIn('"/DMyAppPublishDir=$stagePublish"', self.installer_ps)
        self.assertIn('"/O$stageOutput"', self.installer_ps)
        self.assertIn('Source: "{#MyAppPublishDir}\\MadNegar.exe"', self.iss)

    def test_installer_validates_executable_and_setup_outputs(self):
        self.assertIn("Test-WindowsExecutable -Path $stagedExe", self.installer_ps)
        self.assertIn("Test-WindowsExecutable -Path $stagedInstaller", self.installer_ps)
        self.assertIn("MadNegar-Setup-$version.exe", self.installer_ps)

    def test_installer_generates_sha256_sidecars_for_both_artifacts(self):
        self.assertIn("Get-FileHash -LiteralPath $stagedExe -Algorithm SHA256", self.installer_ps)
        self.assertIn("Get-FileHash -LiteralPath $stagedInstaller -Algorithm SHA256", self.installer_ps)
        self.assertIn("MadNegar.exe.sha256", self.installer_ps)
        self.assertIn("$finalInstallerHash", self.installer_ps)

    def test_installer_keeps_existing_outputs_until_both_build_steps_pass(self):
        self.assertIn("Existing release artifacts were not changed", self.installer_ps)
        self.assertIn("function Commit-ArtifactSet", self.installer_ps)
        self.assertIn("HadBackup", self.installer_ps)
        self.assertIn("Copy-Item -LiteralPath $item.Backup -Destination $item.Final -Force", self.installer_ps)
        self.assertNotIn("Remove-Item -LiteralPath $finalInstaller", self.installer_ps)

    def test_standalone_publish_uses_staging_and_checks_dotnet_exit_code(self):
        self.assertIn(".release-staging", self.release)
        self.assertIn("$LASTEXITCODE", self.release)
        self.assertIn("$publishExitCode -ne 0", self.release)
        self.assertIn("Test-WindowsExecutable -Path $stagedExe", self.release)
        self.assertIn("Commit-ArtifactSet", self.release)

    def test_failed_publish_does_not_delete_previous_release_executable_first(self):
        self.assertNotIn("Remove-Item -LiteralPath $expectedExe -Force", self.release)
        self.assertNotIn("del /q", self.installer_cmd.lower())

    def test_installer_macro_default_and_output_filename_are_consistent(self):
        self.assertIn(f'#define MyAppVersion "{self.version}"', self.iss)
        self.assertIn('OutputBaseFilename=MadNegar-Setup-{#MyAppVersion}', self.iss)
        self.assertIn('#define MyAppPublishDir "..\\publish"', self.iss)

    def test_staging_directory_is_ignored_by_git(self):
        self.assertIn('.release-staging/', text('.gitignore'))


    def test_dual_model_selectors_include_supported_downloaded_models_from_multiple_providers(self):
        self.assertIn("GetDownloadedDualModelChoices", self.settings)
        self.assertIn("ModelCatalog.Models.Select(model => new SpeechModelChoice(model, null))", self.settings)
        self.assertIn("SupportsLanguageRole(choice, \"fa\")", self.settings)
        self.assertIn("SupportsLanguageRole(choice, \"en\")", self.settings)
        self.assertIn("TranscribeDualModelsAcrossProvidersAsync", self.main)
        self.assertIn("AutoMixedPersianModelKey", text("AppSettings.cs"))
        self.assertIn("AutoMixedEnglishModelKey", text("AppSettings.cs"))

    def test_consolidated_roadmap_documents_status_and_acceptance_gaps(self):
        roadmap = text("docs/ROADMAP.md")
        manual = text("tests/MANUAL_ACCEPTANCE.md")
        self.assertIn("1.0.1", roadmap)
        self.assertIn("not a substitute for systematic evaluation", roadmap)
        self.assertIn("Visual Studio", roadmap)
        self.assertIn("WER/CER", manual)
        self.assertIn("dotnet build -c Release", manual)

if __name__ == "__main__":
    unittest.main(verbosity=2)
