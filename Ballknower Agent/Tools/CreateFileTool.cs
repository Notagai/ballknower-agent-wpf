using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace Ballknower.Tools;

public class CreateFileTool : ITool
{
    public ToolDefinition Definition { get; } = new()
    {
        Name = "create_file",

        Description =
            "Creates a file at the specified path with the specified contents.",

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
                        "Destination file path. " +
                        "Use ~/Desktop, ~/Documents or ~/Downloads " +
                        "instead of guessing the Windows username."
                },

                content = new
                {
                    type = "string",

                    description =
                        "Text to write into the file."
                }
            },

            required = new[]
            {
                "path",
                "content"
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

        arguments.TryGetValue(
            "content",
            out var content);

        content ??= string.Empty;

        try
        {
            var directory =
                Path.GetDirectoryName(path);

            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(
                    directory);
            }

            File.WriteAllText(
                path,
                content);

            return Task.FromResult(
                new ToolResult
                {
                    Tool = Definition.Name,
                    Success = true,
                    Message =
                        $"File created successfully: {path}"
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
                        $"Failed to create file: {ex.Message}"
                });
        }
    }
}