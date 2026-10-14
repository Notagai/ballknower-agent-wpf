using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace Ballknower.Voice;

public sealed class KittenTts2SpeechOutput : ISpeechOutput
{
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

    public KittenTts2SpeechOutput(
        Func<string> getVoice,
        Func<string> getOutputDevice,
        Func<int> getVolume)
    {
        _getVoice = getVoice;
        _audioOutput = new AudioOutputService(getOutputDevice, getVolume);
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
            outputPath = Path.Combine(Path.GetTempPath(), "Ballknower", "KittenTTS2", Guid.NewGuid().ToString("N") + ".wav");
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            await _workerInput!.WriteLineAsync(JsonSerializer.Serialize(new
            {
                action = "speak",
                text,
                voice = NormalizeVoice(_getVoice()),
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
        IReadOnlyList<SpeechVoice> voices = KnownVoices
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

            if (!File.Exists(venvPython))
            {
                var pythonLauncher = await FindPythonLauncherAsync(cancellationToken).ConfigureAwait(false);
                await RunProcessAsync(pythonLauncher.file, pythonLauncher.prefix.Concat(new[]
                {
                    "-m", "venv", Path.Combine(runtimeDirectory, "venv")
                }), runtimeDirectory, cancellationToken).ConfigureAwait(false);
            }

            var importCheck = await RunProcessCaptureAsync(venvPython, new[] { "-c", "import kittenml, soundfile" }, runtimeDirectory, cancellationToken).ConfigureAwait(false);
            if (importCheck.exitCode != 0)
            {
                await RunProcessAsync(venvPython, new[] { "-m", "pip", "install", "--upgrade", "pip" }, runtimeDirectory, cancellationToken).ConfigureAwait(false);
                await RunProcessAsync(venvPython, new[] { "-m", "pip", "install", "kittenml" }, runtimeDirectory, cancellationToken).ConfigureAwait(false);
            }

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
                var readyLine = await _workerOutput.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                if (readyLine is null)
                {
                    var exitCode = _worker.HasExited ? _worker.ExitCode.ToString() : "unknown";
                    throw new InvalidOperationException("KittenTTS 2 failed to start (exit code " + exitCode + "). Check the app log for details.");
                }

                using var ready = JsonDocument.Parse(readyLine);
                if (!ready.RootElement.TryGetProperty("ok", out var readyOk) || !readyOk.GetBoolean())
                    throw new InvalidOperationException("KittenTTS 2 could not load its model: " + ReadWorkerError(ready.RootElement));
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
                var result = await RunProcessCaptureAsync(candidate.file, candidate.prefix.Concat(new[] { "--version" }), null, cancellationToken).ConfigureAwait(false);
                if (result.exitCode == 0) return candidate;
            }
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
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        return (process.ExitCode, (await stdout.ConfigureAwait(false)) + Environment.NewLine + (await stderr.ConfigureAwait(false)));
    }
}
