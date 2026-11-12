namespace AIStudio.Infrastructure.AI.Gemini;

public sealed class GeminiOptions
{
    public const string SectionName = "Gemini";

    public bool Enabled { get; init; }

    public string BaseUrl { get; init; } = "https://generativelanguage.googleapis.com/";

    public string ApiKey { get; init; } = string.Empty;

    public string DefaultModel { get; init; } = "gemini-2.5-flash";

    public int TimeoutSeconds { get; init; } = 120;
}
