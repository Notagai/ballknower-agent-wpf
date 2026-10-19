using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace Ballknower.Voice;

public sealed class KittenTts2SpeechOutput : ISpeechOutput
{
    public event Action<string>? ProgressChanged;

    private void ReportProgress(string message)
    {
        Ballknower.Diagnostics.AppLogger.Info("KittenTTS 2 setup: " + message);
        ProgressChanged?.Invoke(message);
    }

    private static readonly string[] KnownVoices =
    {
        "Alfred", "Arabic", "Bella", "Bruno", "Chinese", "Claire", "Dante",
        "Diana", "Dolores", "Eleanor", "Edith", "Elliot", "Frank", "French", "German", "Grace", "Herbert", "Hindi",
        "Hugo", "Iris", "Italian", "Jasper", "Julian", "Kiki", "Laurence",
        "Leo", "Luna", "Maeve", "Marcus", "Martha", "Matthew", "Miles",
        "Otis", "Portuguese", "Raven", "Reginald", "Rosie", "Russian", "Sable",
        "Saoirse", "Serena", "Spanish", "Victor", "Victoria", "Vincent",
        "Walter", "Willow"
    };

    private static readonly SemaphoreSlim RuntimeLock = new(1, 1);
    private readonly AudioOutputService _audioOutput;
    private readonly Func<string> _getVoice;
    private readonly SemaphoreSlim _requestLock = new(1, 1);
    private Process? _worker;
    private StreamWriter? _workerInput;
    private StreamReader? _workerOutput;
    private bool _disposed;
    private volatile bool _isRequestInProgress;
    private IReadOnlyList<string> _availableVoices = KnownVoices;

    public KittenTts2SpeechOutput(
        Func<string> getVoice,
        Func<string> getOutputDevice,
        Func<int> getVolume)
    {
        _getVoice = getVoice;
        _audioOutput = new AudioOutputService(getOutputDevice, getVolume);
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(KittenTts2SpeechOutput));
        await EnsureWorkerAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task SpeakAsync(string text, CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(KittenTts2SpeechOutput));
        if (string.IsNullOrWhiteSpace(text)) return;
        await _requestLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        _isRequestInProgress = true;
        string? outputPath = null;
        try
        {
            await EnsureWorkerAsync(cancellationToken).ConfigureAwait(false);
            ReportProgress("Generating test speech…");
            outputPath = Path.Combine(Path.GetTempPath(), "Ballknower", "KittenTTS2", Guid.NewGuid().ToString("N") + ".wav");
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            await _workerInput!.WriteLineAsync(JsonSerializer.Serialize(new
            {
                action = "speak",
                text,
                voice = _availableVoices.FirstOrDefault(v =>
                    string.Equals(v, NormalizeVoice(_getVoice()), StringComparison.OrdinalIgnoreCase))
                    ?? _availableVoices.FirstOrDefault()
                    ?? "Luna",
                output = outputPath
            }).AsMemory(), cancellationToken).ConfigureAwait(false);
            await _workerInput.FlushAsync(cancellationToken).ConfigureAwait(false);

            var line = await _workerOutput!.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (line is null) throw new InvalidOperationException("KittenTTS 2 worker exited unexpectedly.");
            using var response = JsonDocument.Parse(line);
            var root = response.RootElement;
            if (!root.TryGetProperty("ok", out var ok) || !ok.GetBoolean())
                throw new InvalidOperationException(ReadWorkerError(root));
            if (!File.Exists(outputPath))
                throw new InvalidOperationException("KittenTTS 2 did not produce an audio file.");

            await _audioOutput.PlayWavFileAsync(outputPath, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            StopWorker();
            throw;
        }
        catch (Exception ex) when (ex is IOException or JsonException or InvalidOperationException)
        {
            if (_worker is { HasExited: true }) StopWorker();
            throw new InvalidOperationException(
                "KittenTTS 2 could not generate speech. Check that Python and the KittenTTS 2 runtime are installed, then try again. " + ex.Message, ex);
        }
        finally
        {
            if (outputPath is not null)
            {
                try { if (File.Exists(outputPath)) File.Delete(outputPath); } catch { }
            }
            _isRequestInProgress = false;
            _requestLock.Release();
        }
    }

    public Task<IReadOnlyList<SpeechVoice>> GetVoicesAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<SpeechVoice> voices = _availableVoices
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(v => v, StringComparer.OrdinalIgnoreCase)
            .Select(v => new SpeechVoice(v, v))
            .ToList();
        return Task.FromResult(voices);
    }

    public Task TestAsync(CancellationToken cancellationToken = default)
        => SpeakAsync("Hello! This is Ballknower speaking with KittenTTS 2.", cancellationToken);

    public void Stop()
    {
        _audioOutput.Stop();
        if (_isRequestInProgress) StopWorker();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _audioOutput.Stop();
        StopWorker();
        _audioOutput.Dispose();
    }

    private static string NormalizeVoice(string? voice) =>
        string.IsNullOrWhiteSpace(voice) ? "Luna" : voice.Trim();

    private static string ReadWorkerError(JsonElement root) =>
        root.TryGetProperty("error", out var error) ? error.GetString() ?? "Unknown worker error." : "Unknown worker error.";

    private async Task EnsureWorkerAsync(CancellationToken cancellationToken)
    {
        if (_worker is { HasExited: false } && _workerOutput is not null) return;
        await RuntimeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_worker is { HasExited: false } && _workerOutput is not null) return;
            StopWorker();

            var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var runtimeDirectory = Path.Combine(appData, "Ballknower", "KittenTTS2");
            var venvPython = Path.Combine(runtimeDirectory, "venv", "Scripts", "python.exe");
            Directory.CreateDirectory(runtimeDirectory);
            var workerScript = Path.Combine(AppContext.BaseDirectory, "Voice", "kittentts2_worker.py");
            if (!File.Exists(workerScript))
                throw new FileNotFoundException("The KittenTTS 2 worker script was not found beside Ballknower.", workerScript);

            ReportProgress("Checking the local Python environment…");
            if (!File.Exists(venvPython))
            {
                ReportProgress("Python environment not found. Creating a private environment…");
                var pythonLauncher = await FindPythonLauncherAsync(cancellationToken).ConfigureAwait(false);
                ReportProgress("Creating KittenTTS 2's private Python environment…");
                await RunProcessAsync(pythonLauncher.file, pythonLauncher.prefix.Concat(new[]
                {
                    "-m", "venv", Path.Combine(runtimeDirectory, "venv")
                }), runtimeDirectory, cancellationToken).ConfigureAwait(false);
            }

            ReportProgress("Checking installed KittenTTS 2 dependencies…");
            var importCheck = await RunProcessCaptureAsync(venvPython, new[] { "-c", "import kittenml, soundfile" }, runtimeDirectory, cancellationToken).ConfigureAwait(false);
            if (importCheck.exitCode != 0)
            {
                ReportProgress("Dependencies are missing. Installing KittenTTS 2 and required packages…");
                await RunProcessAsync(venvPython, new[] { "-m", "pip", "install", "--upgrade", "pip" }, runtimeDirectory, cancellationToken).ConfigureAwait(false);
                await RunProcessAsync(venvPython, new[] { "-m", "pip", "install", "--upgrade", "kittenml", "soundfile" }, runtimeDirectory, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                ReportProgress("Dependencies are installed. Checking for package updates…");
                // pip's upgrade check ensures an older KittenML runtime is not silently reused.
                await RunProcessAsync(venvPython, new[] { "-m", "pip", "install", "--upgrade", "kittenml" }, runtimeDirectory, cancellationToken).ConfigureAwait(false);
            }

            ReportProgress("Loading KittenTTS 2 model (downloading model files if needed)…");
            var start = new ProcessStartInfo
            {
                FileName = venvPython,
                WorkingDirectory = runtimeDirectory,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            start.ArgumentList.Add("-u");
            start.ArgumentList.Add(workerScript);
            _worker = new Process { StartInfo = start, EnableRaisingEvents = true };
            _worker.Start();
            _workerInput = _worker.StandardInput;
            _workerOutput = _worker.StandardOutput;
            _ = Task.Run(async () =>
            {
                try
                {
                    while (_worker is { HasExited: false })
                    {
                        var logLine = await _worker.StandardError.ReadLineAsync().ConfigureAwait(false);
                        if (logLine is null) break;
                        Ballknower.Diagnostics.AppLogger.Info("KittenTTS 2: " + logLine);
                    }
                }
                catch { }
            });

            try
            {
                using var startupTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                startupTimeout.CancelAfter(TimeSpan.FromMinutes(10));
                string? readyLine;
                try
                {
                    readyLine = await _workerOutput.ReadLineAsync(startupTimeout.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    throw new TimeoutException("KittenTTS 2 did not finish loading within 10 minutes. Check the app log, available memory, and internet connection.");
                }

                if (readyLine is null)
                {
                    var exitCode = _worker.HasExited ? _worker.ExitCode.ToString() : "unknown";
                    throw new InvalidOperationException("KittenTTS 2 failed to start (exit code " + exitCode + "). Check the app log for details.");
                }

                using var ready = JsonDocument.Parse(readyLine);
                if (!ready.RootElement.TryGetProperty("ok", out var readyOk) || !readyOk.GetBoolean())
                    throw new InvalidOperationException("KittenTTS 2 could not load its model: " + ReadWorkerError(ready.RootElement));

                if (ready.RootElement.TryGetProperty("voices", out var voicesElement) &&
                    voicesElement.ValueKind == JsonValueKind.Array)
                {
                    var voices = voicesElement.EnumerateArray()
                        .Select(v => v.GetString())
                        .Where(v => !string.IsNullOrWhiteSpace(v))
                        .Select(v => v!)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();
                    if (voices.Count > 0) _availableVoices = voices;
                }
            }
            catch
            {
                StopWorker();
                throw;
            }
        }
        finally
        {
            RuntimeLock.Release();
        }
    }

    private void StopWorker()
    {
        try { if (_worker is { HasExited: false }) _worker.Kill(true); } catch { }
        try { _worker?.Dispose(); } catch { }
        _worker = null;
        _workerInput = null;
        _workerOutput = null;
    }

    private static async Task<(string file, string[] prefix)> FindPythonLauncherAsync(CancellationToken cancellationToken)
    {
        foreach (var candidate in new[]
        {
            (file: "py", prefix: new[] { "-3" }),
            (file: "python", prefix: Array.Empty<string>())
        })
        {
            try
            {
                var result = await RunProcessCaptureAsync(
                    candidate.file,
                    candidate.prefix.Concat(new[] { "-c", "import sys; print(f'{sys.version_info.major}.{sys.version_info.minor}')"}),
                    null,
                    cancellationToken).ConfigureAwait(false);
                var versionText = result.output.Trim().Split(new[] { '\\r', '\\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
                var parts = versionText?.Split('.');
                if (result.exitCode == 0 && parts is { Length: >= 2 } &&
                    int.TryParse(parts[0], out var major) &&
                    int.TryParse(parts[1], out var minor) &&
                    (major > 3 || (major == 3 && minor >= 9)))
                    return candidate;
            }
            catch (OperationCanceledException) { throw; }
            catch { }
        }
        throw new InvalidOperationException("Python 3.9 or newer is required for local KittenTTS 2. Install Python for Windows, then restart Ballknower. Ballknower will install its private KittenTTS 2 runtime automatically.");
    }

    private static async Task RunProcessAsync(string file, IEnumerable<string> arguments, string? workingDirectory, CancellationToken cancellationToken)
    {
        var result = await RunProcessCaptureAsync(file, arguments, workingDirectory, cancellationToken).ConfigureAwait(false);
        if (result.exitCode != 0)
            throw new InvalidOperationException(file + " failed (" + result.exitCode + "): " + (result.output.Length > 2500 ? result.output[^2500..] : result.output));
    }

    private static async Task<(int exitCode, string output)> RunProcessCaptureAsync(string file, IEnumerable<string> arguments, string? workingDirectory, CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo { FileName = file, WorkingDirectory = workingDirectory ?? Environment.CurrentDirectory, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start " + file + ".");
        var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(10));
        try
        {
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try { if (!process.HasExited) process.Kill(true); } catch { }
            if (cancellationToken.IsCancellationRequested) throw;
            throw new TimeoutException(file + " did not finish within 10 minutes.");
        }
        return (process.ExitCode, (await stdout.ConfigureAwait(false)) + Environment.NewLine + (await stderr.ConfigureAwait(false)));
    }
}
