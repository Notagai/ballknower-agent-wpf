using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace Ballknower.Tools;

public class MoveFileTool : ITool
{
    public ToolDefinition Definition { get; } = new()
    {
        Name = "move_file",

        Description =
            "Moves or renames an existing file. " +
            "The destination must include the new filename. " +
            "Never overwrites an existing file. " +
            "Use ~/Desktop, ~/Documents or ~/Downloads " +
            "instead of guessing the Windows username.",

        RequiresConfirmation = true,

        Parameters = new
        {
            type = "object",

            properties = new
            {
                source = new
                {
                    type = "string",
                    description =
                        "Full path of the existing file to move."
                },

                destination = new
                {
                    type = "string",
                    description =
                        "Full destination path, including the filename. " +
                        "To rename a file, use the same directory " +
                        "with a different filename."
                }
            },

            required = new[]
            {
                "source",
                "destination"
            },

            additionalProperties = false
        }
    };

    public Task<ToolResult> ExecuteAsync(
        Dictionary<string, string> arguments)
    {
        if (!arguments.TryGetValue("source", out var source) ||
            string.IsNullOrWhiteSpace(source))
        {
            return Failure(
                "Missing required argument: source.");
        }

        if (!arguments.TryGetValue(
                "destination",
                out var destination) ||
            string.IsNullOrWhiteSpace(destination))
        {
            return Failure(
                "Missing required argument: destination.");
        }

        try
        {
            source = Path.GetFullPath(source);
            destination = Path.GetFullPath(destination);

            if (!File.Exists(source))
            {
                return Failure(
                    $"Source file does not exist: {source}");
            }

            if (Directory.Exists(destination))
            {
                return Failure(
                    "The destination is a directory. " +
                    "Include the destination filename.");
            }

            if (File.Exists(destination))
            {
                return Failure(
                    $"Destination file already exists: {destination}");
            }

            if (string.Equals(
                source,
                destination,
                StringComparison.OrdinalIgnoreCase))
            {
                return Failure(
                    "Source and destination are the same.");
            }

            var destinationDirectory =
                Path.GetDirectoryName(destination);

            if (string.IsNullOrWhiteSpace(
                destinationDirectory))
            {
                return Failure(
                    "Invalid destination directory.");
            }

            Directory.CreateDirectory(
                destinationDirectory);

            File.Move(
                source,
                destination,
                overwrite: false);

            return Task.FromResult(new ToolResult
            {
                Tool = Definition.Name,
                Success = true,
                Message =
                    $"File moved successfully.\n" +
                    $"From: {source}\n" +
                    $"To: {destination}"
            });
        }
        catch (Exception ex)
        {
            return Failure(
                $"Failed to move file: {ex.Message}");
        }
    }

    private Task<ToolResult> Failure(string message)
    {
        return Task.FromResult(new ToolResult
        {
            Tool = Definition.Name,
            Success = false,
            Message = message
        });
    }
}