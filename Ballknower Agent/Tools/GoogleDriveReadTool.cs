using Ballknower.Google;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Ballknower.Tools;

public sealed class GoogleDriveReadTool : ITool
{
    private readonly GoogleDriveService _drive;
    public GoogleDriveReadTool(GoogleDriveService drive) { _drive = drive; }

    public ToolDefinition Definition { get; } = new()
    {
        Name = "drive_read",
        Description = "Reads the text contents of a Google Drive file using its file ID. If the user gave a file name or description instead of an ID, use drive_search first to find the ID; do not ask the user for the ID when the file can be searched.",
        RequiresConfirmation = false,
        Parameters = new
        {
            type = "object",
            properties = new { file_id = new { type = "string", description = "The Google Drive file ID." } },
            required = new[] { "file_id" },
            additionalProperties = false
        }
    };

    public async Task<ToolResult> ExecuteAsync(Dictionary<string, string> arguments)
    {
        if (!arguments.TryGetValue("file_id", out var fileId) || string.IsNullOrWhiteSpace(fileId))
            return new ToolResult { Tool = Definition.Name, Success = false, Message = "Missing required argument: file_id." };
        try
        {
            var content = await _drive.ReadAsync(fileId.Trim());
            return new ToolResult { Tool = Definition.Name, Success = true, Message = content.Length > 100_000 ? content[..100_000] + "\n\n[Content truncated at 100,000 characters.]" : content };
        }
        catch (Exception ex) { return new ToolResult { Tool = Definition.Name, Success = false, Message = $"Google Drive read failed: {ex.Message}" }; }
    }
}
