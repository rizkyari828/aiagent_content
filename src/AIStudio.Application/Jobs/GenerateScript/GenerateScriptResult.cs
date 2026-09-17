using System.Text.Json;
using System.Text.Json.Serialization;

namespace AIStudio.Application.Jobs.GenerateScript;

public sealed record GenerateScriptResult(
    string Title,
    string OpeningHook,
    IReadOnlyList<GenerateScriptSection> Sections,
    string Closing)
{
    private const int MaxTitleLength = 500;
    private const int MaxShortTextLength = 2_000;
    private const int MaxHeadingLength = 500;
    private const int MaxNarrationLength = 8_000;
    private const int MaxSections = 20;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public static GenerateScriptResult Deserialize(string json)
    {
        GenerateScriptResult? result;

        try
        {
            result = JsonSerializer.Deserialize<GenerateScriptResult>(json, JsonOptions);
        }
        catch (JsonException exception)
        {
            throw InvalidResult(
                "AI response does not match the GenerateScript result schema.",
                exception);
        }

        if (result is null)
        {
            throw InvalidResult("AI response did not contain a GenerateScript result.");
        }

        return Normalize(result);
    }

    public string Serialize() => JsonSerializer.Serialize(Normalize(this), JsonOptions);

    private static GenerateScriptResult Normalize(GenerateScriptResult result)
    {
        if (result.Sections is null || result.Sections.Count is < 1 or > MaxSections)
        {
            throw InvalidResult(
                $"AI response field 'sections' must contain 1 to {MaxSections} sections.");
        }

        var sections = result.Sections
            .Select((section, index) => section is null
                ? throw InvalidResult($"AI response section {index + 1} is required.")
                : new GenerateScriptSection(
                    RequireText(section.Heading, MaxHeadingLength, $"sections[{index}].heading"),
                    RequireText(section.Narration, MaxNarrationLength, $"sections[{index}].narration")))
            .ToArray();

        return result with
        {
            Title = RequireText(result.Title, MaxTitleLength, "title"),
            OpeningHook = RequireText(result.OpeningHook, MaxShortTextLength, "openingHook"),
            Sections = sections,
            Closing = RequireText(result.Closing, MaxShortTextLength, "closing")
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
        new("generate_script_invalid_result", message, innerException);
}

public sealed record GenerateScriptSection(string Heading, string Narration);
