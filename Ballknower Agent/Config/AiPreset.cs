namespace Ballknower.Config;

public class AiPreset
{
    public string Name { get; set; } = string.Empty;
    public string AIProvider { get; set; } = "Groq";
    public string Model { get; set; } = string.Empty;

    // DPAPI CurrentUser-protected API key. Never store plaintext here.
    public string? EncryptedApiKey { get; set; }
}
