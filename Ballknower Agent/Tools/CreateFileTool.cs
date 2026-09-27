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
        Description = "Creates a new file without overwriting an existing file.",
        RequiresConfirmation = true,
        Parameters = new
        {
            type = "object",
            properties = new
            {
                path = new { type = "string", description = "Destination file path. Supports ~/ paths." },
                content = new { type = "string", description = "Text to write into the file." }
            },
            required = new[] { "path", "content" },
            additionalProperties = false
        }
    };

    public Task<ToolResult> ExecuteAsync(Dictionary<string, string> arguments)
    {
        if (!arguments.TryGetValue("path", out var path) || string.IsNullOrWhiteSpace(path))
            return Result(false, "Missing required argument: path.");
        if (!arguments.TryGetValue("content", out var content))
            return Result(false, "Missing required argument: content.");

        try
        {
            path = ResolvePath(path);
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            using var writer = new StreamWriter(stream);
            writer.Write(content);
            return Result(true, $"File created successfully: {path}");
        }
        catch (IOException) when (File.Exists(SafeResolve(path)))
        {
            return Result(false, "A file already exists at the destination; it was not overwritten.");
        }
        catch (Exception)
        {
            return Result(false, "Failed to create file. Check the path and permissions.");
        }
    }

    internal static string ResolvePath(string path)
    {
        if (path == "~") return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (path.StartsWith("~/", StringComparison.Ordinal) || path.StartsWith(@"~\\", StringComparison.Ordinal))
            path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), path.Substring(2));
        return Path.GetFullPath(Environment.ExpandEnvironmentVariables(path));
    }

    private static string SafeResolve(string path)
    {
        try { return ResolvePath(path); } catch { return path; }
    }

    private Task<ToolResult> Result(bool success, string message) =>
        Task.FromResult(new ToolResult { Tool = Definition.Name, Success = success, Message = message });
}