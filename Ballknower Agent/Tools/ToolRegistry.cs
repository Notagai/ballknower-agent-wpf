using System.Collections.Generic;
using System.Linq;

namespace Ballknower.Tools;

public class ToolRegistry
{
    private readonly Dictionary<string, ITool> _tools = new();

    // When enabled, only public web search is available to the model.
    public bool WebSearchOnlyMode { get; set; }

    private bool IsAllowed(ITool tool) =>
        !WebSearchOnlyMode || tool.Definition.Name == "web_search";

    public void Register(ITool tool)
    {
        _tools[tool.Definition.Name] = tool;
    }

    public bool TryGetTool(
        string name,
        out ITool? tool)
    {
        // Once web search is actually invoked, lock the remainder of this
        // turn to web search only. The caller resets WebSearchOnlyMode at
        // the start of the next user turn, so this is a per-turn capability
        // boundary rather than a permanent restriction.
        if (!WebSearchOnlyMode &&
            name == "web_search")
        {
            WebSearchOnlyMode = true;
        }

        if (WebSearchOnlyMode && name != "web_search")
        {
            tool = null;
            return false;
        }

        return _tools.TryGetValue(
            name,
            out tool);
    }

    public IReadOnlyCollection<ITool> GetAll()
    {
        return _tools.Values
            .Where(IsAllowed)
            .ToArray();
    }

    public object[] GetAiToolDefinitions()
    {
        return GetAll()
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