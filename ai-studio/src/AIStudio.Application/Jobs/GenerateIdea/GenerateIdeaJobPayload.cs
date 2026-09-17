using System.Text.Json;
using System.Text.Json.Serialization;

namespace AIStudio.Application.Jobs.GenerateIdea;

public sealed record GenerateIdeaJobPayload(
    Guid ContentProjectId,
    string Topic,
    string? TargetAudience = null,
    string? Language = null,
    string? Model = null)
{
    private const int MaxTopicLength = 1_000;
    private const int MaxAudienceLength = 500;
    private const int MaxLanguageLength = 100;
    private const int MaxModelLength = 200;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public static GenerateIdeaJobPayload Deserialize(string json)
    {
        GenerateIdeaJobPayload? payload;

        try
        {
            payload = JsonSerializer.Deserialize<GenerateIdeaJobPayload>(json, JsonOptions);
        }
        catch (JsonException exception)
        {
            throw InvalidPayload("GenerateIdea payload must be a valid JSON object.", exception);
        }

        if (payload is null || payload.ContentProjectId == Guid.Empty)
        {
            throw InvalidPayload("GenerateIdea payload requires a contentProjectId.");
        }

        return payload with
        {
            Topic = RequireText(payload.Topic, MaxTopicLength, "topic"),
            TargetAudience = OptionalText(
                payload.TargetAudience,
                MaxAudienceLength,
                "targetAudience"),
            Language = string.IsNullOrWhiteSpace(payload.Language)
                ? "English"
                : RequireText(payload.Language, MaxLanguageLength, "language"),
            Model = OptionalText(payload.Model, MaxModelLength, "model")
        };
    }

    private static string RequireText(string? value, int maxLength, string fieldName)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrEmpty(normalized) || normalized.Length > maxLength)
        {
            throw InvalidPayload(
                $"GenerateIdea payload field '{fieldName}' must contain 1 to {maxLength} characters.");
        }

        return normalized;
    }

    private static string? OptionalText(string? value, int maxLength, string fieldName) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : RequireText(value, maxLength, fieldName);

    private static JobExecutionException InvalidPayload(
        string message,
        Exception? innerException = null) =>
        new("generate_idea_invalid_payload", message, innerException);
}
