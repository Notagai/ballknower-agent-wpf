using System.IO;
using System.Net.Http;
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


    private static string NormalizeApiKey(string? key)
    {
        var normalized = (key ?? string.Empty).Trim().Trim('"', '\'');
        if (normalized.StartsWith("xi-api-key:", StringComparison.OrdinalIgnoreCase))
            normalized = normalized.Substring("xi-api-key:".Length).Trim();
        if (normalized.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            normalized = normalized.Substring("Bearer ".Length).Trim();
        // PasswordBox pastes can occasionally contain line breaks or other whitespace.
        return string.Concat(normalized.Where(c => !char.IsWhiteSpace(c)));
    }

    private static InvalidOperationException CreateApiException(System.Net.HttpStatusCode statusCode, string? body)
    {
        if (statusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            var detail = string.IsNullOrWhiteSpace(body) ? string.Empty : " API response: " + body;
            return new InvalidOperationException(
                "ElevenLabs rejected the API key (401 Unauthorized). Check that you pasted the full, active ElevenLabs API key and that it has not expired or been revoked." + detail);
        }

        if (statusCode == System.Net.HttpStatusCode.Forbidden)
        {
            var detail = string.IsNullOrWhiteSpace(body) ? string.Empty : " API response: " + body;
            return new InvalidOperationException(
                "ElevenLabs denied this request (403 Forbidden). Check the API key's endpoint permissions and any IP allowlist restrictions." + detail);
        }

        return new InvalidOperationException("ElevenLabs returned " + (int)statusCode + " (" + statusCode + "): " + body);
    }

    public async Task SpeakAsync(string text, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        var key = NormalizeApiKey(_getApiKey());
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
            throw CreateApiException(response.StatusCode, body);

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
        request.Headers.Add("xi-api-key", key.Trim());
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw CreateApiException(response.StatusCode, body);

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

    public async Task<IReadOnlyList<SpeechVoice>> GetModelsAsync(CancellationToken cancellationToken = default)
    {
        var key = _getApiKey();
        if (string.IsNullOrWhiteSpace(key))
            throw new InvalidOperationException("Enter an ElevenLabs API key first.");

        using var request = new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}/models");
        request.Headers.Add("xi-api-key", key.Trim());
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw CreateApiException(response.StatusCode, body);

        using var document = JsonDocument.Parse(body);
        return document.RootElement.ValueKind == JsonValueKind.Array
            ? document.RootElement.EnumerateArray()
                .Where(item => !item.TryGetProperty("can_do_text_to_speech", out var canSpeak) || canSpeak.GetBoolean())
                .Select(item => new SpeechVoice(
                    item.GetProperty("model_id").GetString() ?? string.Empty,
                    item.TryGetProperty("name", out var name) ? name.GetString() ?? item.GetProperty("model_id").GetString() ?? string.Empty : item.GetProperty("model_id").GetString() ?? string.Empty))
                .Where(model => !string.IsNullOrWhiteSpace(model.Id))
                .OrderBy(model => model.Name, StringComparer.OrdinalIgnoreCase)
                .ToList()
            : new List<SpeechVoice>();
    }

    public Task TestAsync(CancellationToken cancellationToken = default)
        => SpeakAsync("Hello. This is Ballknower's ElevenLabs voice test.", cancellationToken);

    public void Stop()
    {
        _audioOutput.Stop();
    }

    public void Dispose()
    {
        Stop();
        _audioOutput.Dispose();
        _httpClient.Dispose();
    }
}
