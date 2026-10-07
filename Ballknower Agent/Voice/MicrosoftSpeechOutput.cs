using System.Speech.Synthesis;

namespace Ballknower.Voice;

public sealed class MicrosoftSpeechOutput : ISpeechOutput
{
    public Task SpeakAsync(string text, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
            return Task.CompletedTask;

        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            using var synthesizer = new SpeechSynthesizer();
            synthesizer.SetOutputToDefaultAudioDevice();

            using var registration = cancellationToken.Register(() =>
            {
                try
                {
                    synthesizer.SpeakAsyncCancelAll();
                }
                catch
                {
                    // The synthesis task will observe cancellation below.
                }
            });

            synthesizer.Speak(text);
            cancellationToken.ThrowIfCancellationRequested();
        }, cancellationToken);
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

    public void Dispose()
    {
    }
}
