namespace Ballknower.Tools;

public class ToolResult
{
    public string Tool { get; set; } = string.Empty;

    public bool Success { get; set; }

    public string Message { get; set; } = string.Empty;

    public ToolContextMessage ToContextMessage()
    {
        return new ToolContextMessage
        {
            Tool = Tool,
            Success = Success,
            Message = Message
        };
    }
}