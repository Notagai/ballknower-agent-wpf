using Ballknower.Tools;
using Ballknower.Diagnostics;
using System;
using System.Collections.Generic;
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
        var requestBody = new
        {
            model,
            messages,
            tools = _toolRegistry.GetAiToolDefinitions(),
            tool_choice = "auto",
            stream = false
        };

        var responseJson = await PostAsync(requestBody);
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