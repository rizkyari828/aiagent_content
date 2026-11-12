namespace AIStudio.Infrastructure.AI.OpenAiCompatible;

public class OpenAiCompatibleOptions
{
    public const string SectionName = "OpenAiCompatible";

    public bool Enabled { get; set; }

    public string BaseUrl { get; set; } = "https://api.openai.com/v1/";

    public string ApiKey { get; set; } = string.Empty;

    public string DefaultModel { get; set; } = "gpt-4o";

    public int TimeoutSeconds { get; set; } = 120;
}
