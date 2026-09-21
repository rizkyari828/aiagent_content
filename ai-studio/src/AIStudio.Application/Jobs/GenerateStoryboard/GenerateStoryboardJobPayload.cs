using System.Text.Json;
using System.Text.Json.Serialization;
using AIStudio.Application.Bibles;
using AIStudio.Application.Creative;
using AIStudio.Application.Stories;

namespace AIStudio.Application.Jobs.GenerateStoryboard;

public sealed record GenerateStoryboardJobPayload(
    Guid ContentProjectId,
    CreativeDirection? CreativeDirection = null,
    StoryPlan? StoryPlan = null,
    IReadOnlyList<CharacterState>? CharacterStates = null,
    IReadOnlyList<WorldState>? WorldStates = null)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public static GenerateStoryboardJobPayload Deserialize(string json)
    {
        GenerateStoryboardJobPayload? payload;

        try
        {
            payload = JsonSerializer.Deserialize<GenerateStoryboardJobPayload>(json, JsonOptions);
        }
        catch (JsonException exception)
        {
            throw InvalidPayload(
                "GenerateStoryboard payload must be a valid JSON object.",
                exception);
        }

        if (payload is null || payload.ContentProjectId == Guid.Empty)
        {
            throw InvalidPayload("GenerateStoryboard payload requires a contentProjectId.");
        }

        return payload;
    }

    private static JobExecutionException InvalidPayload(
        string message,
        Exception? innerException = null) =>
        new("generate_storyboard_invalid_payload", message, innerException);
}
