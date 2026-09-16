namespace AIStudio.Infrastructure.AI;

public sealed class AiOptions
{
    public const string SectionName = "Ai";

    public string Provider { get; set; } = "Ollama";
}
