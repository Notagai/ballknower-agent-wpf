namespace Ballknower.Tools;

public class ToolContextMessage
{
    public string Tool { get; set; } = string.Empty;

    public bool Success { get; set; }

    public string Message { get; set; } = string.Empty;

    public string ToPromptText()
    {
        return
            $"Tool: {Tool}\n" +
            $"Success: {Success}\n" +
            $"Result: {Message}";
    }
}