namespace AIStudio.Application.AI;

public sealed record AiTextResponse(
    string Text,
    string Model,
    TimeSpan? TotalDuration,
    int? PromptTokenCount,
    int? OutputTokenCount);
