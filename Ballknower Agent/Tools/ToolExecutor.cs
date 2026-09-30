using Ballknower.Diagnostics;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
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

    private async Task<bool> RequestConfirmationAsync(ITool tool, Dictionary<string, string> arguments)
    {
        // Tool calls can be executed from a background async continuation.
        // WPF dialogs must be created on the UI thread; otherwise an STA/thread
        // affinity exception can terminate the desktop app.
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null)
            return false;

        try
        {
            if (dispatcher.CheckAccess())
            {
                return ShowConfirmation(tool, arguments);
            }

            return await dispatcher.InvokeAsync(
                () => ShowConfirmation(tool, arguments));
        }
        catch (Exception ex)
        {
            AppLogger.Error("Google Drive/tool confirmation dialog failed", ex);
            return false;
        }
    }

    private static bool ShowConfirmation(ITool tool, Dictionary<string, string> arguments)
    {
        try
        {
            var owner = Application.Current?.MainWindow;
            var dialog = new Window
            {
                Title = "Confirm Action",
                Width = 520,
                SizeToContent = SizeToContent.Height,
                MinHeight = 220,
                ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = owner is null
                    ? WindowStartupLocation.CenterScreen
                    : WindowStartupLocation.CenterOwner,
                ShowInTaskbar = false,
                Owner = owner
            };

            var panel = new StackPanel { Margin = new Thickness(20) };
            panel.Children.Add(new TextBlock
            {
                Text = BuildConfirmationMessage(tool, arguments),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 20)
            });

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };

            var cancel = new Button
            {
                Content = "Cancel",
                MinWidth = 90,
                Margin = new Thickness(8, 0, 0, 0),
                IsCancel = true
            };

            var allow = new Button
            {
                Content = "Allow",
                MinWidth = 90,
                IsDefault = true
            };

            allow.Click += (_, _) => dialog.DialogResult = true;
            cancel.Click += (_, _) => dialog.DialogResult = false;

            buttons.Children.Add(cancel);
            buttons.Children.Add(allow);
            panel.Children.Add(buttons);
            dialog.Content = panel;

            return dialog.ShowDialog() == true;
        }
        catch (Exception ex)
        {
            AppLogger.Error("Confirmation dialog failed", ex);
            return false;
        }
    }

    private static string BuildConfirmationMessage(ITool tool, Dictionary<string, string> arguments)
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
        if (name == "drive_write")
        {
            arguments.TryGetValue("operation", out var operation);
            arguments.TryGetValue("file_id", out var fileId);
            arguments.TryGetValue("name", out var nameValue);
            arguments.TryGetValue("parent_id", out var parentId);
            arguments.TryGetValue("content", out var content);

            var detail = operation switch
            {
                "create_text" => $"Create a new text file\nName: {nameValue}\nFolder ID: {parentId ?? "(Drive root)"}\nContent: {SummarizeContent(content)}",
                "update_text" => $"Update file\nFile ID: {fileId}\nNew content: {SummarizeContent(content)}",
                "rename" => $"Rename file\nFile ID: {fileId}\nNew name: {nameValue}",
                "move" => $"Move file\nFile ID: {fileId}\nDestination folder ID: {parentId}",
                "delete" => $"Delete file\nFile ID: {fileId}",
                _ => $"Operation: {operation}"
            };

            return $"Ballknower wants to make this Google Drive change:\n\n{detail}\n\nAllow this action?";
        }
        return $"Ballknower wants to execute '{name}'.\n\nAllow this action?";
    }

    private static string SummarizeContent(string? content)
    {
        if (string.IsNullOrEmpty(content))
            return "(empty)";

        const int maxLength = 500;
        var singleLine = content.Replace("\r", " ").Replace("\n", " ");
        return singleLine.Length <= maxLength
            ? singleLine
            : singleLine[..maxLength] + "…";
    }
}