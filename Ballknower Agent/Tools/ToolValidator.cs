using System;
using System.Collections.Generic;

namespace Ballknower.Tools;

public class ToolValidator
{
    private readonly ToolRegistry _registry;

    public ToolValidator(ToolRegistry registry)
    {
        _registry = registry;
    }

    public bool TryValidate(
        ToolRequest request,
        out string error)
    {
        error = string.Empty;

        if (request is null)
        {
            error = "Tool request is null.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(request.Tool))
        {
            error = "Tool request is missing a tool name.";
            return false;
        }

        if (!_registry.TryGetTool(
                request.Tool,
                out var tool))
        {
            error =
                $"Tool '{request.Tool}' does not exist.";

            return false;
        }

        if (tool is null)
        {
            error =
                $"Tool '{request.Tool}' could not be loaded.";

            return false;
        }

        if (request.Arguments is null)
        {
            error =
                $"Tool '{request.Tool}' is missing its arguments.";

            return false;
        }

        return true;
    }
}