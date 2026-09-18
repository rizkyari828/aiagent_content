using System.Text.Json;
using System.Text.Json.Serialization;

namespace AIStudio.Application.Jobs.FinalVideoQa;

public sealed record FinalVideoQaResult(
    string OutputPath,
    string ContentHash,
    double DurationSeconds,
    int Width,
    int Height,
    bool HasVideo,
    bool HasAudio,
    bool HasSubtitle)
{
    public const int ContentHashLength = 64;
    public const int MaxOutputPathLength = 1024;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public static FinalVideoQaResult Deserialize(string json)
    {
        FinalVideoQaResult? result;

        try
        {
            result = JsonSerializer.Deserialize<FinalVideoQaResult>(json, JsonOptions);
        }
        catch (JsonException exception)
        {
            throw InvalidResult(
                "FinalVideoQa result JSON does not match the result schema.",
                exception);
        }

        if (result is null)
        {
            throw InvalidResult("FinalVideoQa result is missing.");
        }

        return Normalize(result);
    }

    public string Serialize() => JsonSerializer.Serialize(Normalize(this), JsonOptions);

    private static FinalVideoQaResult Normalize(FinalVideoQaResult result)
    {
        if (result.DurationSeconds <= 0)
        {
            throw InvalidResult("FinalVideoQa result duration must be greater than zero.");
        }

        if (result.Width <= 0 || result.Height <= 0)
        {
            throw InvalidResult("FinalVideoQa result dimensions must be greater than zero.");
        }

        if (!result.HasVideo)
        {
            throw InvalidResult("FinalVideoQa result requires a video stream.");
        }

        if (!result.HasAudio)
        {
            throw InvalidResult("FinalVideoQa result requires an audio stream.");
        }

        return result with
        {
            OutputPath = RequireText(
                result.OutputPath,
                MaxOutputPathLength,
                "outputPath"),
            ContentHash = RequireHash(result.ContentHash, "contentHash")
        };
    }

    private static string RequireHash(string? value, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value.Length != ContentHashLength
            || !value.All(Uri.IsHexDigit))
        {
            throw InvalidResult(
                $"FinalVideoQa result field '{fieldName}' must be a 64-character SHA-256 hash.");
        }

        return value.ToLowerInvariant();
    }

    private static string RequireText(string? value, int maxLength, string fieldName)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrEmpty(normalized) || normalized.Length > maxLength)
        {
            throw InvalidResult(
                $"FinalVideoQa result field '{fieldName}' must contain 1 to {maxLength} characters.");
        }

        return normalized;
    }

    private static JobExecutionException InvalidResult(
        string message,
        Exception? innerException = null) =>
        new("final_video_qa_invalid_result", message, innerException);
}
