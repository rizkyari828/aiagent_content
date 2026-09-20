using System.Text.Json;
using System.Text.Json.Serialization;

namespace AIStudio.Application.Jobs.RenderVideo;

public sealed record RenderVideoJobPayload(
    Guid ContentProjectId,
    Guid StoryboardJobId,
    string? Variant = null)
{
    public const int MaxVariantLength = 40;

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

        if (payload.Variant is not null)
        {
            var variant = payload.Variant.Trim();
            if (variant.Length is < 1 or > MaxVariantLength
                || !variant.All(character => char.IsAsciiLetterOrDigit(character) || character == '-'))
            {
                throw InvalidPayload("RenderVideo payload variant is invalid.");
            }

            payload = payload with { Variant = variant };
        }

        return payload;
    }

    private static JobExecutionException InvalidPayload(
        string message,
        Exception? innerException = null) =>
        new("render_video_invalid_payload", message, innerException);
}
