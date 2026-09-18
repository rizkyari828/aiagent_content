using System.Text.Json;
using System.Text.Json.Serialization;

namespace AIStudio.Application.Jobs.RenderVideo;

public sealed record RenderVideoResult(
    string OutputPath,
    string ContentHash,
    long ByteSize,
    double DurationSeconds,
    int Width,
    int Height,
    Guid StoryboardJobId,
    Guid NarrationTrackId,
    int SceneCount,
    string NarrationContentHash,
    Guid? SubtitleTrackId = null,
    string? SubtitleContentHash = null)
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

        if (result.NarrationTrackId == Guid.Empty)
        {
            throw InvalidResult("RenderVideo result requires a narrationTrackId.");
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

        return result with
        {
            OutputPath = RequireText(
                result.OutputPath,
                MaxOutputPathLength,
                "outputPath"),
            ContentHash = RequireHash(result.ContentHash, "contentHash"),
            NarrationContentHash = RequireHash(result.NarrationContentHash, "narrationContentHash"),
            SubtitleContentHash = result.SubtitleContentHash is null
                ? null
                : RequireHash(result.SubtitleContentHash, "subtitleContentHash")
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
