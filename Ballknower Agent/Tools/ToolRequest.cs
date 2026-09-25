using System.Collections.Generic;

namespace Ballknower.Tools;

public class ToolRequest
{
    public string Tool { get; set; } = string.Empty;

    public Dictionary<string, string> Arguments { get; set; } = new();
}