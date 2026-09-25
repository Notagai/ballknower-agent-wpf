using System.Collections.Generic;
using System.Threading.Tasks;

namespace Ballknower.Tools;

public interface ITool
{
    ToolDefinition Definition { get; }

    Task<ToolResult> ExecuteAsync(
        Dictionary<string, string> arguments);
}