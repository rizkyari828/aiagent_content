using System.Text.Json;
using System.Text.Json.Serialization;
using AIStudio.Application.Bibles;
using AIStudio.Application.Creative;
using AIStudio.Application.Jobs.GenerateIdea;
using AIStudio.Application.Stories;

namespace AIStudio.Application.Jobs.GenerateScript;

public sealed record GenerateScriptJobPayload(
    Guid ContentProjectId,
    GenerateIdeaResult SelectedIdea,
    string? Language = null,
    CreativeDirection? CreativeDirection = null,
    StoryPlan? StoryPlan = null,
    IReadOnlyList<CharacterState>? CharacterStates = null,
    IReadOnlyList<WorldState>? WorldStates = null)
{
    private const int MaxLanguageLength = 100;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public static GenerateScriptJobPayload Deserialize(string json)
    {
        GenerateScriptJobPayload? payload;

        try
        {
            payload = JsonSerializer.Deserialize<GenerateScriptJobPayload>(json, JsonOptions);
        }
        catch (JsonException exception)
        {
            throw InvalidPayload("GenerateScript payload must be a valid JSON object.", exception);
        }

        if (payload is null || payload.ContentProjectId == Guid.Empty)
        {
            throw InvalidPayload("GenerateScript payload requires a contentProjectId.");
        }

        if (payload.SelectedIdea is null)
        {
            throw InvalidPayload("GenerateScript payload requires a selectedIdea.");
        }

        GenerateIdeaResult selectedIdea;
        try
        {
            selectedIdea = GenerateIdeaResult.Deserialize(payload.SelectedIdea.Serialize());
        }
        catch (JobExecutionException exception)
        {
            throw InvalidPayload("GenerateScript payload selectedIdea is invalid.", exception);
        }

        return payload with
        {
            SelectedIdea = selectedIdea,
            Language = string.IsNullOrWhiteSpace(payload.Language)
                ? "Indonesian"
                : RequireText(payload.Language, MaxLanguageLength, "language")
        };
    }

    private static string RequireText(string? value, int maxLength, string fieldName)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrEmpty(normalized) || normalized.Length > maxLength)
        {
            throw InvalidPayload(
                $"GenerateScript payload field '{fieldName}' must contain 1 to {maxLength} characters.");
        }

        return normalized;
    }

    private static JobExecutionException InvalidPayload(
        string message,
        Exception? innerException = null) =>
        new("generate_script_invalid_payload", message, innerException);
}
