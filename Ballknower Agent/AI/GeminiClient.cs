using Ballknower.Tools;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace Ballknower.AI;

public sealed class GeminiClient : IAiClient
{
    private const string Endpoint = "https://generativelanguage.googleapis.com/v1beta/models/";
    private readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(90) };
    private readonly string _apiKey;
    private readonly ToolRegistry _toolRegistry;

    public GeminiClient(string apiKey, ToolRegistry toolRegistry)
    {
        _apiKey = apiKey;
        _toolRegistry = toolRegistry;
    }

    public async Task<OpenRouterMessage> SendWithToolsAsync(string model, List<OpenRouterMessage> messages)
    {
        var contents = messages
            .Where(m => m.Role != "system" && m.Role != "tool")
            .Select(m => new
            {
                role = m.Role == "assistant" ? "model" : "user",
                parts = new[] { new { text = m.Content ?? string.Empty } }
            }).ToArray();

        var systemText = string.Join("\n", messages.Where(m => m.Role == "system").Select(m => m.Content));
        var declarations = _toolRegistry.GetAiToolDefinitions()
            .Select(t => new
            {
                name = t.function.name,
                description = t.function.description,
                parameters = t.function.parameters
            }).ToArray();

        var requestBody = new
        {
            systemInstruction = string.IsNullOrWhiteSpace(systemText) ? null : new { parts = new[] { new { text = systemText } } },
            contents,
            tools = new[] { new { functionDeclarations = declarations } },
            toolConfig = new { functionCallingConfig = new { mode = "AUTO" } }
        };

        var json = JsonSerializer.Serialize(requestBody, new JsonSerializerOptions { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull });
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{Endpoint}{Uri.EscapeDataString(model)}:generateContent?key={Uri.EscapeDataString(_apiKey)}");
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        using var response = await _httpClient.SendAsync(request);
        var responseJson = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
            throw new Exception($"Gemini API error ({(int)response.StatusCode}): {responseJson}");

        using var document = JsonDocument.Parse(responseJson);
        if (!document.RootElement.TryGetProperty("candidates", out var candidates) ||
            candidates.ValueKind != JsonValueKind.Array || candidates.GetArrayLength() == 0)
            throw new Exception("Gemini returned no candidates.");

        var parts = candidates[0].GetProperty("content").GetProperty("parts");
        var result = new OpenRouterMessage { Role = "assistant" };
        var textParts = new List<string>();
        var calls = new List<OpenRouterToolCall>();
        foreach (var part in parts.EnumerateArray())
        {
            if (part.TryGetProperty("text", out var text))
                textParts.Add(text.GetString() ?? string.Empty);
            if (part.TryGetProperty("functionCall", out var call))
            {
                var args = call.TryGetProperty("args", out var a) ? a.GetRawText() : "{}";
                calls.Add(new OpenRouterToolCall
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Type = "function",
                    Function = new OpenRouterFunctionCall
                    {
                        Name = call.GetProperty("name").GetString() ?? string.Empty,
                        Arguments = args
                    }
                });
            }
        }
        result.Content = string.Join("\n", textParts);
        result.ToolCalls = calls.Count == 0 ? null : calls;
        return result;
    }
}