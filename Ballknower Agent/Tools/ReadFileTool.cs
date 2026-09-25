using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace Ballknower.Tools;

public class ReadFileTool : ITool
{
    public ToolDefinition Definition { get; } = new()
    {
        Name = "read_file",

        Description =
            "Reads and returns the text contents of a file.",

        RequiresConfirmation = false,

        Parameters = new
        {
            type = "object",

            properties = new
            {
                path = new
                {
                    type = "string",

                    description =
                        "Path of the file to read. " +
                        "Use ~/Desktop, ~/Documents or ~/Downloads " +
                        "for familiar folders."
                }
            },

            required = new[]
            {
                "path"
            },

            additionalProperties = false
        }
    };

    public Task<ToolResult> ExecuteAsync(
        Dictionary<string, string> arguments)
    {
        if (!arguments.TryGetValue(
                "path",
                out var path) ||
            string.IsNullOrWhiteSpace(path))
        {
            return Task.FromResult(
                new ToolResult
                {
                    Tool = Definition.Name,
                    Success = false,
                    Message =
                        "Missing required argument: path."
                });
        }

        try
        {
            if (!File.Exists(path))
            {
                return Task.FromResult(
                    new ToolResult
                    {
                        Tool = Definition.Name,
                        Success = false,
                        Message =
                            $"File does not exist: {path}"
                    });
            }

            var content =
                File.ReadAllText(path);

            return Task.FromResult(
                new ToolResult
                {
                    Tool = Definition.Name,
                    Success = true,
                    Message = content
                });
        }
        catch (Exception ex)
        {
            return Task.FromResult(
                new ToolResult
                {
                    Tool = Definition.Name,
                    Success = false,
                    Message =
                        $"Failed to read file: {ex.Message}"
                });
        }
    }
}