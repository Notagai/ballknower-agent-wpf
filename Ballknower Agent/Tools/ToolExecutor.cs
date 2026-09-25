using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Ballknower.Tools;

public class ToolExecutor
{
    private readonly ToolRegistry _registry;
    private readonly XamlRoot _xamlRoot;

    public ToolExecutor(
        ToolRegistry registry,
        XamlRoot xamlRoot)
    {
        _registry = registry;
        _xamlRoot = xamlRoot;
    }

    public async Task<ToolResult> ExecuteAsync(
        string toolName,
        Dictionary<string, string> arguments)
    {
        if (!_registry.TryGetTool(
                toolName,
                out var tool))
        {
            return new ToolResult
            {
                Tool = toolName,
                Success = false,
                Message =
                    $"Tool '{toolName}' does not exist."
            };
        }

        if (tool is null)
        {
            return new ToolResult
            {
                Tool = toolName,
                Success = false,
                Message =
                    $"Tool '{toolName}' could not be loaded."
            };
        }

        if (tool.Definition.RequiresConfirmation)
        {
            var confirmed =
                await RequestConfirmationAsync(
                    tool,
                    arguments);

            if (!confirmed)
            {
                return new ToolResult
                {
                    Tool = toolName,
                    Success = false,
                    Message =
                        "The user cancelled the tool execution."
                };
            }
        }

        return await tool.ExecuteAsync(arguments);
    }

    private async Task<bool> RequestConfirmationAsync(
        ITool tool,
        Dictionary<string, string> arguments)
    {
        var message =
            BuildConfirmationMessage(
                tool,
                arguments);

        var dialog = new ContentDialog
        {
            Title = "Confirm Action",
            Content = message,
            PrimaryButtonText = "Allow",
            CloseButtonText = "Cancel",
            DefaultButton =
                ContentDialogButton.Close,
            XamlRoot = _xamlRoot
        };

        var result =
            await dialog.ShowAsync();

        return result ==
            ContentDialogResult.Primary;
    }

    private string BuildConfirmationMessage(
        ITool tool,
        Dictionary<string, string> arguments)
    {
        if (tool.Definition.Name ==
                "delete_file" &&
            arguments.TryGetValue(
                "path",
                out var path))
        {
            return
                "Ballknower wants to delete this file:\n\n" +
                path +
                "\n\nDo you want to allow this action?";
        }

        return
            $"Ballknower wants to execute " +
            $"'{tool.Definition.Name}'.\n\n" +
            "Do you want to allow this action?";
    }
}