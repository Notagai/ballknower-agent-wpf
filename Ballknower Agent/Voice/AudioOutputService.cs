using System.IO;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace Ballknower.Voice;

public sealed class AudioOutputService : IDisposable
{
    private readonly Func<string> _getDevice;
    private readonly Func<int> _getVolume;
    private readonly object _sync = new();
    private WasapiOut? _output;

    public AudioOutputService(Func<string> getDevice, Func<int> getVolume)
    {
        _getDevice = getDevice;
        _getVolume = getVolume;
    }

    public async Task PlayMp3Async(Stream mp3Stream, CancellationToken cancellationToken = default)
    {
        await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var reader = new Mp3FileReader(mp3Stream);
            using var enumerator = new MMDeviceEnumerator();
            var selected = _getDevice();
            var device = string.IsNullOrWhiteSpace(selected)
                ? null
                : enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active)
                    .FirstOrDefault(d => string.Equals(d.FriendlyName, selected, StringComparison.OrdinalIgnoreCase));

            var output = device is null
                ? new WasapiOut(AudioClientShareMode.Shared, true, 100)
                : new WasapiOut(device, AudioClientShareMode.Shared, true, 100);

            // Apply the app volume as a per-sample gain multiplier. Do not set the
            // output/session/device volume, so Windows master volume is never changed.
            var volumeProvider = new VolumeSampleProvider(reader.ToSampleProvider())
            {
                Volume = Math.Clamp(_getVolume(), 0, 100) / 100f
            };
            output.Init(new SampleToWaveProvider(volumeProvider));
            lock (_sync)
            {
                _output?.Stop();
                _output?.Dispose();
                _output = output;
            }
            output.Play();

            try
            {
                while (output.PlaybackState == PlaybackState.Playing)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    Thread.Sleep(20);
                }
            }
            finally
            {
                lock (_sync)
                {
                    if (ReferenceEquals(_output, output))
                        _output = null;
                }
                output.Dispose();
            }
        }, cancellationToken);
    }

    public void Stop()
    {
        lock (_sync)
        {
            _output?.Stop();
            _output?.Dispose();
            _output = null;
        }
    }

    public static IReadOnlyList<string> GetOutputDevices()
    {
        using var enumerator = new MMDeviceEnumerator();
        return enumerator
            .EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active)
            .Select(d => d.FriendlyName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public void Dispose() => Stop();
}
