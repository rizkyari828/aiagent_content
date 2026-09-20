using System.Text.Json;
using AIStudio.Application.Jobs.GenerateStoryboard;
using AIStudio.Application.Rendering.AudioProduction;

namespace AIStudio.Application.Rendering.Narration;

/// <summary>One reviewed-script narration segment.</summary>
public sealed record ReviewedNarrationSection(string Heading, string Narration);

/// <summary>Per-scene narration text resolved from the approved reviewed script.</summary>
public sealed record SceneNarrationText(
    int SceneIndex,
    string Heading,
    string Text,
    string Kind);

/// <summary>
/// Parses the canonical approved <c>ReviewedScript.Content</c> and maps it to
/// storyboard scenes deterministically. The reviewed script is the single source of
/// truth for TTS, subtitles, scene timing and choreography; storyboard headings are
/// metadata only and are never used as spoken text.
/// </summary>
public sealed record ReviewedNarrationScript(
    string Title,
    string OpeningHook,
    IReadOnlyList<ReviewedNarrationSection> Sections,
    string Closing)
{
    private static readonly string[] OpeningKeywords = ["opening", "hook", "pembuka", "intro"];
    private static readonly string[] ClosingKeywords = ["closing", "penutup", "kesimpulan", "outro"];

    public static ReviewedNarrationScript Parse(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            throw Invalid("The reviewed script is empty.");
        }

        try
        {
            using var document = JsonDocument.Parse(content);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw Invalid("The reviewed script must be a JSON object.");
            }

            var title = ReadString(root, "title");
            var opening = ReadString(root, "openingHook");
            var closing = ReadString(root, "closing");

            var sections = new List<ReviewedNarrationSection>();
            if (root.TryGetProperty("sections", out var sectionsNode)
                && sectionsNode.ValueKind == JsonValueKind.Array)
            {
                foreach (var section in sectionsNode.EnumerateArray())
                {
                    if (section.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }

                    var heading = ReadString(section, "heading");
                    var narration = ReadString(section, "narration");
                    if (heading.Length > 0 && narration.Length > 0)
                    {
                        sections.Add(new ReviewedNarrationSection(heading, narration));
                    }
                }
            }

            return new ReviewedNarrationScript(title, opening, sections, closing);
        }
        catch (JsonException exception)
        {
            throw Invalid("The reviewed script is not valid JSON.", exception);
        }
    }

    /// <summary>
    /// Maps the reviewed narration to scenes. Preferred mapping is positional
    /// (opening hook, then sections in order, then closing); when the counts differ
    /// it falls back to exact heading matching. If neither is reliable it fails
    /// rather than fabricating narration from headings.
    /// </summary>
    public IReadOnlyList<SceneNarrationText> MapToScenes(GenerateStoryboardResult storyboard)
    {
        ArgumentNullException.ThrowIfNull(storyboard);

        var ordered = new List<(string Heading, string Text, string Kind)>();
        if (OpeningHook.Length > 0)
        {
            ordered.Add(("Opening Hook", NarrationText.Normalize(OpeningHook), "opening"));
        }

        foreach (var section in Sections)
        {
            ordered.Add((section.Heading, NarrationText.Normalize(section.Narration), "section"));
        }

        if (Closing.Length > 0)
        {
            ordered.Add(("Closing", NarrationText.Normalize(Closing), "closing"));
        }

        if (ordered.Count == storyboard.Scenes.Count)
        {
            return ordered
                .Select((segment, index) => new SceneNarrationText(
                    index,
                    storyboard.Scenes[index].Heading,
                    segment.Text,
                    segment.Kind))
                .ToArray();
        }

        var byHeading = Sections.ToDictionary(
            section => section.Heading,
            section => section.Narration,
            StringComparer.OrdinalIgnoreCase);

        var mapped = new List<SceneNarrationText>(storyboard.Scenes.Count);
        for (var index = 0; index < storyboard.Scenes.Count; index++)
        {
            var heading = storyboard.Scenes[index].Heading.Trim();
            string? text = null;
            var kind = "heading";

            if (byHeading.TryGetValue(heading, out var sectionText))
            {
                text = sectionText;
                kind = "section";
            }
            else if (Contains(heading, OpeningKeywords))
            {
                text = OpeningHook;
                kind = "opening";
            }
            else if (Contains(heading, ClosingKeywords))
            {
                text = Closing;
                kind = "closing";
            }

            if (string.IsNullOrWhiteSpace(text))
            {
                throw Unmapped(
                    $"Scene {index} ('{heading}') has no matching reviewed narration segment.");
            }

            mapped.Add(new SceneNarrationText(index, heading, text, kind));
        }

        return mapped;
    }

    private static string ReadString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value)
            && value.ValueKind == JsonValueKind.String
                ? value.GetString()?.Trim() ?? string.Empty
                : string.Empty;

    private static bool Contains(string text, string[] keywords) =>
        keywords.Any(keyword => text.Contains(keyword, StringComparison.OrdinalIgnoreCase));

    private static AudioProductionException Invalid(
        string message,
        Exception? innerException = null) =>
        new("audio_narration_source_invalid", message, innerException);

    private static AudioProductionException Unmapped(string message) =>
        new("audio_narration_source_unmapped", message);
}
