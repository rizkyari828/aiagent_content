using System.Text.RegularExpressions;
using AIStudio.Application.Jobs.GenerateStoryboard;

namespace AIStudio.Application.Rendering.Visuals;

/// <summary>
/// Deterministic visual planner: maps the existing storyboard output
/// (<c>heading</c> + <c>visual</c>) into a structured <see cref="SceneVisualPlan"/>.
/// It derives the SVG layout/palette and, for a small subset of scenes, selects a
/// predefined animation template. It is pure and heuristic based on explicit
/// keywords; the eventual creative-director model can replace it behind the same
/// plan shape without changing the engines.
/// <para>
/// Display text is resolved <em>only</em> from the canonical heading and explicit
/// on-screen strings (quoted labels plus a small concept vocabulary). The raw
/// <c>visual</c> field is creative direction, so it is never truncated into
/// user-facing card/bubble/heading text.
/// </para>
/// </summary>
public static class SceneVisualPlanner
{
    /// <summary>Bump when the mapping changes so job input hashes change.</summary>
    public const int PlannerVersion = 2;

    /// <summary>Animation templates must fit their scene; aligned with the timing floor.</summary>
    public const double AnimationDurationSeconds = SceneTiming.AnimationMinimumSeconds;

    private static readonly SceneVisualPalette[] PaletteCycle =
    [
        SceneVisualPalette.Ocean,
        SceneVisualPalette.Slate,
        SceneVisualPalette.Violet,
        SceneVisualPalette.Sunset
    ];

    private static readonly string[] LocalFlowKeywords =
        ["cloud", "gpu", "internet", "lokal", "local", "offline"];

    private static readonly string[] LaptopKeywords =
        ["laptop", "notebook"];

    private static readonly string[] ChatFlowKeywords =
        ["chat", "percakapan", "prompt", "jawaban", "assistant"];

    // Layout intent comes from the heading (canonical, user-facing), not from the
    // creative-direction prose where nouns like "terminal" may only be described.
    private static readonly string[] ChatLayoutKeywords =
        ["chat", "percakapan", "obrol"];

    private static readonly string[] WindowLayoutKeywords =
        ["terminal", "install", "download", "unduh"];

    private static readonly string[] CardsLayoutKeywords =
    [
        "tips", "checklist", "daftar", "panel", "kartu", "opsi", "langkah",
        "butuh", "kebutuhan", "syarat", "persiapan", "model", "tarik"
    ];

    /// <summary>
    /// Small reusable concept vocabulary. Each entry is a generic communication
    /// pattern (a requirement/option card), not a per-video special case.
    /// </summary>
    private static readonly Concept[] Concepts =
    [
        new("Laptop", SceneVisualIcon.Laptop, ["laptop", "ram"]),
        new("Ruang Disk", SceneVisualIcon.Disk, ["disk", "penyimpanan", "hard disk"]),
        new("Koneksi", SceneVisualIcon.Download, ["internet", "koneksi", "wifi", "wi-fi", "kabel", "unduh", "download"]),
        new("Ollama", SceneVisualIcon.Model, ["ollama"]),
        new("Model AI", SceneVisualIcon.Model, ["model"]),
        new("Chat", SceneVisualIcon.Chat, ["chat", "percakapan", "obrol"]),
        new("GPU", SceneVisualIcon.Bolt, ["gpu"]),
        new("Privasi", SceneVisualIcon.Shield, ["privasi", "gembok", "aman"]),
        new("Tips", SceneVisualIcon.Bulb, ["tips", "ide"])
    ];

    private static readonly string[] DetailCues =
        ["gb", "mb", "ram", "download", "unduh", "minimal", "sekitar", "kosong", "menit"];

    private static readonly Regex QuotedLabelPattern = new(
        "(?<q>['\"])(?<text>[^'\"]{2,80})\\k<q>",
        RegexOptions.Compiled);

    private static readonly Regex CommandPattern = new(
        @"\bollama\s+(?:--version|pull|run|serve|list|install|show|rm|ps)\b(?:\s+[A-Za-z0-9._:\-]+)?",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ModelPullPattern = new(
        @"\bollama\s+pull\s+(?<model>[A-Za-z0-9][A-Za-z0-9._:\-]*)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static IReadOnlyList<SceneVisualPlan> PlanAll(
        GenerateStoryboardResult storyboard,
        bool enableAnimation,
        bool enableAiImages = false,
        bool enableThreeD = false)
    {
        ArgumentNullException.ThrowIfNull(storyboard);
        var plans = new List<SceneVisualPlan>(storyboard.Scenes.Count);
        var localFlowUsed = false;
        var chatFlowUsed = false;
        var laptopThreeDUsed = false;

        for (var index = 0; index < storyboard.Scenes.Count; index++)
        {
            var scene = storyboard.Scenes[index];
            var palette = PaletteCycle[index % PaletteCycle.Length];
            var layout = ClassifyLayout(scene);
            var brief = BuildBrief(scene, index, layout, palette);

            var template = SceneAnimationTemplate.None;
            if (enableAnimation)
            {
                if (!localFlowUsed && ContainsAny(SceneText(scene), LocalFlowKeywords))
                {
                    template = SceneAnimationTemplate.LocalAiFlow;
                    localFlowUsed = true;
                }
                else if (!chatFlowUsed && ContainsAny(SceneText(scene), ChatFlowKeywords))
                {
                    template = SceneAnimationTemplate.ChatFlow;
                    chatFlowUsed = true;
                }
            }

            // Blender is a narrow, explicit selection: at most one scene whose text
            // clearly maps to the trusted local-AI laptop template, and only when no
            // higher-priority animation template already claimed the scene.
            var threeD = SceneThreeDTemplate.None;
            if (enableThreeD
                && template == SceneAnimationTemplate.None
                && !laptopThreeDUsed
                && IsThreeDLaptopScene(scene))
            {
                threeD = SceneThreeDTemplate.LocalAiLaptop;
                laptopThreeDUsed = true;
            }

            var engine = SceneVisualEngineSelector.Select(template, enableAiImages, threeD);
            var animation = engine == SceneVisualEngine.ManimAnimation
                ? BuildAnimationParameters(brief, palette)
                : null;

            plans.Add(new SceneVisualPlan(brief, engine, template, animation, threeD));
        }

        return plans;
    }

    public static SceneVisualLayout ClassifyLayout(StoryboardScene scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        var heading = scene.Heading;

        if (ContainsAny(heading, ChatLayoutKeywords))
        {
            return SceneVisualLayout.Chat;
        }

        if (ContainsAny(heading, WindowLayoutKeywords))
        {
            return SceneVisualLayout.Window;
        }

        // Two or more concrete model options read as a selection grid even when
        // the heading does not say "cards".
        if (ExtractModelNames(scene.Visual).Count >= 2)
        {
            return SceneVisualLayout.Cards;
        }

        if (ContainsAny(heading, CardsLayoutKeywords))
        {
            return SceneVisualLayout.Cards;
        }

        if (ExtractCommand(scene.Visual) is not null)
        {
            return SceneVisualLayout.Window;
        }

        return SceneVisualLayout.Hero;
    }

    private static SceneVisualBrief BuildBrief(
        StoryboardScene scene,
        int index,
        SceneVisualLayout layout,
        SceneVisualPalette palette)
    {
        IReadOnlyList<SceneVisualCard> cards;
        string? note = null;
        string? command = null;
        var progress = 0d;

        switch (layout)
        {
            case SceneVisualLayout.Window:
                command = ExtractCommand(scene.Visual);
                progress = HasProgress(scene.Visual) ? 0.72 : 0;
                cards = [];
                break;
            case SceneVisualLayout.Chat:
                cards = BuildChatCards(scene);
                break;
            case SceneVisualLayout.Cards:
                (cards, note) = BuildCardContent(scene);
                break;
            default:
                cards = BuildHeroCards(scene);
                break;
        }

        return new SceneVisualBrief(
            index,
            "Video 1",
            scene.Heading,
            layout,
            palette,
            cards,
            note,
            command,
            progress);
    }

    private static SceneAnimationParameters BuildAnimationParameters(
        SceneVisualBrief brief,
        SceneVisualPalette palette)
    {
        // Animation parameters are display text too. Only quoted dialogue from a
        // chat scene is safe to surface; everything else stays on the template's
        // own fallback labels.
        var secondary = brief.Layout == SceneVisualLayout.Chat && brief.Cards.Count > 0
            ? brief.Cards[0].Title
            : null;
        var tertiary = brief.Layout == SceneVisualLayout.Chat && brief.Cards.Count > 1
            ? brief.Cards[1].Title
            : null;

        return new(
            brief.Kicker,
            brief.Heading,
            secondary,
            tertiary,
            palette,
            AnimationDurationSeconds);
    }

    private static (IReadOnlyList<SceneVisualCard> Cards, string? Note) BuildCardContent(
        StoryboardScene scene)
    {
        var models = ExtractModelNames(scene.Visual);
        if (models.Count >= 2)
        {
            var modelCards = models
                .Take(4)
                .Select(model => new SceneVisualCard(model, null, SceneVisualIcon.Model))
                .ToArray();
            return (modelCards, ExtractCommand(scene.Visual));
        }

        var concepts = ExtractConceptCards(scene.Visual);
        if (concepts.Count > 0)
        {
            return (concepts, null);
        }

        var labels = ExtractDisplayLabels(scene.Visual);
        if (labels.Count > 0)
        {
            return (labels
                .Take(4)
                .Select(label => new SceneVisualCard(Truncate(label, 34)))
                .ToArray(), null);
        }

        return ([new SceneVisualCard(Truncate(scene.Heading, 34))], null);
    }

    private static IReadOnlyList<SceneVisualCard> ExtractConceptCards(string visual)
    {
        var cards = new List<SceneVisualCard>(4);
        foreach (var concept in Concepts)
        {
            if (cards.Count == 4)
            {
                break;
            }

            var keywordIndex = FirstMatchIndex(visual, concept.Keywords);
            if (keywordIndex < 0)
            {
                continue;
            }

            cards.Add(new SceneVisualCard(
                concept.Title,
                DetailFor(visual, keywordIndex),
                concept.Icon));
        }

        return cards;
    }

    private static IReadOnlyList<SceneVisualCard> BuildHeroCards(StoryboardScene scene)
    {
        var labels = ExtractDisplayLabels(scene.Visual);
        var primary = labels
            .Where(label => label.Length >= 12)
            .OrderByDescending(label => label.Length)
            .FirstOrDefault();

        var cards = new List<SceneVisualCard>
        {
            primary is not null
                ? new SceneVisualCard(Headline(primary))
                : new SceneVisualCard(Truncate(scene.Heading, 34))
        };

        foreach (var label in labels)
        {
            if (cards.Count == 4)
            {
                break;
            }

            if (label.Length is < 4 or > 24
                || string.Equals(label, primary, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            cards.Add(new SceneVisualCard(Truncate(label, 24)));
        }

        return cards;
    }

    private static IReadOnlyList<SceneVisualCard> BuildChatCards(StoryboardScene scene)
    {
        var labels = ExtractDisplayLabels(scene.Visual)
            .Where(IsDialogue)
            .Take(2)
            .Select(label => new SceneVisualCard(Truncate(label, 80)))
            .ToList();

        if (labels.Count == 0)
        {
            labels.Add(new SceneVisualCard(Truncate(scene.Heading, 40)));
        }

        return labels;
    }

    private static bool IsDialogue(string label) =>
        label.Length >= 8
        && label.Contains(' ')
        && !CommandPattern.IsMatch(label);

    private static string SceneText(StoryboardScene scene) =>
        $"{scene.Heading} {scene.Visual}";

    /// <summary>
    /// Narrow, explainable rule for the trusted <c>local_ai_laptop</c> Blender
    /// template: the scene must mention a laptop and carry a local/offline AI
    /// signal. This is the only v1 path to the 3D engine; it is not a classifier.
    /// </summary>
    private static bool IsThreeDLaptopScene(StoryboardScene scene)
    {
        var text = SceneText(scene);
        return ContainsAny(text, LaptopKeywords) && ContainsAny(text, LocalFlowKeywords);
    }

    private static bool ContainsAny(string text, string[] keywords) =>
        keywords.Any(keyword => text.Contains(keyword, StringComparison.OrdinalIgnoreCase));

    private static int FirstMatchIndex(string text, string[] keywords)
    {
        var best = -1;
        foreach (var keyword in keywords)
        {
            var index = text.IndexOf(keyword, StringComparison.OrdinalIgnoreCase);
            if (index >= 0 && (best < 0 || index < best))
            {
                best = index;
            }
        }

        return best;
    }

    private static string? DetailFor(string visual, int keywordIndex)
    {
        foreach (var label in ExtractDisplayLabels(visual))
        {
            var labelIndex = visual.IndexOf(label, keywordIndex, StringComparison.OrdinalIgnoreCase);
            if (labelIndex >= 0
                && labelIndex - keywordIndex <= 80
                && IsQuantitativeDetail(label))
            {
                return label;
            }
        }

        return null;
    }

    private static bool IsQuantitativeDetail(string label) =>
        label.Any(char.IsDigit) || ContainsAny(label, DetailCues);

    private static IReadOnlyList<string> ExtractDisplayLabels(string visual)
    {
        var labels = new List<string>();
        foreach (Match match in QuotedLabelPattern.Matches(visual))
        {
            var text = match.Groups["text"].Value.Trim();
            if (text.Length > 0 && !labels.Contains(text, StringComparer.OrdinalIgnoreCase))
            {
                labels.Add(text);
            }
        }

        return labels;
    }

    private static IReadOnlyList<string> ExtractModelNames(string visual)
    {
        var models = new List<string>();
        foreach (Match match in ModelPullPattern.Matches(visual))
        {
            var model = match.Groups["model"].Value;
            if (!models.Contains(model, StringComparer.OrdinalIgnoreCase))
            {
                models.Add(model);
            }
        }

        return models;
    }

    private static string? ExtractCommand(string visual)
    {
        var match = CommandPattern.Match(visual);
        if (!match.Success)
        {
            return null;
        }

        var command = match.Value.Trim().TrimEnd('.', ',', ';', ':');
        return command.Length is > 0 and <= 60 ? command : null;
    }

    private static string Headline(string text)
    {
        var trimmed = text.Trim();
        var stop = trimmed.IndexOfAny([',', ';', ':', '.']);
        var clause = stop > 0 ? trimmed[..stop] : trimmed;
        return Truncate(clause, 34);
    }

    private static string Truncate(string text, int maxLength)
    {
        var trimmed = text.Trim();
        if (trimmed.Length <= maxLength)
        {
            return trimmed;
        }

        var cut = trimmed[..maxLength];
        var lastSpace = cut.LastIndexOf(' ');
        if (lastSpace > 0)
        {
            cut = cut[..lastSpace];
        }

        return cut.TrimEnd(' ', ',', ';', ':', '.');
    }

    private static bool HasProgress(string visual) =>
        visual.Contains("progress", StringComparison.OrdinalIgnoreCase)
        || visual.Contains("download", StringComparison.OrdinalIgnoreCase)
        || visual.Contains("install", StringComparison.OrdinalIgnoreCase)
        || visual.Contains("unduh", StringComparison.OrdinalIgnoreCase);

    private sealed record Concept(string Title, SceneVisualIcon Icon, string[] Keywords);
}
