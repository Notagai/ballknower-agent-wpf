using Ballknower.Google;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Ballknower.Tools;

public sealed class GoogleDriveWriteTool : ITool
{
    private readonly GoogleDriveService _drive;

    public GoogleDriveWriteTool(GoogleDriveService drive)
    {
        _drive = drive;
    }

    public ToolDefinition Definition { get; } = new()
    {
        Name = "drive_write",
        Description = "Makes a change to Google Drive. Supported operations are create_text, update_text, rename, move, and delete. Every operation requires explicit user confirmation in the Ballknower app.",
        RequiresConfirmation = true,
        Parameters = new
        {
            type = "object",
            properties = new
            {
                operation = new
                {
                    type = "string",
                    @enum = new[] { "create_text", "update_text", "rename", "move", "delete" },
                    description = "The Drive change to perform."
                },
                file_id = new { type = "string", description = "Existing Google Drive file ID for update_text, rename, move, or delete." },
                name = new { type = "string", description = "File name for create_text, or new name for rename." },
                content = new { type = "string", description = "UTF-8 text content for create_text or update_text." },
                parent_id = new { type = "string", description = "Destination Google Drive folder ID for create_text or move." }
            },
            required = new[] { "operation" },
            additionalProperties = false
        }
    };

    public async Task<ToolResult> ExecuteAsync(Dictionary<string, string> arguments)
    {
        if (!arguments.TryGetValue("operation", out var operation) || string.IsNullOrWhiteSpace(operation))
            return Failure("Missing required argument: operation.");

        try
        {
            operation = operation.Trim().ToLowerInvariant();

            return operation switch
            {
                "create_text" => await CreateAsync(arguments),
                "update_text" => await UpdateAsync(arguments),
                "rename" => await RenameAsync(arguments),
                "move" => await MoveAsync(arguments),
                "delete" => await DeleteAsync(arguments),
                _ => Failure("Unsupported Drive write operation.")
            };
        }
        catch (Exception ex)
        {
            return Failure($"Google Drive write failed: {ex.Message}");
        }
    }

    private async Task<ToolResult> CreateAsync(Dictionary<string, string> arguments)
    {
        if (!arguments.TryGetValue("name", out var name) || string.IsNullOrWhiteSpace(name))
            return Failure("Missing required argument: name.");
        arguments.TryGetValue("content", out var content);
        arguments.TryGetValue("parent_id", out var parentId);

        var result = await _drive.CreateTextFileAsync(name, content ?? string.Empty, parentId);
        return Success($"Google Drive file created.\n\n{result}");
    }

    private async Task<ToolResult> UpdateAsync(Dictionary<string, string> arguments)
    {
        if (!arguments.TryGetValue("file_id", out var fileId) || string.IsNullOrWhiteSpace(fileId))
            return Failure("Missing required argument: file_id.");
        if (!arguments.TryGetValue("content", out var content))
            return Failure("Missing required argument: content.");

        var result = await _drive.UpdateTextFileAsync(fileId, content);
        return Success($"Google Drive file updated.\n\n{result}");
    }

    private async Task<ToolResult> RenameAsync(Dictionary<string, string> arguments)
    {
        if (!arguments.TryGetValue("file_id", out var fileId) || string.IsNullOrWhiteSpace(fileId))
            return Failure("Missing required argument: file_id.");
        if (!arguments.TryGetValue("name", out var name) || string.IsNullOrWhiteSpace(name))
            return Failure("Missing required argument: name.");

        var result = await _drive.RenameAsync(fileId, name);
        return Success($"Google Drive file renamed.\n\n{result}");
    }

    private async Task<ToolResult> MoveAsync(Dictionary<string, string> arguments)
    {
        if (!arguments.TryGetValue("file_id", out var fileId) || string.IsNullOrWhiteSpace(fileId))
            return Failure("Missing required argument: file_id.");
        if (!arguments.TryGetValue("parent_id", out var parentId) || string.IsNullOrWhiteSpace(parentId))
            return Failure("Missing required argument: parent_id.");

        var result = await _drive.MoveAsync(fileId, parentId);
        return Success($"Google Drive file moved.\n\n{result}");
    }

    private async Task<ToolResult> DeleteAsync(Dictionary<string, string> arguments)
    {
        if (!arguments.TryGetValue("file_id", out var fileId) || string.IsNullOrWhiteSpace(fileId))
            return Failure("Missing required argument: file_id.");

        await _drive.DeleteAsync(fileId);
        return Success($"Google Drive file deleted. ID: {fileId}");
    }

    private ToolResult Success(string message) =>
        new() { Tool = Definition.Name, Success = true, Message = message };

    private ToolResult Failure(string message) =>
        new() { Tool = Definition.Name, Success = false, Message = message };
}
