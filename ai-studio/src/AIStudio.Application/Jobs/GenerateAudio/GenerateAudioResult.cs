using System.Text.Json;
using System.Text.Json.Serialization;

namespace AIStudio.Application.Jobs.GenerateAudio;

public sealed record GenerateAudioArtifact(
    string Path,
    string ContentHash,
    long ByteSize,
    double DurationSeconds,
    int SampleRate,
    int Channels);

/// <summary>
/// Canonical GenerateAudio evidence: the three durable stage artifacts plus which
/// of them were reused from a previous run. Nothing here holds file bytes.
/// </summary>
public sealed record GenerateAudioResult(
    Guid StoryboardJobId,
    GenerateAudioArtifact Narration,
    GenerateAudioArtifact Music,
    GenerateAudioArtifact Master,
    bool NarrationReused,
    bool MusicReused,
    bool MasterReused)
{
    public const int ContentHashLength = 64;
    public const int MaxPathLength = 1024;

    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web)
        {
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
        };

    public static GenerateAudioResult Deserialize(string json)
    {
        GenerateAudioResult? result;

        try
        {
            result = JsonSerializer.Deserialize<GenerateAudioResult>(json, JsonOptions);
        }
        catch (JsonException exception)
        {
            throw InvalidResult("GenerateAudio result JSON does not match the result schema.", exception);
        }

        if (result is null)
        {
            throw InvalidResult("GenerateAudio result is missing.");
        }

        return Normalize(result);
    }

    public string Serialize() => JsonSerializer.Serialize(Normalize(this), JsonOptions);

    private static GenerateAudioResult Normalize(GenerateAudioResult result)
    {
        if (result.StoryboardJobId == Guid.Empty)
        {
            throw InvalidResult("GenerateAudio result requires a storyboardJobId.");
        }

        return result with
        {
            Narration = NormalizeArtifact(result.Narration, "narration"),
            Music = NormalizeArtifact(result.Music, "music"),
            Master = NormalizeArtifact(result.Master, "master")
        };
    }

    private static GenerateAudioArtifact NormalizeArtifact(
        GenerateAudioArtifact? artifact,
        string stage)
    {
        if (artifact is null)
        {
            throw InvalidResult($"GenerateAudio result requires the {stage} artifact.");
        }

        if (artifact.ByteSize <= 0)
        {
            throw InvalidResult($"GenerateAudio {stage} byte size must be greater than zero.");
        }

        if (artifact.DurationSeconds <= 0)
        {
            throw InvalidResult($"GenerateAudio {stage} duration must be greater than zero.");
        }

        if (artifact.SampleRate <= 0 || artifact.Channels <= 0)
        {
            throw InvalidResult($"GenerateAudio {stage} format is invalid.");
        }

        var path = artifact.Path?.Trim();
        if (string.IsNullOrEmpty(path) || path.Length > MaxPathLength)
        {
            throw InvalidResult($"GenerateAudio {stage} path is invalid.");
        }

        if (string.IsNullOrWhiteSpace(artifact.ContentHash)
            || artifact.ContentHash.Length != ContentHashLength
            || !artifact.ContentHash.All(Uri.IsHexDigit))
        {
            throw InvalidResult($"GenerateAudio {stage} content hash must be a SHA-256 value.");
        }

        return artifact with
        {
            Path = path,
            ContentHash = artifact.ContentHash.ToLowerInvariant()
        };
    }

    private static JobExecutionException InvalidResult(
        string message,
        Exception? innerException = null) =>
        new("audio_result_invalid", message, innerException);
}
