using System.Collections.Generic;

namespace Ballknower.Config;

public class AppSettings
{
    public string AIProvider { get; set; } = "Groq";

    public string OpenRouterModel { get; set; } =
        "nvidia/nemotron-3-ultra-550b-a55b-20260604:free";

    public string GroqModel { get; set; } =
        "openai/gpt-oss-120b";

    public string OpenAIModel { get; set; } =
        "gpt-4o-mini";

    public string GeminiModel { get; set; } =
        "gemini-2.5-flash";

    public bool StreamResponses { get; set; } = true;

    public string SearchProvider { get; set; } = "DuckDuckGo";

    public bool JailbreakEnabled { get; set; } = false;

    public string JailbreakPrompt { get; set; } = string.Empty;

    public string OpeningShortcut { get; set; } = "Alt+Win";

    // Approximate conversation history budget per AI request.
    // Does not include the model's response or all tool overhead.
    public int HistoryTokenBudget { get; set; } = 2000;

    public string StylePreset { get; set; } = "Default";

    // Unified applies the selected palette to both light and dark states.
    public string StyleThemeMode { get; set; } = "Unified";

    public string LightThemeBackground { get; set; } = "#FFFFFFFF";
    public string LightThemeForeground { get; set; } = "#FF000000";
    public string DarkThemeBackground { get; set; } = "#FF000000";
    public string DarkThemeForeground { get; set; } = "#FFFFFFFF";

    public string StyleFontFamily { get; set; } = "Segoe UI";
    public bool StyleAnimatedEffects { get; set; } = true;
    public bool StyleRainbowBorder { get; set; } = true;
    public bool StyleGlowEffect { get; set; } = true;

    public Dictionary<string, string> Shortcuts { get; set; } =
        new();

    public List<AiPreset> AiPresets { get; set; } = new();
}