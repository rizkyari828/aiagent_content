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
/// One scene's narration evidence: the reviewed narration text, the measured clip,
/// and its resolved position on the production timeline.
/// </summary>
public sealed record GenerateAudioScene(
    int SceneIndex,
    string Heading,
    string NarrationText,
    string NarrationPath,
    string ContentHash,
    double NarrationDurationSeconds,
    double NarrationStartSeconds,
    double VisualStartSeconds,
    double VisualDurationSeconds,
    bool Reused);

/// <summary>
/// Canonical GenerateAudio evidence: the assembled narration, BGM and mastered mix,
/// plus per-scene narration/timing. Nothing here holds file bytes.
/// </summary>
public sealed record GenerateAudioResult(
    Guid StoryboardJobId,
    GenerateAudioArtifact Narration,
    GenerateAudioArtifact Music,
    GenerateAudioArtifact Master,
    bool NarrationReused,
    bool MusicReused,
    bool MasterReused,
    IReadOnlyList<GenerateAudioScene>? Scenes = null,
    double TransitionSeconds = 0)
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

        var scenes = (result.Scenes ?? [])
            .Select(NormalizeScene)
            .ToArray();

        return result with
        {
            Narration = NormalizeArtifact(result.Narration, "narration"),
            Music = NormalizeArtifact(result.Music, "music"),
            Master = NormalizeArtifact(result.Master, "master"),
            Scenes = scenes
        };
    }

    private static GenerateAudioScene NormalizeScene(GenerateAudioScene scene)
    {
        if (scene.SceneIndex < 0)
        {
            throw InvalidResult("GenerateAudio scene index cannot be negative.");
        }

        if (scene.NarrationDurationSeconds <= 0
            || scene.NarrationStartSeconds < 0
            || scene.VisualStartSeconds < 0
            || scene.VisualDurationSeconds <= 0)
        {
            throw InvalidResult($"GenerateAudio scene {scene.SceneIndex} timing is invalid.");
        }

        var path = RequireText(scene.NarrationPath, MaxPathLength, "narrationPath");
        return scene with
        {
            NarrationPath = path,
            ContentHash = RequireHash(scene.ContentHash, "narration contentHash")
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

        return artifact with
        {
            Path = RequireText(artifact.Path, MaxPathLength, $"{stage} path"),
            ContentHash = RequireHash(artifact.ContentHash, $"{stage} contentHash")
        };
    }

    private static string RequireHash(string? value, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value.Length != ContentHashLength
            || !value.All(Uri.IsHexDigit))
        {
            throw InvalidResult($"GenerateAudio result field '{fieldName}' must be a SHA-256 value.");
        }

        return value.ToLowerInvariant();
    }

    private static string RequireText(string? value, int maxLength, string fieldName)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrEmpty(normalized) || normalized.Length > maxLength)
        {
            throw InvalidResult(
                $"GenerateAudio result field '{fieldName}' must contain 1 to {maxLength} characters.");
        }

        return normalized;
    }

    private static JobExecutionException InvalidResult(
        string message,
        Exception? innerException = null) =>
        new("audio_result_invalid", message, innerException);
}
