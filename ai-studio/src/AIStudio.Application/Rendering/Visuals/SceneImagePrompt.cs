namespace AIStudio.Application.Rendering.Visuals;

/// <summary>
/// Pure, deterministic prompt composer for the AI image engine. It turns the
/// structured brief into a single text-to-image prompt and keeps display text out
/// of the generated pixels (the model is told not to draw words), so the still
/// stays a background/illustration rather than a second text layer.
/// <para>
/// Two modes: a technical explainer still (heading plus card labels, tech style),
/// and a narrative scene (the storyboard's cinematic <c>visual</c> description).
/// Narrative mode is selected from the production routing context, never from a
/// hardcoded genre, so technical scenes keep their existing brief/template output.
/// </para>
/// </summary>
public static class SceneImagePrompt
{
    private const string TechStyle =
        "clean modern editorial illustration for a technology explainer video, "
        + "soft 3D shading with flat vector shapes, uncluttered composition, "
        + "generous empty margins, cinematic soft lighting";

    /// <summary>
    /// Neutral, genre-free narrative style. Genre (for example anime) must come
    /// from production/concept data, not from this composer.
    /// </summary>
    private const string NarrativeStyle =
        "cinematic storyboard illustration, cohesive art direction, detailed "
        + "environment, expressive subjects, dramatic cinematic lighting, rich atmosphere";

    private const string NoText =
        "absolutely no text, no words, no letters, no numbers, no typography, "
        + "no captions, no labels, no signage, no logos or brand marks, "
        + "blank unlabeled screens, no watermark, no interface text, no user-interface elements";

    public static string Build(
        SceneVisualBrief brief,
        bool narrative = false,
        string? artDirection = null)
    {
        ArgumentNullException.ThrowIfNull(brief);

        // Narrative AiImage scenes are described by the storyboard's cinematic
        // `visual` text, never by the SVG card/heading labels that exist only to
        // render technical slides. Caller-supplied art direction (data, never a
        // hardcoded genre) replaces the neutral fallback style when present.
        if (narrative && !string.IsNullOrWhiteSpace(brief.VisualDescription))
        {
            var style = string.IsNullOrWhiteSpace(artDirection)
                ? NarrativeStyle
                : Clean(artDirection).TrimEnd('.', ' ', ',', ';');
            return string.Join(
                ". ",
                style,
                $"scene: {Clean(brief.VisualDescription).TrimEnd('.', ' ', ',', ';')}",
                NoText) + ".";
        }

        var parts = new List<string>
        {
            TechStyle,
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
