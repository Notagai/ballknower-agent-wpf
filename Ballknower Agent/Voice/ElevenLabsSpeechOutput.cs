using System.Text;
using System.Text.Json;

namespace Ballknower.Voice;

public sealed class ElevenLabsSpeechOutput : ISpeechOutput
{
    private const string BaseUrl = "https://api.elevenlabs.io/v1";
    private readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(60) };
    private readonly Func<string?> _getApiKey;
    private readonly Func<string> _getVoiceId;
    private readonly Func<string> _getModel;
    private readonly AudioOutputService _audioOutput;

    public ElevenLabsSpeechOutput(
        Func<string?> getApiKey,
        Func<string> getVoiceId,
        Func<string> getModel,
        Func<string> getOutputDevice,
        Func<int> getVolume)
    {
        _getApiKey = getApiKey;
        _getVoiceId = getVoiceId;
        _getModel = getModel;
        _audioOutput = new AudioOutputService(getOutputDevice, getVolume);
    }

    public async Task SpeakAsync(string text, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        var key = _getApiKey();
        var voiceId = _getVoiceId();
        if (string.IsNullOrWhiteSpace(key))
            throw new InvalidOperationException("ElevenLabs API key is not configured.");
        if (string.IsNullOrWhiteSpace(voiceId))
            throw new InvalidOperationException("ElevenLabs voice is not configured.");

        var url = $"{BaseUrl}/text-to-speech/{Uri.EscapeDataString(voiceId)}/stream?output_format=mp3_44100_128";
        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Add("xi-api-key", key);
        request.Content = new StringContent(
            JsonSerializer.Serialize(new { text, model_id = _getModel() }),
            Encoding.UTF8,
            "application/json");

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        var body = response.IsSuccessStatusCode ? null : await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"ElevenLabs returned {(int)response.StatusCode}: {body}");

        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var memory = new MemoryStream();
        await source.CopyToAsync(memory, cancellationToken);
        memory.Position = 0;
        await _audioOutput.PlayMp3Async(memory, cancellationToken);
    }

    public async Task<IReadOnlyList<SpeechVoice>> GetVoicesAsync(CancellationToken cancellationToken = default)
    {
        var key = _getApiKey();
        if (string.IsNullOrWhiteSpace(key))
            throw new InvalidOperationException("Enter an ElevenLabs API key first.");

        using var request = new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}/voices");
        request.Headers.Add("xi-api-key", key);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"ElevenLabs returned {(int)response.StatusCode}: {body}");

        using var document = JsonDocument.Parse(body);
        return document.RootElement.TryGetProperty("voices", out var items)
            ? items.EnumerateArray()
                .Select(item => new SpeechVoice(
                    item.GetProperty("voice_id").GetString() ?? string.Empty,
                    item.GetProperty("name").GetString() ?? string.Empty))
                .Where(v => !string.IsNullOrWhiteSpace(v.Id) && !string.IsNullOrWhiteSpace(v.Name))
                .OrderBy(v => v.Name, StringComparer.OrdinalIgnoreCase)
                .ToList()
            : new List<SpeechVoice>();
    }

    public Task TestAsync(CancellationToken cancellationToken = default)
        => SpeakAsync("Hello. This is Ballknower's ElevenLabs voice test.", cancellationToken);

    public void Dispose()
    {
        _audioOutput.Dispose();
        _httpClient.Dispose();
    }
}
