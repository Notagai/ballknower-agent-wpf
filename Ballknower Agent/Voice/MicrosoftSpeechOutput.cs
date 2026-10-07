using System.Speech.Synthesis;

namespace Ballknower.Voice;

public sealed class MicrosoftSpeechOutput : ISpeechOutput
{
    private readonly object _sync = new();
    private SpeechSynthesizer? _synthesizer;

    public async Task SpeakAsync(string text, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;

        cancellationToken.ThrowIfCancellationRequested();

        using var synthesizer = new SpeechSynthesizer();
        synthesizer.SetOutputToDefaultAudioDevice();

        lock (_sync)
        {
            _synthesizer?.SpeakAsyncCancelAll();
            _synthesizer = synthesizer;
        }

        try
        {
            var completion = new TaskCompletionSource<object?>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            EventHandler<SpeakCompletedEventArgs>? handler = null;
            handler = (_, args) =>
            {
                synthesizer.SpeakCompleted -= handler;
                if (args.Error is not null)
                    completion.TrySetException(args.Error);
                else if (args.Cancelled)
                    completion.TrySetCanceled();
                else
                    completion.TrySetResult(null);
            };

            synthesizer.SpeakCompleted += handler;
            using var registration = cancellationToken.Register(() =>
            {
                try { synthesizer.SpeakAsyncCancelAll(); } catch { }
            });

            synthesizer.SpeakAsync(text);
            await completion.Task.ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
        }
        finally
        {
            lock (_sync)
            {
                if (ReferenceEquals(_synthesizer, synthesizer))
                    _synthesizer = null;
            }
        }
    }

    public Task<IReadOnlyList<SpeechVoice>> GetVoicesAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var synthesizer = new SpeechSynthesizer();

        IReadOnlyList<SpeechVoice> voices = synthesizer
            .GetInstalledVoices()
            .Where(v => v.Enabled)
            .Select(v => new SpeechVoice(
                v.VoiceInfo.Name,
                v.VoiceInfo.Culture.DisplayName))
            .OrderBy(v => v.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return Task.FromResult(voices);
    }

    public Task TestAsync(CancellationToken cancellationToken = default)
        => SpeakAsync(
            "Hello. This is Ballknower using Microsoft Windows text to speech.",
            cancellationToken);

    public void Stop()
    {
        lock (_sync)
        {
            try { _synthesizer?.SpeakAsyncCancelAll(); } catch { }
        }
    }

    public void Dispose()
    {
        Stop();
    }
}
