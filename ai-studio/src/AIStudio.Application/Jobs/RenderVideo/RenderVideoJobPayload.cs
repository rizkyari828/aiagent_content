using System.Text.Json;
using System.Text.Json.Serialization;

namespace AIStudio.Application.Jobs.RenderVideo;

public sealed record RenderVideoJobPayload(Guid ContentProjectId, Guid StoryboardJobId)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public static RenderVideoJobPayload Deserialize(string json)
    {
        RenderVideoJobPayload? payload;

        try
        {
            payload = JsonSerializer.Deserialize<RenderVideoJobPayload>(json, JsonOptions);
        }
        catch (JsonException exception)
        {
            throw InvalidPayload("RenderVideo payload must be a valid JSON object.", exception);
        }

        if (payload is null || payload.ContentProjectId == Guid.Empty)
        {
            throw InvalidPayload("RenderVideo payload requires a contentProjectId.");
        }

        if (payload.StoryboardJobId == Guid.Empty)
        {
            throw InvalidPayload("RenderVideo payload requires a storyboardJobId.");
        }

        return payload;
    }

    private static JobExecutionException InvalidPayload(
        string message,
        Exception? innerException = null) =>
        new("render_video_invalid_payload", message, innerException);
}
