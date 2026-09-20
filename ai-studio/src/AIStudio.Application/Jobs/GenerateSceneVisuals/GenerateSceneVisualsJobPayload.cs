using System.Text.Json;
using System.Text.Json.Serialization;

namespace AIStudio.Application.Jobs.GenerateSceneVisuals;

public sealed record GenerateSceneVisualsJobPayload(
    Guid ContentProjectId,
    Guid StoryboardJobId,
    bool Force = false)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public static GenerateSceneVisualsJobPayload Deserialize(string json)
    {
        GenerateSceneVisualsJobPayload? payload;

        try
        {
            payload = JsonSerializer.Deserialize<GenerateSceneVisualsJobPayload>(json, JsonOptions);
        }
        catch (JsonException exception)
        {
            throw InvalidPayload(
                "GenerateSceneVisuals payload must be a valid JSON object.",
                exception);
        }

        if (payload is null || payload.ContentProjectId == Guid.Empty)
        {
            throw InvalidPayload("GenerateSceneVisuals payload requires a contentProjectId.");
        }

        if (payload.StoryboardJobId == Guid.Empty)
        {
            throw InvalidPayload("GenerateSceneVisuals payload requires a storyboardJobId.");
        }

        return payload;
    }

    private static JobExecutionException InvalidPayload(
        string message,
        Exception? innerException = null) =>
        new("visual_job_invalid_payload", message, innerException);
}
