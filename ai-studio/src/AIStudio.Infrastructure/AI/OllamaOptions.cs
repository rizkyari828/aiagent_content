namespace AIStudio.Infrastructure.AI;

public sealed class OllamaOptions
{
    public const string SectionName = "Ollama";

    public string BaseUrl { get; set; } = "http://127.0.0.1:11434";

    public string DefaultModel { get; set; } = "qwen3.8:27b-q4_K_M";

    public int TimeoutSeconds { get; set; } = 120;
}
