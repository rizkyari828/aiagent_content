using AIStudio.Application.Jobs.GenerateStoryboard;

namespace AIStudio.Application.Rendering.Visuals;

/// <summary>
/// Deterministic visual planner: maps the existing storyboard output
/// (<c>heading</c> + <c>visual</c>) into a structured <see cref="SceneVisualPlan"/>.
/// It derives the SVG layout/palette and, for a small subset of scenes, selects a
/// predefined animation template. It is pure and heuristic based on explicit
/// keywords; the eventual creative-director model can replace it behind the same
/// plan shape without changing the engines.
/// </summary>
public static class SceneVisualPlanner
{
    /// <summary>Bump when the mapping changes so job input hashes change.</summary>
    public const int PlannerVersion = 1;

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

    private static readonly string[] ChatFlowKeywords =
        ["chat", "percakapan", "prompt", "jawaban", "assistant"];

    private static readonly string[] ChatLayoutKeywords =
        ["chat", "percakapan", "bubble", "jawaban", "asisten"];

    private static readonly string[] WindowLayoutKeywords =
    [
        "terminal", "perintah", "install", "download", "unduh",
        "progress", "browser", "address", "ollama", "pull", "run"
    ];

    private static readonly string[] CardsLayoutKeywords =
        ["panel", "checklist", "daftar", "tiga", "opsi", "langkah", "kartu", "tips", "poin"];

    public static IReadOnlyList<SceneVisualPlan> PlanAll(
        GenerateStoryboardResult storyboard,
        bool enableAnimation)
    {
        ArgumentNullException.ThrowIfNull(storyboard);
        var plans = new List<SceneVisualPlan>(storyboard.Scenes.Count);
        var localFlowUsed = false;
        var chatFlowUsed = false;

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

            var engine = SceneVisualEngineSelector.Select(template);
            var animation = engine == SceneVisualEngine.ManimAnimation
                ? BuildAnimationParameters(brief, palette)
                : null;

            plans.Add(new SceneVisualPlan(brief, engine, template, animation));
        }

        return plans;
    }

    public static SceneVisualLayout ClassifyLayout(StoryboardScene scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        var text = SceneText(scene);

        if (ContainsAny(text, ChatLayoutKeywords))
        {
            return SceneVisualLayout.Chat;
        }

        if (ContainsAny(text, WindowLayoutKeywords))
        {
            return SceneVisualLayout.Window;
        }

        if (ContainsAny(text, CardsLayoutKeywords))
        {
            return SceneVisualLayout.Cards;
        }

        return SceneVisualLayout.Hero;
    }

    private static SceneVisualBrief BuildBrief(
        StoryboardScene scene,
        int index,
        SceneVisualLayout layout,
        SceneVisualPalette palette)
    {
        var cards = BuildCards(scene);
        var command = layout == SceneVisualLayout.Window
            ? ExtractCommand(scene.Visual)
            : null;
        var progress = layout == SceneVisualLayout.Window && HasProgress(scene.Visual)
            ? 0.72
            : 0;

        return new SceneVisualBrief(
            index,
            "Video 1",
            scene.Heading,
            layout,
            palette,
            cards,
            Note: null,
            Command: command,
            Progress: progress);
    }

    private static IReadOnlyList<SceneVisualCard> BuildCards(StoryboardScene scene)
    {
        var cards = new List<SceneVisualCard>();
        foreach (var segment in SplitSegments(scene.Visual))
        {
            if (cards.Count == 4)
            {
                break;
            }

            var title = Shorten(segment);
            if (title.Length > 0)
            {
                cards.Add(new SceneVisualCard(title, null, IconFor(segment)));
            }
        }

        if (cards.Count == 0)
        {
            cards.Add(new SceneVisualCard(Shorten(scene.Heading)));
        }

        return cards;
    }

    private static SceneAnimationParameters BuildAnimationParameters(
        SceneVisualBrief brief,
        SceneVisualPalette palette) =>
        new(
            brief.Kicker,
            brief.Heading,
            brief.Cards.Count > 0 ? brief.Cards[0].Title : null,
            brief.Cards.Count > 1 ? brief.Cards[1].Title : null,
            palette,
            AnimationDurationSeconds);

    private static string SceneText(StoryboardScene scene) =>
        $"{scene.Heading} {scene.Visual}";

    private static bool ContainsAny(string text, string[] keywords) =>
        keywords.Any(keyword => text.Contains(keyword, StringComparison.OrdinalIgnoreCase));

    private static IReadOnlyList<string> SplitSegments(string visual) =>
        visual
            .Split(
                ['.', ';', '\n'],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(segment => segment.Length >= 3)
            .ToArray();

    private static string Shorten(string text)
    {
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var trimmed = words.Length > 5 ? string.Join(' ', words[..5]) : string.Join(' ', words);
        return trimmed.Length > 34 ? trimmed[..34].TrimEnd() : trimmed;
    }

    private static SceneVisualIcon IconFor(string text)
    {
        var value = text.ToLowerInvariant();
        if (value.Contains("laptop", StringComparison.Ordinal))
        {
            return SceneVisualIcon.Laptop;
        }

        if (value.Contains("cloud", StringComparison.Ordinal)
            || value.Contains("server", StringComparison.Ordinal)
            || value.Contains("awan", StringComparison.Ordinal))
        {
            return SceneVisualIcon.Cloud;
        }

        if (value.Contains("gembok", StringComparison.Ordinal)
            || value.Contains("privasi", StringComparison.Ordinal)
            || value.Contains("aman", StringComparison.Ordinal))
        {
            return SceneVisualIcon.Shield;
        }

        if (value.Contains("chat", StringComparison.Ordinal)
            || value.Contains("percakapan", StringComparison.Ordinal))
        {
            return SceneVisualIcon.Chat;
        }

        if (value.Contains("download", StringComparison.Ordinal)
            || value.Contains("unduh", StringComparison.Ordinal)
            || value.Contains("install", StringComparison.Ordinal))
        {
            return SceneVisualIcon.Download;
        }

        if (value.Contains("model", StringComparison.Ordinal))
        {
            return SceneVisualIcon.Model;
        }

        if (value.Contains("centang", StringComparison.Ordinal)
            || value.Contains("check", StringComparison.Ordinal))
        {
            return SceneVisualIcon.Check;
        }

        if (value.Contains("gpu", StringComparison.Ordinal)
            || value.Contains("suhu", StringComparison.Ordinal))
        {
            return SceneVisualIcon.Bolt;
        }

        if (value.Contains("disk", StringComparison.Ordinal)
            || value.Contains("gb", StringComparison.Ordinal)
            || value.Contains("penyimpanan", StringComparison.Ordinal))
        {
            return SceneVisualIcon.Disk;
        }

        if (value.Contains("tips", StringComparison.Ordinal)
            || value.Contains("ide", StringComparison.Ordinal))
        {
            return SceneVisualIcon.Bulb;
        }

        return SceneVisualIcon.None;
    }

    private static string? ExtractCommand(string visual)
    {
        var index = visual.IndexOf("ollama", StringComparison.OrdinalIgnoreCase);
        if (index < 0)
        {
            return null;
        }

        var snippet = visual[index..];
        var stop = snippet.IndexOfAny(['\'', '"', '\n']);
        if (stop > 0)
        {
            snippet = snippet[..stop];
        }

        var tokens = snippet.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length > 4)
        {
            tokens = tokens[..4];
        }

        var command = string.Join(' ', tokens).Trim();
        return command.Length is > 0 and <= 60 ? command : null;
    }

    private static bool HasProgress(string visual) =>
        visual.Contains("progress", StringComparison.OrdinalIgnoreCase)
        || visual.Contains("download", StringComparison.OrdinalIgnoreCase)
        || visual.Contains("install", StringComparison.OrdinalIgnoreCase)
        || visual.Contains("unduh", StringComparison.OrdinalIgnoreCase);
}
