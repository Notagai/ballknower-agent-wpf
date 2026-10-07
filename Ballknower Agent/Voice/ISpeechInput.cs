namespace Ballknower.Voice;

public interface ISpeechInput : IDisposable
{
    Task<string?> RecognizeAsync(CancellationToken cancellationToken = default);
}
