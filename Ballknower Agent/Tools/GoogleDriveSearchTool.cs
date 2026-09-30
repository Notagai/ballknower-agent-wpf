using Ballknower.Google;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Ballknower.Tools;

public sealed class GoogleDriveSearchTool : ITool
{
    private readonly GoogleDriveService _drive;
    public GoogleDriveSearchTool(GoogleDriveService drive) { _drive = drive; }

    public ToolDefinition Definition { get; } = new()
    {
        Name = "drive_search",
        Description = "Searches the connected user's Google Drive by file name. Returns matching file IDs, names, types, sizes, modified times, and links.",
        RequiresConfirmation = false,
        Parameters = new
        {
            type = "object",
            properties = new { query = new { type = "string", description = "A file name or distinctive part of a file name to search for." } },
            required = new[] { "query" },
            additionalProperties = false
        }
    };

    public async Task<ToolResult> ExecuteAsync(Dictionary<string, string> arguments)
    {
        if (!arguments.TryGetValue("query", out var query) || string.IsNullOrWhiteSpace(query))
            return new ToolResult { Tool = Definition.Name, Success = false, Message = "Missing required argument: query." };
        try
        {
            var files = await _drive.SearchAsync(query);
            if (files.Count == 0)
                return new ToolResult { Tool = Definition.Name, Success = true, Message = $"No Google Drive files matched '{query}'." };
            var lines = new List<string>();
            foreach (var file in files)
                lines.Add($"ID: {file.Id}\nName: {file.Name}\nType: {file.MimeType}\nModified: {file.ModifiedTimeDateTimeOffset}\nLink: {file.WebViewLink}");
            return new ToolResult { Tool = Definition.Name, Success = true, Message = $"Google Drive results for '{query}':\n\n" + string.Join("\n\n", lines) };
        }
        catch (Exception ex) { return new ToolResult { Tool = Definition.Name, Success = false, Message = $"Google Drive search failed: {ex.Message}" }; }
    }
}
