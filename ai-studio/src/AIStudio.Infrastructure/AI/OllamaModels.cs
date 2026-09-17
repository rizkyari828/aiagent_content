using System.Text.Json.Serialization;

namespace AIStudio.Infrastructure.AI;

internal sealed record OllamaChatRequest(
    [property: JsonPropertyName("model")] string Model,
    [property: JsonPropertyName("messages")] IReadOnlyList<OllamaMessage> Messages,
    [property: JsonPropertyName("stream")] bool Stream,
    [property: JsonPropertyName("format")] string? Format,
    [property: JsonPropertyName("options")] OllamaGenerationOptions? Options);

internal sealed record OllamaMessage(
    [property: JsonPropertyName("role")] string Role,
    [property: JsonPropertyName("content")] string Content);

internal sealed record OllamaGenerationOptions(
    [property: JsonPropertyName("temperature")] double? Temperature,
    [property: JsonPropertyName("num_predict")] int? MaxTokens);

internal sealed class OllamaChatResponse
{
    [JsonPropertyName("model")]
    public string? Model { get; init; }

    [JsonPropertyName("message")]
    public OllamaMessage? Message { get; init; }

    [JsonPropertyName("done")]
    public bool Done { get; init; }

    [JsonPropertyName("total_duration")]
    public long? TotalDurationNanoseconds { get; init; }

    [JsonPropertyName("prompt_eval_count")]
    public int? PromptTokenCount { get; init; }

    [JsonPropertyName("eval_count")]
    public int? OutputTokenCount { get; init; }
}
