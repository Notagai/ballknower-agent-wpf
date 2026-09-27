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
    public ToolExecutor(ToolRegistry registry) => _registry = registry;

    public async Task<ToolResult> ExecuteAsync(string toolName, Dictionary<string, string> arguments)
    {
        if (!_registry.TryGetTool(toolName, out var tool) || tool is null)
            return new ToolResult { Tool = toolName, Success = false, Message = "Tool does not exist or is unavailable." };

        if (tool.Definition.RequiresConfirmation &&
            !await RequestConfirmationAsync(tool, arguments))
            return new ToolResult { Tool = toolName, Success = false, Message = "The user cancelled the tool execution." };

        return await tool.ExecuteAsync(arguments);
    }

    private Task<bool> RequestConfirmationAsync(ITool tool, Dictionary<string, string> arguments)
    {
        var result = WpfMessageBox.Show(BuildConfirmationMessage(tool, arguments), "Confirm Action",
            WpfMessageBoxButton.OKCancel, WpfMessageBoxImage.Question);
        return Task.FromResult(result == WpfMessageBoxResult.OK);
    }

    private string BuildConfirmationMessage(ITool tool, Dictionary<string, string> arguments)
    {
        var name = tool.Definition.Name;
        if (name == "move_file")
        {
            arguments.TryGetValue("source", out var source);
            arguments.TryGetValue("destination", out var destination);
            return $"Ballknower wants to move/rename this file:\n\nFrom: {source}\nTo: {destination}\n\nNo existing destination will be overwritten. Continue?";
        }
        if (name == "create_file")
        {
            arguments.TryGetValue("path", out var path);
            return $"Ballknower wants to create a new file (existing files will not be overwritten):\n\n{path}\n\nAllow this action?";
        }
        if (name == "delete_file")
        {
            arguments.TryGetValue("path", out var path);
            return $"Ballknower wants to delete this file:\n\n{path}\n\nAllow this action?";
        }
        return $"Ballknower wants to execute '{name}'.\n\nAllow this action?";
    }
}