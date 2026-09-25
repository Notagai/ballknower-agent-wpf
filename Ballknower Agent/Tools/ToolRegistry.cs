using System.Collections.Generic;
using System.Linq;

namespace Ballknower.Tools;

public class ToolRegistry
{
    private readonly Dictionary<string, ITool> _tools = new();

    public void Register(ITool tool)
    {
        _tools[tool.Definition.Name] = tool;
    }

    public bool TryGetTool(
        string name,
        out ITool? tool)
    {
        return _tools.TryGetValue(
            name,
            out tool);
    }

    public IReadOnlyCollection<ITool> GetAll()
    {
        return _tools.Values;
    }

    public object[] GetAiToolDefinitions()
    {
        return _tools.Values
            .Select(tool => new
            {
                type = "function",

                function = new
                {
                    name =
                        tool.Definition.Name,

                    description =
                        tool.Definition.Description,

                    parameters =
                        tool.Definition.Parameters
                }
            })
            .ToArray();
    }
}