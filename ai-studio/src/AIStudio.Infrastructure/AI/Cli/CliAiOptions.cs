namespace AIStudio.Infrastructure.AI.Cli;

public sealed class CliAiOptions
{
    public const string SectionName = "CliAi";

    public bool Enabled { get; set; }

    public string Executable { get; set; } = "gemini";

    public string DefaultModel { get; set; } = "gemini-2.5-pro";

    public int TimeoutSeconds { get; set; } = 90;

    public int CooldownSeconds { get; set; } = 300;
}
