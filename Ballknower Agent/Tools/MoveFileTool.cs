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
        Description = "Moves or renames a file without overwriting an existing destination. Supports ~/ paths.",
        RequiresConfirmation = true,
        Parameters = new
        {
            type = "object",
            properties = new
            {
                source = new { type = "string", description = "Existing file path." },
                destination = new { type = "string", description = "Destination path, including filename." }
            },
            required = new[] { "source", "destination" },
            additionalProperties = false
        }
    };

    public Task<ToolResult> ExecuteAsync(Dictionary<string, string> arguments)
    {
        if (!arguments.TryGetValue("source", out var source) || string.IsNullOrWhiteSpace(source))
            return Failure("Missing required argument: source.");
        if (!arguments.TryGetValue("destination", out var destination) || string.IsNullOrWhiteSpace(destination))
            return Failure("Missing required argument: destination.");

        try
        {
            source = CreateFileTool.ResolvePath(source);
            destination = CreateFileTool.ResolvePath(destination);
            if (!File.Exists(source)) return Failure("Source file does not exist.");
            if (Directory.Exists(destination)) return Failure("Destination is a directory; include a filename.");
            if (string.Equals(source, destination, StringComparison.OrdinalIgnoreCase))
                return Failure("Source and destination are the same.");

            var directory = Path.GetDirectoryName(destination);
            if (string.IsNullOrWhiteSpace(directory)) return Failure("Invalid destination directory.");
            Directory.CreateDirectory(directory);

            // File.Move with overwrite:false is the authoritative atomic no-overwrite check.
            File.Move(source, destination, overwrite: false);
            return Task.FromResult(new ToolResult { Tool = Definition.Name, Success = true,
                Message = $"File moved successfully.\nFrom: {source}\nTo: {destination}" });
        }
        catch (IOException)
        {
            return Failure("Move failed: the destination may already exist or the file may be in use.");
        }
        catch (Exception)
        {
            return Failure("Failed to move file. Check both paths and permissions.");
        }
    }

    private Task<ToolResult> Failure(string message) =>
        Task.FromResult(new ToolResult { Tool = Definition.Name, Success = false, Message = message });
}