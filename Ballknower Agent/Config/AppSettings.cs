using System.Collections.Generic;

namespace Ballknower.Config;

public class AppSettings
{
    public string AIProvider { get; set; } = "Groq";

    public string OpenRouterModel { get; set; } =
        "nvidia/nemotron-3-ultra-550b-a55b-20260604:free";

    public string GroqModel { get; set; } =
        "openai/gpt-oss-120b";

    public bool StreamResponses { get; set; } = true;

    public string OpeningShortcut { get; set; } = "Alt+Win";

    // Approximate conversation history budget per AI request.
    // Does not include the model's response or all tool overhead.
    public int HistoryTokenBudget { get; set; } = 2000;

    public Dictionary<string, string> Shortcuts { get; set; } =
        new();
}