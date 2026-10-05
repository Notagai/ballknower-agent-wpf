using Ballknower.Tools;
using Ballknower.Diagnostics;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Ballknower.AI;

public sealed class OpenAIClient : IAiClient
{
    private const string Endpoint = "https://api.openai.com/v1/chat/completions";

    private readonly HttpClient _httpClient;
    private readonly ToolRegistry _toolRegistry;

    public OpenAIClient(string apiKey, ToolRegistry toolRegistry)
    {
        _toolRegistry = toolRegistry;
        _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(90) };
        _httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", apiKey);
    }

    public async Task<OpenRouterMessage> SendWithToolsAsync(
        string model,
        List<OpenRouterMessage> messages)
    {
        var tools = _toolRegistry.GetAiToolDefinitions();

        var requestBody = new
        {
            model,
            messages,
            tools,
            tool_choice = "auto",
            reasoning_effort = "none",
            stream = false
        };

        var responseJson = await PostAsync(requestBody);

        LogRequestDiagnostics(
            model,
            messages,
            tools,
            responseJson);

        using var document = JsonDocument.Parse(responseJson);

        if (!document.RootElement.TryGetProperty("choices", out var choices) ||
            choices.ValueKind != JsonValueKind.Array ||
            choices.GetArrayLength() == 0 ||
            !choices[0].TryGetProperty("message", out var messageElement))
        {
            throw new Exception("OpenAI returned no message.");
        }

        return JsonSerializer.Deserialize<OpenRouterMessage>(messageElement.GetRawText())
            ?? throw new Exception("Could not deserialize OpenAI's message.");
    }

    private static void LogRequestDiagnostics(
        string model,
        List<OpenRouterMessage> messages,
        object tools,
        string responseJson)
    {
        try
        {
            var requestJson =
                JsonSerializer.Serialize(
                    new
                    {
                        model,
                        messages,
                        tools,
                        tool_choice = "auto",
                        reasoning_effort = "none",
                        stream = false
                    });

            using var document =
                JsonDocument.Parse(responseJson);

            var usage =
                document.RootElement.TryGetProperty(
                    "usage",
                    out var usageElement)
                    ? usageElement
                    : default;

            var inputTokens =
                usage.ValueKind != JsonValueKind.Undefined &&
                usage.TryGetProperty(
                    "prompt_tokens",
                    out var promptTokens)
                    ? promptTokens.GetInt32().ToString()
                    : "unavailable";

            var cachedTokens =
                usage.ValueKind != JsonValueKind.Undefined &&
                usage.TryGetProperty(
                    "prompt_tokens_details",
                    out var promptDetails) &&
                promptDetails.TryGetProperty(
                    "cached_tokens",
                    out var cached)
                    ? cached.GetInt32().ToString()
                    : "unavailable";

            Debug.WriteLine(
                $"OpenAI request diagnostics: " +
                $"messages={messages.Count}, " +
                $"toolsJsonChars={JsonSerializer.Serialize(tools).Length}, " +
                $"requestJsonChars={requestJson.Length}, " +
                $"inputTokens={inputTokens}, " +
                $"cachedInputTokens={cachedTokens}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine(
                $"OpenAI request diagnostics failed: {ex.Message}");
        }
    }

    private async Task<string> PostAsync(object requestBody)
    {
        var json = JsonSerializer.Serialize(requestBody);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var response = await _httpClient.PostAsync(Endpoint, content);
        var responseJson = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
            throw ProviderErrorHandler.CreateException("OpenAI", response, responseJson);

        return responseJson;
    }
}