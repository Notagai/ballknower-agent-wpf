using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Ballknower.Tools;
using Ballknower.Diagnostics;

namespace Ballknower.AI;

public class GroqClient : IAiClient
{
    private const string Endpoint =
        "https://api.groq.com/openai/v1/chat/completions";

    private readonly HttpClient _httpClient;
    private readonly ToolRegistry _toolRegistry;

    public GroqClient(
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
            await PostAsync(requestBody);

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
                "Groq",
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
                "Groq returned no choices: " +
                responseJson);
        }

        if (!choices[0].TryGetProperty(
                "message",
                out var messageElement))
        {
            throw new Exception(
                "Groq response is missing its message: " +
                responseJson);
        }

        var message =
            JsonSerializer.Deserialize<OpenRouterMessage>(
                messageElement.GetRawText());

        if (message is null)
        {
            throw new Exception(
                "Could not deserialize Groq's message.");
        }

        return message;
    }
}