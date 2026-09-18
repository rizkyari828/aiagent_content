using System.Text.Json;
using System.Text.Json.Serialization;

namespace AIStudio.Application.Jobs.GenerateStoryboard;

public sealed record GenerateStoryboardResult(
    string Title,
    IReadOnlyList<StoryboardScene> Scenes)
{
    private const int MaxTitleLength = 500;
    private const int MaxHeadingLength = 500;
    private const int MaxVisualLength = 4_000;
    private const int MaxScenes = 50;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public static GenerateStoryboardResult Deserialize(string json)
    {
        GenerateStoryboardResult? result;

        try
        {
            result = JsonSerializer.Deserialize<GenerateStoryboardResult>(json, JsonOptions);
        }
        catch (JsonException exception)
        {
            throw InvalidResult(
                "AI response does not match the GenerateStoryboard result schema.",
                exception);
        }

        if (result is null)
        {
            throw InvalidResult("AI response did not contain a GenerateStoryboard result.");
        }

        return Normalize(result);
    }

    public string Serialize() => JsonSerializer.Serialize(Normalize(this), JsonOptions);

    private static GenerateStoryboardResult Normalize(GenerateStoryboardResult result)
    {
        if (result.Scenes is null || result.Scenes.Count is < 1 or > MaxScenes)
        {
            throw InvalidResult(
                $"AI response field 'scenes' must contain 1 to {MaxScenes} scenes.");
        }

        var scenes = result.Scenes
            .Select((scene, index) => scene is null
                ? throw InvalidResult($"AI response scene {index + 1} is required.")
                : new StoryboardScene(
                    RequireText(scene.Heading, MaxHeadingLength, $"scenes[{index}].heading"),
                    RequireText(scene.Visual, MaxVisualLength, $"scenes[{index}].visual")))
            .ToArray();

        return result with
        {
            Title = RequireText(result.Title, MaxTitleLength, "title"),
            Scenes = scenes
        };
    }

    private static string RequireText(string? value, int maxLength, string fieldName)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrEmpty(normalized) || normalized.Length > maxLength)
        {
            throw InvalidResult(
                $"AI response field '{fieldName}' must contain 1 to {maxLength} characters.");
        }

        return normalized;
    }

    private static JobExecutionException InvalidResult(
        string message,
        Exception? innerException = null) =>
        new("generate_storyboard_invalid_result", message, innerException);
}

public sealed record StoryboardScene(string Heading, string Visual);
