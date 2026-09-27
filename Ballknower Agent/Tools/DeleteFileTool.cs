using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace Ballknower.Tools;

public class DeleteFileTool : ITool
{
    public ToolDefinition Definition { get; } = new()
    {
        Name = "delete_file",
        Description = "Deletes a file at the specified path. Supports ~/ paths.",
        RequiresConfirmation = true,
        Parameters = new
        {
            type = "object",
            properties = new { path = new { type = "string", description = "Path of the file to delete. Supports ~/ paths." } },
            required = new[] { "path" },
            additionalProperties = false
        }
    };

    public Task<ToolResult> ExecuteAsync(Dictionary<string, string> arguments)
    {
        if (!arguments.TryGetValue("path", out var path) || string.IsNullOrWhiteSpace(path))
            return Result(false, "Missing required argument: path.");
        try
        {
            path = CreateFileTool.ResolvePath(path);
            if (!File.Exists(path)) return Result(false, "File does not exist.");
            File.Delete(path);
            return Result(true, $"File deleted successfully: {path}");
        }
        catch (IOException)
        {
            return Result(false, "Failed to delete file; it may be in use.");
        }
        catch (System.Exception)
        {
            return Result(false, "Failed to delete file. Check the path and permissions.");
        }
    }

    private Task<ToolResult> Result(bool success, string message) =>
        Task.FromResult(new ToolResult { Tool = Definition.Name, Success = success, Message = message });
}