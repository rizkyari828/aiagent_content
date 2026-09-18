using System.Text.Json;
using System.Text.Json.Serialization;

namespace AIStudio.Application.Jobs.GenerateSceneVisuals;

public sealed record GeneratedSceneVisual(
    int SceneIndex,
    string Engine,
    string Template,
    string Path,
    long ByteSize,
    string ContentHash);

public sealed record GenerateSceneVisualsResult(
    Guid StoryboardJobId,
    int SceneCount,
    int GeneratedCount,
    int SkippedCount,
    IReadOnlyList<GeneratedSceneVisual> Visuals)
{
    public const int ContentHashLength = 64;
    public const int MaxPathLength = 1024;
    private const int MaxLabelLength = 64;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public static GenerateSceneVisualsResult Deserialize(string json)
    {
        GenerateSceneVisualsResult? result;

        try
        {
            result = JsonSerializer.Deserialize<GenerateSceneVisualsResult>(json, JsonOptions);
        }
        catch (JsonException exception)
        {
            throw InvalidResult(
                "GenerateSceneVisuals result JSON does not match the result schema.",
                exception);
        }

        if (result is null)
        {
            throw InvalidResult("GenerateSceneVisuals result is missing.");
        }

        return Normalize(result);
    }

    public string Serialize() => JsonSerializer.Serialize(Normalize(this), JsonOptions);

    private static GenerateSceneVisualsResult Normalize(GenerateSceneVisualsResult result)
    {
        if (result.StoryboardJobId == Guid.Empty)
        {
            throw InvalidResult("GenerateSceneVisuals result requires a storyboardJobId.");
        }

        if (result.SceneCount < 1)
        {
            throw InvalidResult("GenerateSceneVisuals result requires at least one scene.");
        }

        if (result.GeneratedCount < 0 || result.GeneratedCount > result.SceneCount)
        {
            throw InvalidResult("GenerateSceneVisuals generatedCount is out of range.");
        }

        if (result.SkippedCount != result.SceneCount - result.GeneratedCount)
        {
            throw InvalidResult(
                "GenerateSceneVisuals skippedCount must equal sceneCount - generatedCount.");
        }

        if (result.Visuals is null || result.Visuals.Count != result.GeneratedCount)
        {
            throw InvalidResult(
                "GenerateSceneVisuals visuals must contain exactly generatedCount entries.");
        }

        return result with
        {
            Visuals = result.Visuals.Select(NormalizeVisual).ToArray()
        };
    }

    private static GeneratedSceneVisual NormalizeVisual(GeneratedSceneVisual visual)
    {
        if (visual.SceneIndex < 0)
        {
            throw InvalidResult("GenerateSceneVisuals scene index cannot be negative.");
        }

        if (visual.ByteSize <= 0)
        {
            throw InvalidResult("GenerateSceneVisuals asset byte size must be greater than zero.");
        }

        if (string.IsNullOrWhiteSpace(visual.Engine)
            || visual.Engine.Length > MaxLabelLength)
        {
            throw InvalidResult("GenerateSceneVisuals engine label is invalid.");
        }

        if (visual.Template is null || visual.Template.Length > MaxLabelLength)
        {
            throw InvalidResult("GenerateSceneVisuals template label is invalid.");
        }

        var path = visual.Path?.Trim();
        if (string.IsNullOrEmpty(path) || path.Length > MaxPathLength)
        {
            throw InvalidResult("GenerateSceneVisuals visual path is invalid.");
        }

        if (string.IsNullOrWhiteSpace(visual.ContentHash)
            || visual.ContentHash.Length != ContentHashLength
            || !visual.ContentHash.All(Uri.IsHexDigit))
        {
            throw InvalidResult(
                "GenerateSceneVisuals content hash must be a 64-character SHA-256 value.");
        }

        return visual with
        {
            Path = path,
            ContentHash = visual.ContentHash.ToLowerInvariant()
        };
    }

    private static JobExecutionException InvalidResult(
        string message,
        Exception? innerException = null) =>
        new("visual_result_invalid", message, innerException);
}
