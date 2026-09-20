using System.Text.Json;
using System.Text.Json.Serialization;

namespace AIStudio.Application.Jobs.GenerateAudio;

/// <summary>
/// Durable GenerateAudio input. The narration text itself is derived
/// deterministically from the completed storyboard, so it is not stored here; the
/// payload carries only the stable production choices.
/// </summary>
public sealed record GenerateAudioJobPayload(
    Guid ContentProjectId,
    Guid StoryboardJobId,
    string VoiceProfile = "Formal",
    string Locale = "id-ID")
{
    public const int MaxVoiceProfileLength = 64;
    public const int MaxLocaleLength = 32;

    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web)
        {
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
        };

    public static GenerateAudioJobPayload Deserialize(string json)
    {
        GenerateAudioJobPayload? payload;

        try
        {
            payload = JsonSerializer.Deserialize<GenerateAudioJobPayload>(json, JsonOptions);
        }
        catch (JsonException exception)
        {
            throw InvalidPayload("GenerateAudio payload must be a valid JSON object.", exception);
        }

        if (payload is null || payload.ContentProjectId == Guid.Empty)
        {
            throw InvalidPayload("GenerateAudio payload requires a contentProjectId.");
        }

        if (payload.StoryboardJobId == Guid.Empty)
        {
            throw InvalidPayload("GenerateAudio payload requires a storyboardJobId.");
        }

        if (string.IsNullOrWhiteSpace(payload.VoiceProfile)
            || payload.VoiceProfile.Length > MaxVoiceProfileLength)
        {
            throw InvalidPayload("GenerateAudio payload voiceProfile is invalid.");
        }

        if (string.IsNullOrWhiteSpace(payload.Locale)
            || payload.Locale.Length > MaxLocaleLength)
        {
            throw InvalidPayload("GenerateAudio payload locale is invalid.");
        }

        return payload;
    }

    private static JobExecutionException InvalidPayload(
        string message,
        Exception? innerException = null) =>
        new("audio_job_invalid_payload", message, innerException);
}
