using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace Ballknower.Tools;

public class ToolValidator
{
    private readonly ToolRegistry _registry;
    public ToolValidator(ToolRegistry registry) => _registry = registry;

    public bool TryValidate(ToolRequest request, out string error)
    {
        error = string.Empty;
        if (request is null) { error = "Tool request is null."; return false; }
        if (string.IsNullOrWhiteSpace(request.Tool)) { error = "Tool request is missing a tool name."; return false; }
        if (!_registry.TryGetTool(request.Tool, out var tool) || tool is null) { error = "Tool does not exist or is unavailable."; return false; }
        if (request.Arguments is null) { error = "Tool request is missing its arguments."; return false; }

        try
        {
            using var schema = JsonDocument.Parse(JsonSerializer.Serialize(tool.Definition.Parameters));
            var root = schema.RootElement;
            if (root.TryGetProperty("properties", out var properties))
            {
                foreach (var key in request.Arguments.Keys)
                    if (!properties.TryGetProperty(key, out _))
                    { error = $"Unexpected argument: {key}."; return false; }
                if (root.TryGetProperty("required", out var required))
                    foreach (var item in required.EnumerateArray())
                    {
                        var key = item.GetString();
                        if (key is null || !request.Arguments.TryGetValue(key, out var value) || value is null)
                        { error = $"Missing required argument: {key}."; return false; }
                        if (properties.TryGetProperty(key, out var definition) &&
                            definition.TryGetProperty("type", out var type) &&
                            type.GetString() == "string" && string.IsNullOrWhiteSpace(value))
                        { error = $"Argument '{key}' must not be empty."; return false; }
                    }
            }
        }
        catch (JsonException) { error = "Tool schema could not be validated."; return false; }
        return true;
    }
}