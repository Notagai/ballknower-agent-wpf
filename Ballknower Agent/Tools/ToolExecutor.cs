using System.Collections.Generic;
using System.Threading.Tasks;

using System.Windows;

using WpfMessageBox = System.Windows.MessageBox;
using WpfMessageBoxButton = System.Windows.MessageBoxButton;
using WpfMessageBoxImage = System.Windows.MessageBoxImage;
using WpfMessageBoxResult = System.Windows.MessageBoxResult;

namespace Ballknower.Tools;

public class ToolExecutor
{
    private readonly ToolRegistry _registry;

    public ToolExecutor(
        ToolRegistry registry)
    {
        _registry = registry;
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

        return await tool.ExecuteAsync(
            arguments);
    }

    private Task<bool> RequestConfirmationAsync(
        ITool tool,
        Dictionary<string, string> arguments)
    {
        string message =
            BuildConfirmationMessage(
                tool,
                arguments);

        var result =
            WpfMessageBox.Show(
                message,
                "Confirm Action",
                WpfMessageBoxButton.OKCancel,
                WpfMessageBoxImage.Question);

        return Task.FromResult(
            result ==
            WpfMessageBoxResult.OK);
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