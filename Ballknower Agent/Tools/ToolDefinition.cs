namespace Ballknower.Tools;

public class ToolDefinition
{
    public string Name { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public bool RequiresConfirmation { get; set; }

    public object? Parameters { get; set; }
}