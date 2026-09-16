namespace AIStudio.Application.AI;

public sealed record AiTextRequest(
    string Prompt,
    string? SystemPrompt = null,
    string? Model = null,
    AiResponseFormat ResponseFormat = AiResponseFormat.Text,
    double? Temperature = null,
    int? MaxTokens = null);
