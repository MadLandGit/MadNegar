using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Text;
using System.Net.Http.Headers;

namespace MadNegar;

/// <summary>
/// First-use setup and local process adapter for CTranslate2/faster-whisper.
/// UV manages a private Python 3.11 + venv inside %LOCALAPPDATA%\MadNegar so the
/// user does not need a system Python installation. Runtime/package/model inference is local.
/// </summary>
internal static class FasterWhisperRuntime
{
    private const string UvDownloadUrl = "https://github.com/astral-sh/uv/releases/latest/download/uv-x86_64-pc-windows-msvc.zip";
    private const string AdapterResourceName = "MadNegar.FasterWhisperAdapter.py";
    private static readonly HttpClient Http = CreateHttpClient();

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("MadNegar", "1.0.1"));
        client.DefaultRequestHeaders.Accept.ParseAdd("application/octet-stream, */*");
        return client;
    }

    private static string RuntimeDirectory(string root) => Path.Combine(root, "Runtime", "faster-whisper");
    private static string EnvironmentDirectory(string root) => Path.Combine(RuntimeDirectory(root), "venv");
    private static string PythonPath(string root) => Path.Combine(EnvironmentDirectory(root), "Scripts", "python.exe");
    private static string UvPath(string root) => Path.Combine(RuntimeDirectory(root), "uv.exe");
    private static string ReadyMarker(string root) => Path.Combine(EnvironmentDirectory(root), ".madnegar-faster-whisper-ready");
    private static string AdapterPath(string root) => Path.Combine(RuntimeDirectory(root), "FasterWhisperAdapter.py");

    public static bool IsReady(string root) => File.Exists(PythonPath(root)) && File.Exists(ReadyMarker(root));

    public static async Task EnsureReadyAsync(string root, IProgress<string>? status, CancellationToken token)
    {
        Directory.CreateDirectory(RuntimeDirectory(root));
        EnsureAdapterScript(root);

        var python = PythonPath(root);
        if (File.Exists(python) && await CheckImportAsync(python, token).ConfigureAwait(false))
        {
            try { File.WriteAllText(ReadyMarker(root), "ready"); } catch { }
            return;
        }

        status?.Report("Preparing private Python runtime for faster-whisper (one time)…");
        var uv = await EnsureUvAsync(root, status, token).ConfigureAwait(false);

        if (!File.Exists(python))
        {
            status?.Report("Downloading managed Python 3.11 runtime…");
            await RunProcessAsync(uv, ["python", "install", "3.11"], status, "Managed Python installation", token).ConfigureAwait(false);
            if (Directory.Exists(EnvironmentDirectory(root)))
            {
                try { Directory.Delete(EnvironmentDirectory(root), recursive: true); }
                catch (Exception ex) { throw new InvalidOperationException("Could not reset the private faster-whisper environment. Close any other MadNegar instance and retry. " + ex.Message, ex); }
            }
            status?.Report("Creating the private faster-whisper environment…");
            await RunProcessAsync(uv, ["venv", "--python", "3.11", "--managed-python", EnvironmentDirectory(root)], status, "Python environment setup", token).ConfigureAwait(false);
        }

        if (!File.Exists(python))
            throw new FileNotFoundException("The private Python environment was not created. Please retry with an internet connection.", python);

        status?.Report("Installing faster-whisper and its local inference dependencies (one time)…");
        await RunProcessAsync(uv, ["pip", "install", "--python", python, "faster-whisper"], status, "faster-whisper package installation", token).ConfigureAwait(false);
        if (!await CheckImportAsync(python, token).ConfigureAwait(false))
            throw new InvalidOperationException("faster-whisper was installed but could not be imported by its private Python runtime. See the install output above and retry.");

        File.WriteAllText(ReadyMarker(root), "ready");
    }

    public static async Task<string> TranscribeAsync(
        string root, string modelDirectory, string audioPath, string? language, string? autoLanguageStrategy,
        IProgress<string>? status, CancellationToken token, string? englishModelDirectory = null,
        bool preserveEnglishTechnicalTerms = true, string? transcriptionLanguagePreference = null)
    {
        await EnsureReadyAsync(root, status, token).ConfigureAwait(false);
        var python = PythonPath(root);
        var script = EnsureAdapterScript(root);
        var lang = string.IsNullOrWhiteSpace(language) ? "auto" : language;
        var strategy = AutoLanguageText.NormalizeStrategy(autoLanguageStrategy);
        var preference = AutoLanguageText.NormalizeTranscriptionLanguagePreference(transcriptionLanguagePreference);
        if (lang.Equals("auto", StringComparison.OrdinalIgnoreCase) && strategy == AutoLanguageText.DualModelMixed &&
            string.IsNullOrWhiteSpace(englishModelDirectory))
            throw new InvalidOperationException("Dual-model mixed Auto needs both local model directories. Choose a Persian model and a different English model in Settings → Transcription.");
        status?.Report(strategy == AutoLanguageText.DualModelMixed && lang.Equals("auto", StringComparison.OrdinalIgnoreCase)
            ? "Transcribing locally with dual-model mixed Persian/English mode (CPU int8; slower)…"
            : "Transcribing locally with faster-whisper (CPU int8)…");
        // Keep positional adapter arguments stable by always supplying an empty model placeholder
        // when no second model is selected, followed by the explicit term-preservation switch.
        var arguments = new List<string>
        {
            script, modelDirectory, audioPath, lang, strategy,
            englishModelDirectory ?? string.Empty,
            preserveEnglishTechnicalTerms ? "true" : "false",
            preference
        };
        var result = await RunProcessAsync(
            python,
            arguments,
            status,
            "faster-whisper transcription",
            token,
            captureOutput: true).ConfigureAwait(false);
        return result.StandardOutput.Trim();
    }

    private static string EnsureAdapterScript(string root)
    {
        var path = AdapterPath(root);
        Directory.CreateDirectory(RuntimeDirectory(root));

        using var resource = Assembly.GetExecutingAssembly().GetManifestResourceStream(AdapterResourceName)
            ?? throw new InvalidOperationException("The embedded faster-whisper adapter script is missing from this MadNegar build.");
        using var memory = new MemoryStream();
        resource.CopyTo(memory);
        var embeddedBytes = memory.ToArray();

        // The adapter lives in LocalAppData across app upgrades. Replace it when the
        // embedded script changes, otherwise users keep the older cp1252-broken copy.
        try
        {
            if (File.Exists(path) && File.ReadAllBytes(path).AsSpan().SequenceEqual(embeddedBytes))
                return path;
        }
        catch { /* Replace an unreadable/stale adapter below. */ }

        var temporaryPath = path + ".tmp";
        File.WriteAllBytes(temporaryPath, embeddedBytes);
        if (File.Exists(path))
            File.Move(temporaryPath, path, overwrite: true);
        else
            File.Move(temporaryPath, path);
        return path;
    }

    private static async Task<string> EnsureUvAsync(string root, IProgress<string>? status, CancellationToken token)
    {
        var uv = UvPath(root);
        if (File.Exists(uv)) return uv;

        status?.Report("Downloading the small uv Python manager…");
        var zipPath = Path.Combine(RuntimeDirectory(root), "uv-download.zip");
        var stage = Path.Combine(RuntimeDirectory(root), "uv-extract");
        try
        {
            using (var response = await Http.GetAsync(UvDownloadUrl, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();
                await using var source = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
                await using var target = new FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.None, 128 * 1024, useAsync: true);
                await source.CopyToAsync(target, 128 * 1024, token).ConfigureAwait(false);
            }
            if (Directory.Exists(stage)) Directory.Delete(stage, recursive: true);
            Directory.CreateDirectory(stage);
            SafeZipExtractor.ExtractToDirectory(zipPath, stage);
            var found = Directory.EnumerateFiles(stage, "uv.exe", SearchOption.AllDirectories).FirstOrDefault();
            if (found is null) throw new InvalidDataException("The uv download did not contain uv.exe.");
            File.Copy(found, uv, overwrite: true);
            return uv;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            throw new InvalidOperationException("MadNegar could not download the Python runtime manager required by faster-whisper. Check access to GitHub Releases and retry. " + ex.Message, ex);
        }
        finally
        {
            try { if (File.Exists(zipPath)) File.Delete(zipPath); } catch { }
            try { if (Directory.Exists(stage)) Directory.Delete(stage, recursive: true); } catch { }
        }
    }

    private static async Task<bool> CheckImportAsync(string python, CancellationToken token)
    {
        try
        {
            var result = await RunProcessAsync(python, ["-c", "import faster_whisper; print('ok')"], null, "Python package check", token, captureOutput: true, throwOnFailure: false).ConfigureAwait(false);
            return result.ExitCode == 0 && result.StandardOutput.Contains("ok", StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    private sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError);

    private static async Task<ProcessResult> RunProcessAsync(
        string executable, IReadOnlyList<string> arguments, IProgress<string>? status,
        string stageName, CancellationToken token, bool captureOutput = false, bool throwOnFailure = true)
    {
        var executableDirectory = Path.GetDirectoryName(executable) ?? Environment.CurrentDirectory;
        var start = new ProcessStartInfo
        {
            FileName = executable,
            WorkingDirectory = executableDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        if (string.Equals(Path.GetFileName(executable), "uv.exe", StringComparison.OrdinalIgnoreCase))
        {
            // Keep uv-managed Python and its cache inside MadNegar's own data directory.
            start.Environment["UV_PYTHON_INSTALL_DIR"] = Path.Combine(executableDirectory, "managed-python");
            start.Environment["UV_CACHE_DIR"] = Path.Combine(executableDirectory, "uv-cache");
        }
        if (Path.GetFileName(executable).StartsWith("python", StringComparison.OrdinalIgnoreCase))
        {
            // Ensure transcript output and tracebacks use UTF-8 even on Windows locales
            // whose default console encoding (for example cp1252) cannot represent Persian.
            start.Environment["PYTHONUTF8"] = "1";
            start.Environment["PYTHONIOENCODING"] = "utf-8";
        }
        foreach (var argument in arguments) start.ArgumentList.Add(argument);

        using var process = new Process { StartInfo = start, EnableRaisingEvents = true };
        try
        {
            if (!process.Start()) throw new InvalidOperationException($"Could not start {Path.GetFileName(executable)}.");
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Could not start {stageName}. {ex.Message}", ex);
        }

        try
        {
            var stdoutTask = process.StandardOutput.ReadToEndAsync(token);
            var stderrTask = process.StandardError.ReadToEndAsync(token);
            await process.WaitForExitAsync(token).ConfigureAwait(false);
            var stdout = await stdoutTask.ConfigureAwait(false);
            var stderr = await stderrTask.ConfigureAwait(false);
            var result = new ProcessResult(process.ExitCode, stdout, stderr);
            if (throwOnFailure && process.ExitCode != 0)
            {
                var details = string.Join(Environment.NewLine, new[] { stderr.Trim(), stdout.Trim() }.Where(s => !string.IsNullOrWhiteSpace(s)));
                if (details.Length > 5000) details = details[^5000..];
                throw new InvalidOperationException($"{stageName} failed with exit code {process.ExitCode}.\n{details}");
            }
            return result;
        }
        catch (OperationCanceledException)
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { }
            throw;
        }
    }
}
