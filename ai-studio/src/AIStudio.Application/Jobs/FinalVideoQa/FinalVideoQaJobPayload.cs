using System.Text.Json;
using System.Text.Json.Serialization;

namespace AIStudio.Application.Jobs.FinalVideoQa;

public sealed record FinalVideoQaJobPayload(Guid ContentProjectId, Guid RenderJobId)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public static FinalVideoQaJobPayload Deserialize(string json)
    {
        FinalVideoQaJobPayload? payload;

        try
        {
            payload = JsonSerializer.Deserialize<FinalVideoQaJobPayload>(json, JsonOptions);
        }
        catch (JsonException exception)
        {
            throw InvalidPayload("FinalVideoQa payload must be a valid JSON object.", exception);
        }

        if (payload is null || payload.ContentProjectId == Guid.Empty)
        {
            throw InvalidPayload("FinalVideoQa payload requires a contentProjectId.");
        }

        if (payload.RenderJobId == Guid.Empty)
        {
            throw InvalidPayload("FinalVideoQa payload requires a renderJobId.");
        }

        return payload;
    }

    private static JobExecutionException InvalidPayload(
        string message,
        Exception? innerException = null) =>
        new("final_video_qa_invalid_payload", message, innerException);
}
