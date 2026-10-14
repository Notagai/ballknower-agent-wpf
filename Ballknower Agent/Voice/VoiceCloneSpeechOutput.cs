using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace Ballknower.Voice;

public sealed class VoiceCloneSpeechOutput : ISpeechOutput
{
    private static readonly SemaphoreSlim RuntimeLock = new(1, 1);
    private readonly Func<string> _getReferencePath;
    private readonly Func<string> _getReferenceText;
    private readonly Func<bool> _getConsent;
    private readonly AudioOutputService _audioOutput;
    private readonly SemaphoreSlim _requestLock = new(1, 1);
    private Process? _worker;
    private StreamWriter? _workerInput;
    private StreamReader? _workerOutput;
    private bool _disposed;

    public VoiceCloneSpeechOutput(
        Func<string> getReferencePath,
        Func<string> getReferenceText,
        Func<bool> getConsent,
        Func<string> getOutputDevice,
        Func<int> getVolume)
    {
        _getReferencePath = getReferencePath;
        _getReferenceText = getReferenceText;
        _getConsent = getConsent;
        _audioOutput = new AudioOutputService(getOutputDevice, getVolume);
    }

    public async Task SpeakAsync(string text, CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(VoiceCloneSpeechOutput));
        if (string.IsNullOrWhiteSpace(text)) return;
        var reference = _getReferencePath();
        var referenceText = _getReferenceText();
        if (!_getConsent())
            throw new InvalidOperationException("Confirm that you own this voice or have the speaker's explicit permission in Speech settings.");
        if (string.IsNullOrWhiteSpace(reference) || !File.Exists(reference))
            throw new InvalidOperationException("Upload an audio or video reference clip in Speech settings first.");
        if (string.IsNullOrWhiteSpace(referenceText))
            throw new InvalidOperationException("Enter the exact words spoken in the reference clip in Speech settings.");

        await _requestLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        string? outputPath = null;
        try
        {
            await EnsureWorkerAsync(cancellationToken).ConfigureAwait(false);
            outputPath = Path.Combine(Path.GetTempPath(), "Ballknower", "VoiceClone", Guid.NewGuid().ToString("N") + ".wav");
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            var command = JsonSerializer.Serialize(new
            {
                action = "speak",
                text,
                reference,
                reference_text = referenceText,
                consent = _getConsent(),
                output = outputPath
            });
            await _workerInput!.WriteLineAsync(command.AsMemory(), cancellationToken).ConfigureAwait(false);
            await _workerInput.FlushAsync(cancellationToken).ConfigureAwait(false);
            var line = await _workerOutput!.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (line is null) throw new InvalidOperationException("Voice-cloning worker exited unexpectedly.");
            using var response = JsonDocument.Parse(line);
            var root = response.RootElement;
            if (!root.TryGetProperty("ok", out var ok) || !ok.GetBoolean())
            {
                var message = root.TryGetProperty("error", out var error) ? error.GetString() : null;
                throw new InvalidOperationException(message ?? "Voice cloning failed.");
            }
            if (!File.Exists(outputPath)) throw new InvalidOperationException("Voice cloning did not produce an audio file.");
            await _audioOutput.PlayWavFileAsync(outputPath, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            StopWorker();
            throw;
        }
        catch (Exception ex) when (ex is IOException or JsonException or InvalidOperationException)
        {
            throw new InvalidOperationException("Local voice cloning failed. Check the reference clip and Python runtime, then try again. " + ex.Message, ex);
        }
        finally
        {
            if (outputPath is not null)
            {
                try { if (File.Exists(outputPath)) File.Delete(outputPath); } catch { }
            }
            _requestLock.Release();
        }
    }

    public Task<IReadOnlyList<SpeechVoice>> GetVoicesAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<SpeechVoice> voices = new[] { new SpeechVoice("voice-clone-profile", "Imported voice profile") };
        return Task.FromResult(voices);
    }

    public Task TestAsync(CancellationToken cancellationToken = default) =>
        SpeakAsync("Hello! This is a voice cloning test from Ballknower.", cancellationToken);

    public void Stop()
    {
        _audioOutput.Stop();
        StopWorker();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        StopWorker();
        _audioOutput.Dispose();
    }

    private async Task EnsureWorkerAsync(CancellationToken cancellationToken)
    {
        if (_worker is { HasExited: false } && _workerOutput is not null) return;
        await RuntimeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_worker is { HasExited: false } && _workerOutput is not null) return;
            StopWorker();
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var runtimeDirectory = Path.Combine(appData, "Ballknower", "VoiceClone");
            var venvPython = Path.Combine(runtimeDirectory, "venv", "Scripts", "python.exe");
            Directory.CreateDirectory(runtimeDirectory);
            var script = Path.Combine(AppContext.BaseDirectory, "Voice", "voice_clone_worker.py");
            if (!File.Exists(script)) throw new FileNotFoundException("The voice-cloning worker script was not found beside Ballknower.", script);

            if (!File.Exists(venvPython))
            {
                var launcher = await FindPythonLauncherAsync(cancellationToken).ConfigureAwait(false);
                await RunProcessAsync(launcher.file, launcher.prefix.Concat(new[] { "-m", "venv", Path.Combine(runtimeDirectory, "venv") }), runtimeDirectory, cancellationToken).ConfigureAwait(false);
            }
            var check = await RunProcessCaptureAsync(venvPython, new[] { "-c", "import f5_tts, imageio_ffmpeg" }, runtimeDirectory, cancellationToken).ConfigureAwait(false);
            if (check.exitCode != 0)
            {
                await RunProcessAsync(venvPython, new[] { "-m", "pip", "install", "--upgrade", "pip" }, runtimeDirectory, cancellationToken).ConfigureAwait(false);
                await RunProcessAsync(venvPython, new[] { "-m", "pip", "install", "f5-tts", "imageio-ffmpeg" }, runtimeDirectory, cancellationToken).ConfigureAwait(false);
            }

            var start = new ProcessStartInfo
            {
                FileName = venvPython, WorkingDirectory = runtimeDirectory, UseShellExecute = false,
                RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true
            };
            start.ArgumentList.Add("-u");
            start.ArgumentList.Add(script);
            _worker = new Process { StartInfo = start, EnableRaisingEvents = true };
            _worker.Start();
            _workerInput = _worker.StandardInput;
            _workerOutput = _worker.StandardOutput;
            var process = _worker;
            _ = Task.Run(async () =>
            {
                try
                {
                    while (!process.HasExited)
                    {
                        var line = await process.StandardError.ReadLineAsync().ConfigureAwait(false);
                        if (line is null) break;
                        Ballknower.Diagnostics.AppLogger.Info("Voice clone: " + line);
                    }
                }
                catch { }
            });

            try
            {
                var line = await _workerOutput.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                if (line is null) throw new InvalidOperationException("Voice-cloning runtime exited before loading the model.");
                using var ready = JsonDocument.Parse(line);
                if (!ready.RootElement.TryGetProperty("ok", out var ok) || !ok.GetBoolean())
                {
                    var error = ready.RootElement.TryGetProperty("error", out var e) ? e.GetString() : null;
                    throw new InvalidOperationException("Voice-cloning model could not start: " + error);
                }
            }
            catch
            {
                StopWorker();
                throw;
            }
        }
        finally { RuntimeLock.Release(); }
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
            (file: "py", prefix: new[] { "-3.10" }),
            (file: "python", prefix: Array.Empty<string>())
        })
        {
            try
            {
                var args = candidate.prefix.Concat(new[] { "--version" }).ToArray();
                var result = await RunProcessCaptureAsync(candidate.file, args, null, cancellationToken).ConfigureAwait(false);
                if (result.exitCode == 0 && result.output.Contains("Python 3.10", StringComparison.OrdinalIgnoreCase))
                    return candidate;
            }
            catch { }
        }
        throw new InvalidOperationException("Local voice cloning requires Python 3.10. Install Python 3.10 for Windows; Ballknower will create a private environment and install the runtime automatically.");
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
