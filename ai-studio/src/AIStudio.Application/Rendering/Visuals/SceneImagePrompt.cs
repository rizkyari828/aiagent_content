namespace AIStudio.Application.Rendering.Visuals;

/// <summary>
/// Pure, deterministic prompt composer for the AI image engine. It turns the
/// structured brief into a single text-to-image prompt and keeps display text out
/// of the generated pixels (the model is told not to draw words), so the still
/// stays a background/illustration rather than a second text layer.
/// </summary>
public static class SceneImagePrompt
{
    private const string Style =
        "clean modern editorial illustration for a technology explainer video, "
        + "soft 3D shading with flat vector shapes, uncluttered composition, "
        + "generous empty margins, cinematic soft lighting";

    private const string NoText =
        "no text, no words, no letters, no numbers, no captions, no watermark, no logo";

    public static string Build(SceneVisualBrief brief)
    {
        ArgumentNullException.ThrowIfNull(brief);

        var parts = new List<string>
        {
            Style,
            $"scene: {Clean(brief.Heading)}"
        };

        var subjects = brief.Cards
            .Select(card => Clean(card.Title))
            .Where(title => title.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(4)
            .ToArray();
        if (subjects.Length > 0)
        {
            parts.Add($"subjects: {string.Join(", ", subjects)}");
        }

        parts.Add(NoText);
        return string.Join(". ", parts) + ".";
    }

    private static string Clean(string value) =>
        value.Replace('\r', ' ').Replace('\n', ' ').Trim();
}
