namespace HydayTalk.Core;

public enum APIPurpose { Speech, Text }

/// Common OpenAI-compatible services offered in the API settings, so users pick a name
/// instead of remembering an address. Picking one fills its address and a suggested model.
public sealed record APIProvider(string Id, string Title, string? BaseURL, string? KeyPage, string? SpeechModel, string? TextModel)
{
    public static readonly APIProvider OpenAI = new("openAI", "OpenAI", "https://api.openai.com/v1", "https://platform.openai.com/api-keys", "gpt-transcribe", "gpt-4.1-mini");
    public static readonly APIProvider Gemini = new("gemini", "Google Gemini", "https://generativelanguage.googleapis.com/v1beta/openai/", "https://aistudio.google.com/apikey", null, "gemini-3.5-flash-lite");
    public static readonly APIProvider Claude = new("claude", "Claude", "https://api.anthropic.com/v1/", "https://platform.claude.com/settings/keys", null, "claude-haiku-5-5");
    public static readonly APIProvider Groq = new("groq", "Groq", "https://api.groq.com/openai/v1", "https://console.groq.com/keys", "whisper-large-v3-turbo", null);
    public static readonly APIProvider Custom = new("custom", "自訂", null, null, null, null);
    public static readonly APIProvider[] All = [OpenAI, Gemini, Claude, Groq, Custom];

    /// The suggested model, or null when the service is not offered for this purpose.
    public string? Model(APIPurpose purpose) => purpose == APIPurpose.Speech ? SpeechModel : TextModel;

    public static APIProvider[] Choices(APIPurpose purpose) => All.Where(p => p == Custom || p.Model(purpose) != null).ToArray();

    public void Apply(APISettings settings, APIPurpose purpose)
    {
        if (BaseURL is null || Model(purpose) is not { } model) return;
        settings.BaseURL = BaseURL; settings.Model = model;
    }

    public static APIProvider Matching(string baseURL)
    {
        static string Normalized(string url) => url.Trim().ToLowerInvariant().TrimEnd('/');
        var target = Normalized(baseURL);
        return All.FirstOrDefault(p => p.BaseURL != null && Normalized(p.BaseURL) == target) ?? Custom;
    }

    public override string ToString() => Title;
}
