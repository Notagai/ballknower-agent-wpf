using Ballknower.Tools;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Ballknower.Diagnostics;

namespace Ballknower.AI;

public class OpenRouterMessage
{
    [JsonPropertyName("role")]
    public string Role { get; set; } = string.Empty;

    [JsonPropertyName("content")]
    public string? Content { get; set; }

    [JsonPropertyName("tool_calls")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<OpenRouterToolCall>? ToolCalls { get; set; }

    [JsonPropertyName("tool_call_id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ToolCallId { get; set; }
}

public class OpenRouterToolCall
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; set; } = "function";

    [JsonPropertyName("function")]
    public OpenRouterFunctionCall Function { get; set; } = new();
}

public class OpenRouterFunctionCall
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("arguments")]
    public string Arguments { get; set; } = "{}";
}

public class OpenRouterClient : IAiClient
{
    private const string Endpoint =
        "https://openrouter.ai/api/v1/chat/completions";

    private readonly HttpClient _httpClient;
    private readonly ToolRegistry _toolRegistry;

    public OpenRouterClient(
        string apiKey,
        ToolRegistry toolRegistry)
    {
        _toolRegistry =
            toolRegistry;

        _httpClient =
            new HttpClient
            {
                Timeout =
                    TimeSpan.FromSeconds(90)
            };

        _httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue(
                "Bearer",
                apiKey);

        _httpClient.DefaultRequestHeaders.Add(
            "X-Title",
            "Ballknower");
    }

    public async Task<string> SendMessageAsync(
        string model,
        List<OpenRouterMessage> messages)
    {
        var requestBody = new
        {
            model,
            messages
        };

        var responseJson =
            await PostAsync(
                requestBody);

        var message =
            ExtractMessage(
                responseJson);

        return message.Content ??
            string.Empty;
    }

    public async Task<OpenRouterMessage> SendWithToolsAsync(
        string model,
        List<OpenRouterMessage> messages)
    {
        var tools =
            _toolRegistry.GetAiToolDefinitions();

        var requestBody = new
        {
            model,
            messages,
            tools,
            tool_choice = "auto",
            stream = false
        };

        var responseJson =
            await PostAsync(
                requestBody);

        return ExtractMessage(
            responseJson);
    }

    private async Task<string> PostAsync(
        object requestBody)
    {
        var json =
            JsonSerializer.Serialize(
                requestBody);

        using var content =
            new StringContent(
                json,
                Encoding.UTF8,
                "application/json");

        using var response =
            await _httpClient.PostAsync(
                Endpoint,
                content);

        var responseJson =
            await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            throw ProviderErrorHandler.CreateException(
                "OpenRouter",
                response,
                responseJson);
        }

        return responseJson;
    }

    private static OpenRouterMessage ExtractMessage(
        string responseJson)
    {
        using var document =
            JsonDocument.Parse(
                responseJson);

        if (!document.RootElement.TryGetProperty(
                "choices",
                out var choices) ||
            choices.ValueKind !=
                JsonValueKind.Array ||
            choices.GetArrayLength() == 0)
        {
            throw new Exception(
                "OpenRouter returned no choices: " +
                responseJson);
        }

        if (!choices[0].TryGetProperty(
                "message",
                out var messageElement))
        {
            throw new Exception(
                "OpenRouter response is missing its message: " +
                responseJson);
        }

        var message =
            JsonSerializer.Deserialize<OpenRouterMessage>(
                messageElement.GetRawText());

        if (message is null)
        {
            throw new Exception(
                "Could not deserialize OpenRouter's message.");
        }

        return message;
    }

    public async Task StreamMessageAsync(
        string model,
        List<OpenRouterMessage> messages,
        Action<string> onText)
    {
        var requestBody = new
        {
            model,
            messages,
            stream = true
        };

        var json =
            JsonSerializer.Serialize(
                requestBody);

        using var content =
            new StringContent(
                json,
                Encoding.UTF8,
                "application/json");

        using var request =
            new HttpRequestMessage(
                HttpMethod.Post,
                Endpoint);

        request.Content =
            content;

        using var response =
            await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead);

        if (!response.IsSuccessStatusCode)
        {
            var errorJson =
                await response.Content.ReadAsStringAsync();

            throw ProviderErrorHandler.CreateException(
                "OpenRouter",
                response,
                errorJson);
        }

        using var stream =
            await response.Content.ReadAsStreamAsync();

        using var reader =
            new System.IO.StreamReader(
                stream);

        while (!reader.EndOfStream)
        {
            var line =
                await reader.ReadLineAsync();

            if (string.IsNullOrWhiteSpace(line) ||
                !line.StartsWith("data:"))
            {
                continue;
            }

            var data =
                line["data:".Length..].Trim();

            if (data == "[DONE]")
                break;

            using var document =
                JsonDocument.Parse(
                    data);

            if (!document.RootElement.TryGetProperty(
                    "choices",
                    out var choices) ||
                choices.GetArrayLength() == 0)
            {
                continue;
            }

            var choice =
                choices[0];

            if (!choice.TryGetProperty(
                    "delta",
                    out var delta) ||
                !delta.TryGetProperty(
                    "content",
                    out var contentElement) ||
                contentElement.ValueKind !=
                    JsonValueKind.String)
            {
                continue;
            }

            var text =
                contentElement.GetString();

            if (!string.IsNullOrEmpty(text))
            {
                onText(text);
            }
        }
    }
}