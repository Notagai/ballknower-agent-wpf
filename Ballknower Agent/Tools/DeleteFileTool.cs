using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace Ballknower.Tools;

public class DeleteFileTool : ITool
{
    public ToolDefinition Definition { get; } = new()
    {
        Name = "delete_file",

        Description =
            "Deletes a file at the specified path.",

        RequiresConfirmation = true,

        Parameters = new
        {
            type = "object",

            properties = new
            {
                path = new
                {
                    type = "string",

                    description =
                        "Path of the file to delete. " +
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

            File.Delete(path);

            return Task.FromResult(
                new ToolResult
                {
                    Tool = Definition.Name,
                    Success = true,
                    Message =
                        $"File deleted successfully: {path}"
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
                        $"Failed to delete file: {ex.Message}"
                });
        }
    }
}