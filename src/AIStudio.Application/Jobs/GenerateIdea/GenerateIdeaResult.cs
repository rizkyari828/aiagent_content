using System.Text.Json;
using System.Text.Json.Serialization;

namespace AIStudio.Application.Jobs.GenerateIdea;

public sealed record GenerateIdeaResult(
    string Title,
    string Hook,
    string Summary,
    string Angle,
    string TargetAudience,
    string SuggestedFormat)
{
    private const int MaxFieldLength = 2_000;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public static GenerateIdeaResult Deserialize(string json)
    {
        GenerateIdeaResult? result;

        try
        {
            result = JsonSerializer.Deserialize<GenerateIdeaResult>(json, JsonOptions);
        }
        catch (JsonException exception)
        {
            throw InvalidResult(
                "AI response does not match the GenerateIdea result schema.",
                exception);
        }

        if (result is null)
        {
            throw InvalidResult("AI response did not contain a GenerateIdea result.");
        }

        return result with
        {
            Title = RequireText(result.Title, "title"),
            Hook = RequireText(result.Hook, "hook"),
            Summary = RequireText(result.Summary, "summary"),
            Angle = RequireText(result.Angle, "angle"),
            TargetAudience = RequireText(result.TargetAudience, "targetAudience"),
            SuggestedFormat = RequireText(result.SuggestedFormat, "suggestedFormat")
        };
    }

    public string Serialize() => JsonSerializer.Serialize(this, JsonOptions);

    private static string RequireText(string? value, string fieldName)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrEmpty(normalized) || normalized.Length > MaxFieldLength)
        {
            throw InvalidResult(
                $"AI response field '{fieldName}' must contain 1 to {MaxFieldLength} characters.");
        }

        return normalized;
    }

    private static JobExecutionException InvalidResult(
        string message,
        Exception? innerException = null) =>
        new("generate_idea_invalid_result", message, innerException);
}
