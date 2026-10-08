using System.Speech.Recognition;

namespace Ballknower.Voice;

public sealed class MicrosoftSpeechInput : ISpeechInput
{
    private SpeechRecognitionEngine? _recognizer;

    public Task<string?> RecognizeAsync(CancellationToken cancellationToken = default)
        => Task.Run(() => Recognize(cancellationToken), cancellationToken);

    private string? Recognize(CancellationToken cancellationToken)
    {
        using var recognizer = new SpeechRecognitionEngine();
        recognizer.LoadGrammar(new DictationGrammar());
        recognizer.InitialSilenceTimeout = TimeSpan.FromSeconds(5);
        recognizer.BabbleTimeout = TimeSpan.FromSeconds(5);
        recognizer.SetInputToDefaultAudioDevice();

        using var completed = new ManualResetEventSlim(false);
        RecognitionResult? result = null;
        Exception? error = null;

        recognizer.SpeechRecognized += (_, args) =>
        {
            if (args.Result.Confidence >= 0.35)
                result = args.Result;
        };
        recognizer.RecognizeCompleted += (_, args) =>
        {
            error = args.Error;
            completed.Set();
        };

        _recognizer = recognizer;
        using var registration = cancellationToken.Register(() =>
        {
            try { recognizer.RecognizeAsyncCancel(); } catch { }
            completed.Set();
        });

        recognizer.RecognizeAsync(RecognizeMode.Single);
        completed.Wait();
        _recognizer = null;

        if (cancellationToken.IsCancellationRequested)
            return null;
        if (error is not null)
            throw new InvalidOperationException("Microsoft speech recognition failed.", error);

        return result?.Text?.Trim();
    }

    public void Dispose()
    {
        try { _recognizer?.RecognizeAsyncCancel(); } catch { }
        _recognizer = null;
    }
}
