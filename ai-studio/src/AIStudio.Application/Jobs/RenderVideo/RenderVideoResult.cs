using System.Text.Json;
using System.Text.Json.Serialization;

namespace AIStudio.Application.Jobs.RenderVideo;

/// <summary>
/// Canonical render evidence. <see cref="AudioSource"/> records which audio the
/// render consumed: the legacy per-project narration track (<c>narration</c>) or
/// the mastered audio produced by GenerateAudio (<c>mastered</c>). Legacy results
/// that predate the mastered-audio path deserialize as <c>narration</c>.
/// </summary>
public sealed record RenderVideoResult(
    string OutputPath,
    string ContentHash,
    long ByteSize,
    double DurationSeconds,
    int Width,
    int Height,
    Guid StoryboardJobId,
    Guid? NarrationTrackId,
    int SceneCount,
    string? NarrationContentHash,
    Guid? SubtitleTrackId = null,
    string? SubtitleContentHash = null,
    bool SubtitleBurnedIn = false,
    string AudioSource = RenderAudioSources.Narration,
    string? MasteredAudioPath = null,
    string? MasteredAudioContentHash = null)
{
    public const int ContentHashLength = 64;
    public const int MaxOutputPathLength = 1024;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public static RenderVideoResult Deserialize(string json)
    {
        RenderVideoResult? result;

        try
        {
            result = JsonSerializer.Deserialize<RenderVideoResult>(json, JsonOptions);
        }
        catch (JsonException exception)
        {
            throw InvalidResult(
                "RenderVideo result JSON does not match the result schema.",
                exception);
        }

        if (result is null)
        {
            throw InvalidResult("RenderVideo result is missing.");
        }

        return Normalize(result);
    }

    public string Serialize() => JsonSerializer.Serialize(Normalize(this), JsonOptions);

    private static RenderVideoResult Normalize(RenderVideoResult result)
    {
        if (result.StoryboardJobId == Guid.Empty)
        {
            throw InvalidResult("RenderVideo result requires a storyboardJobId.");
        }

        if (result.SceneCount < 1)
        {
            throw InvalidResult("RenderVideo result requires at least one scene.");
        }

        if (result.ByteSize <= 0)
        {
            throw InvalidResult("RenderVideo result byte size must be greater than zero.");
        }

        if (result.DurationSeconds <= 0)
        {
            throw InvalidResult("RenderVideo result duration must be greater than zero.");
        }

        if (result.Width <= 0 || result.Height <= 0)
        {
            throw InvalidResult("RenderVideo result dimensions must be greater than zero.");
        }

        if (result.SubtitleTrackId == Guid.Empty)
        {
            throw InvalidResult("RenderVideo result subtitleTrackId must not be empty when present.");
        }

        if ((result.SubtitleTrackId is null) != (result.SubtitleContentHash is null))
        {
            throw InvalidResult(
                "RenderVideo result subtitle fields must both be present or both be absent.");
        }

        if (result.SubtitleBurnedIn && result.SubtitleTrackId is null)
        {
            throw InvalidResult(
                "RenderVideo result cannot be marked subtitleBurnedIn without a subtitle track.");
        }

        if ((result.NarrationTrackId is null) != (result.NarrationContentHash is null))
        {
            throw InvalidResult(
                "RenderVideo result narration fields must both be present or both be absent.");
        }

        if (result.AudioSource is not (RenderAudioSources.Narration or RenderAudioSources.Mastered))
        {
            throw InvalidResult("RenderVideo result audioSource is not supported.");
        }

        if (result.AudioSource == RenderAudioSources.Narration
            && result.NarrationTrackId is null)
        {
            throw InvalidResult(
                "RenderVideo result requires a narration track when audioSource is narration.");
        }

        if (result.AudioSource == RenderAudioSources.Mastered
            && string.IsNullOrWhiteSpace(result.MasteredAudioPath))
        {
            throw InvalidResult(
                "RenderVideo result requires a mastered audio path when audioSource is mastered.");
        }

        return result with
        {
            OutputPath = RequireText(
                result.OutputPath,
                MaxOutputPathLength,
                "outputPath"),
            ContentHash = RequireHash(result.ContentHash, "contentHash"),
            NarrationContentHash = result.NarrationContentHash is null
                ? null
                : RequireHash(result.NarrationContentHash, "narrationContentHash"),
            SubtitleContentHash = result.SubtitleContentHash is null
                ? null
                : RequireHash(result.SubtitleContentHash, "subtitleContentHash"),
            MasteredAudioPath = string.IsNullOrWhiteSpace(result.MasteredAudioPath)
                ? null
                : RequireText(result.MasteredAudioPath, MaxOutputPathLength, "masteredAudioPath"),
            MasteredAudioContentHash = result.AudioSource == RenderAudioSources.Mastered
                ? RequireHash(result.MasteredAudioContentHash, "masteredAudioContentHash")
                : null
        };
    }

    private static string RequireHash(string? value, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value.Length != ContentHashLength
            || !value.All(Uri.IsHexDigit))
        {
            throw InvalidResult(
                $"RenderVideo result field '{fieldName}' must be a 64-character SHA-256 hash.");
        }

        return value.ToLowerInvariant();
    }

    private static string RequireText(string? value, int maxLength, string fieldName)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrEmpty(normalized) || normalized.Length > maxLength)
        {
            throw InvalidResult(
                $"RenderVideo result field '{fieldName}' must contain 1 to {maxLength} characters.");
        }

        return normalized;
    }

    private static JobExecutionException InvalidResult(
        string message,
        Exception? innerException = null) =>
        new("render_video_invalid_result", message, innerException);
}

public static class RenderAudioSources
{
    public const string Narration = "narration";

    public const string Mastered = "mastered";
}
