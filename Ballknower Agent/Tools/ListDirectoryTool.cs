using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace Ballknower.Tools;

public class ListDirectoryTool : ITool
{
    public ToolDefinition Definition { get; } = new()
    {
        Name = "list_directory",

        Description =
            "Lists the files and subfolders inside a directory. " +
            "Use this tool to explore folders. " +
            "To look inside a subfolder, call this tool again " +
            "using the subfolder's full path. " +
            "Does not search recursively.",

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
                        "Full path of the folder to inspect. " +
                        "Use ~/Desktop, ~/Documents or ~/Downloads " +
                        "instead of guessing the Windows username."
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
        if (!arguments.TryGetValue("path", out var path) ||
            string.IsNullOrWhiteSpace(path))
        {
            return Task.FromResult(new ToolResult
            {
                Tool = Definition.Name,
                Success = false,
                Message = "Missing required argument: path."
            });
        }

        try
        {
            if (!Directory.Exists(path))
            {
                return Task.FromResult(new ToolResult
                {
                    Tool = Definition.Name,
                    Success = false,
                    Message = $"Directory does not exist: {path}"
                });
            }

            const int maxEntries = 200;

            var directory = new DirectoryInfo(path);

            var entries = directory
                .EnumerateFileSystemInfos()
                .OrderBy(entry =>
                    entry is DirectoryInfo ? 0 : 1)
                .ThenBy(entry => entry.Name)
                .Take(maxEntries + 1)
                .ToList();

            bool truncated = entries.Count > maxEntries;

            var results = entries
                .Take(maxEntries)
                .Select(entry => new
                {
                    name = entry.Name,

                    path = entry.FullName,

                    type = entry is DirectoryInfo
                        ? "directory"
                        : "file"
                })
                .ToArray();

            var output = new
            {
                directory = directory.FullName,

                entries = results,

                truncated,

                message = truncated
                    ? $"Showing the first {maxEntries} entries."
                    : "All entries returned."
            };

            return Task.FromResult(new ToolResult
            {
                Tool = Definition.Name,
                Success = true,
                Message = JsonSerializer.Serialize(
                    output,
                    new JsonSerializerOptions
                    {
                        WriteIndented = true
                    })
            });
        }
        catch (Exception ex)
        {
            return Task.FromResult(new ToolResult
            {
                Tool = Definition.Name,
                Success = false,
                Message =
                    $"Failed to list directory: {ex.Message}"
            });
        }
    }
}