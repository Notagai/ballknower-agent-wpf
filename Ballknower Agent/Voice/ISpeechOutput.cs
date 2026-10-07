namespace Ballknower.Voice;

public interface ISpeechOutput : IDisposable
{
    Task SpeakAsync(string text, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SpeechVoice>> GetVoicesAsync(CancellationToken cancellationToken = default);
    Task TestAsync(CancellationToken cancellationToken = default);
}

public sealed record SpeechVoice(string Id, string Name);
