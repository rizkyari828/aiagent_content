using AIStudio.Application.Jobs.GenerateStoryboard;

namespace AIStudio.Application.Rendering.Visuals;

/// <summary>
/// Deterministic visual director. It classifies each storyboard scene into a
/// <see cref="SceneVisualIntent"/> from the canonical heading and direction text and
/// resolves the preferred engine, fallback, composition and choreography. It is
/// pure and keyword driven; no model call and no executable content. The eventual
/// creative-director model can replace it behind the same direction shape.
/// </summary>
public static class SceneVisualDirector
{
    private static readonly string[] ClosingKeywords = ["closing", "penutup", "kesimpulan"];
    private static readonly string[] OpeningKeywords = ["opening", "hook", "pembuka"];
    private static readonly string[] DownloadKeywords = ["download", "install", "unduh", "pasang"];
    private static readonly string[] PullKeywords = ["tarik", "pull"];
    private static readonly string[] ChatKeywords = ["chat", "percakapan", "obrol"];
    private static readonly string[] TipsKeywords = ["tips"];
    private static readonly string[] RequirementsKeywords =
        ["butuh", "kebutuhan", "checklist", "langkah", "syarat", "persiapan", "opsi"];
    private static readonly string[] FlowKeywords =
        ["kenapa", "mengapa", "penting", "alur", "flow", "data", "cara kerja"];

    public static IReadOnlyList<SceneVisualDirection> DirectAll(
        GenerateStoryboardResult storyboard,
        IReadOnlyList<double> durations)
    {
        ArgumentNullException.ThrowIfNull(storyboard);
        ArgumentNullException.ThrowIfNull(durations);

        if (durations.Count != storyboard.Scenes.Count)
        {
            throw new ArgumentException(
                "Durations must match the storyboard scene count.",
                nameof(durations));
        }

        var directions = new List<SceneVisualDirection>(storyboard.Scenes.Count);
        for (var index = 0; index < storyboard.Scenes.Count; index++)
        {
            directions.Add(Direct(
                storyboard.Scenes[index],
                index,
                storyboard.Scenes.Count,
                durations[index]));
        }

        return directions;
    }

    public static SceneVisualDirection Direct(
        StoryboardScene scene,
        int index,
        int sceneCount,
        double durationSeconds)
    {
        ArgumentNullException.ThrowIfNull(scene);
        var intent = Classify(scene, index, sceneCount);
        var composition = CompositionFor(intent);
        var (preferred, fallback, manim, threeD, required, motion) = Resolve(intent);

        return new SceneVisualDirection(
            index,
            intent,
            composition,
            preferred,
            fallback,
            manim,
            threeD,
            SceneChoreography.Empty,
            required,
            motion,
            durationSeconds);
    }

    public static SceneVisualIntent Classify(
        StoryboardScene scene,
        int index,
        int sceneCount)
    {
        var heading = scene.Heading;
        var text = $"{scene.Heading} {scene.Visual}";

        if (ContainsAny(heading, ClosingKeywords))
        {
            return SceneVisualIntent.Closing;
        }

        if (ContainsAny(heading, OpeningKeywords))
        {
            return SceneVisualIntent.Opening;
        }

        if (ContainsAny(heading, DownloadKeywords))
        {
            return SceneVisualIntent.DownloadInstall;
        }

        if (ContainsAny(heading, PullKeywords)
            || (ContainsAny(heading, ["model"]) && ContainsAny(scene.Visual, PullKeywords)))
        {
            return SceneVisualIntent.ModelPull;
        }

        if (ContainsAny(heading, ChatKeywords))
        {
            return SceneVisualIntent.Conversation;
        }

        if (ContainsAny(heading, TipsKeywords))
        {
            return SceneVisualIntent.Tips;
        }

        if (ContainsAny(heading, RequirementsKeywords))
        {
            return SceneVisualIntent.Requirements;
        }

        if (ContainsAny(text, FlowKeywords))
        {
            return SceneVisualIntent.TechnicalFlow;
        }

        return SceneVisualIntent.Generic;
    }

    public static SceneVisualComposition CompositionFor(SceneVisualIntent intent) =>
        intent switch
        {
            SceneVisualIntent.Opening => SceneVisualComposition.Device3D,
            SceneVisualIntent.TechnicalFlow => SceneVisualComposition.DataFlow,
            SceneVisualIntent.DownloadInstall => SceneVisualComposition.Terminal,
            SceneVisualIntent.ModelPull => SceneVisualComposition.Terminal,
            SceneVisualIntent.Conversation => SceneVisualComposition.Chat,
            SceneVisualIntent.Requirements => SceneVisualComposition.Cards,
            SceneVisualIntent.Tips => SceneVisualComposition.Cards,
            SceneVisualIntent.Closing => SceneVisualComposition.Illustration,
            _ => SceneVisualComposition.Hero
        };

    private static (
        SceneVisualEngine Preferred,
        SceneVisualEngine Fallback,
        SceneAnimationTemplate Manim,
        SceneThreeDTemplate ThreeD,
        IReadOnlyList<string> Required,
        string MotionStyle) Resolve(SceneVisualIntent intent) =>
        intent switch
        {
            SceneVisualIntent.Opening => (
                SceneVisualEngine.ThreeD,
                SceneVisualEngine.ManimAnimation,
                SceneAnimationTemplate.LocalAiFlow,
                SceneThreeDTemplate.LocalAiLaptop,
                ["laptop", "local-compute-indicator", "camera-movement"],
                "camera_push"),

            SceneVisualIntent.TechnicalFlow => (
                SceneVisualEngine.ManimAnimation,
                SceneVisualEngine.AnimatedSvg,
                SceneAnimationTemplate.LocalAiFlow,
                SceneThreeDTemplate.None,
                ["user", "computer", "cloud", "data-path"],
                "data_flow"),

            SceneVisualIntent.DownloadInstall => (
                SceneVisualEngine.ManimAnimation,
                SceneVisualEngine.AnimatedSvg,
                SceneAnimationTemplate.ProcessFlow,
                SceneThreeDTemplate.None,
                ["download", "progress", "terminal", "completion"],
                "process"),

            SceneVisualIntent.ModelPull => (
                SceneVisualEngine.ManimAnimation,
                SceneVisualEngine.AnimatedSvg,
                SceneAnimationTemplate.ProcessFlow,
                SceneThreeDTemplate.None,
                ["command", "progress", "model", "completion"],
                "progress"),

            SceneVisualIntent.Conversation => (
                SceneVisualEngine.AnimatedSvg,
                SceneVisualEngine.SvgStill,
                SceneAnimationTemplate.None,
                SceneThreeDTemplate.None,
                ["user-bubble", "typing", "reply"],
                "chat_reveal"),

            SceneVisualIntent.Requirements or SceneVisualIntent.Tips => (
                SceneVisualEngine.AnimatedSvg,
                SceneVisualEngine.SvgStill,
                SceneAnimationTemplate.None,
                SceneThreeDTemplate.None,
                ["cards", "sequential-reveal"],
                "sequential_reveal"),

            SceneVisualIntent.Closing => (
                SceneVisualEngine.AiImage,
                SceneVisualEngine.AnimatedSvg,
                SceneAnimationTemplate.None,
                SceneThreeDTemplate.None,
                ["hero-illustration", "local-ai-badge", "overlay-motion"],
                "parallax_push"),

            _ => (
                SceneVisualEngine.AnimatedSvg,
                SceneVisualEngine.SvgStill,
                SceneAnimationTemplate.None,
                SceneThreeDTemplate.None,
                ["cards"],
                "sequential_reveal")
        };

    private static bool ContainsAny(string text, string[] keywords) =>
        keywords.Any(keyword => text.Contains(keyword, StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// Builds bounded Animated-SVG choreography from the composed brief and the actual
/// scene duration: a small number of reveals that fit the scene, with a readable
/// tail hold. Beats never exceed the scene; content settles before the transition.
/// </summary>
public static class SceneChoreographyPlanner
{
    private const double DefaultBeatSeconds = 0.42;
    private const double DefaultLeadInSeconds = 0.35;
    private const double DefaultTailHoldSeconds = 0.55;

    public static SceneChoreography Build(
        SceneVisualBrief brief,
        double durationSeconds,
        SceneNarrationWindow? narrationWindow = null)
    {
        ArgumentNullException.ThrowIfNull(brief);
        if (durationSeconds <= 0)
        {
            return SceneChoreography.Empty;
        }

        var (start, span) = Schedule(narrationWindow, durationSeconds);
        var beats = brief.Layout switch
        {
            SceneVisualLayout.Cards => Cards(brief, start, span),
            SceneVisualLayout.Chat => Chat(start, span),
            SceneVisualLayout.Window => Window(brief, start, span),
            _ => Hero(brief, start, span)
        };

        return new SceneChoreography(Trim(beats, durationSeconds));
    }

    /// <summary>
    /// The narration window drives beat timing: motion is scheduled relative to the
    /// speech, not the whole scene. Without a window it degrades to the previous
    /// scene-relative defaults.
    /// </summary>
    private static (double Start, double Span) Schedule(
        SceneNarrationWindow? window,
        double duration)
    {
        if (window is not null && window.NarrationDurationSeconds > 0)
        {
            return (
                Math.Max(0, window.NarrationStartWithinScene),
                window.NarrationDurationSeconds);
        }

        return (DefaultLeadInSeconds, Math.Max(DefaultBeatSeconds, duration - 0.9));
    }

    private static List<AnimationBeat> Cards(SceneVisualBrief brief, double start, double span)
    {
        var beats = new List<AnimationBeat>();
        var count = Math.Clamp(brief.Cards.Count, 1, 4);
        var step = (span * 0.9) / count;

        for (var index = 0; index < count; index++)
        {
            beats.Add(new AnimationBeat(
                start + index * step,
                Math.Min(DefaultBeatSeconds, Math.Max(0.25, step)),
                index == 0 ? AnimationPrimitive.FadeIn : AnimationPrimitive.SlideIn,
                $"card{index}"));
        }

        beats.Add(new AnimationBeat(
            start + (span * 0.8),
            0.5,
            AnimationPrimitive.ScalePulse,
            $"card{count - 1}"));

        if (!string.IsNullOrWhiteSpace(brief.Note))
        {
            beats.Add(new AnimationBeat(start, 0.5, AnimationPrimitive.FadeIn, "note"));
        }

        return beats;
    }

    private static List<AnimationBeat> Chat(double start, double span)
    {
        return
        [
            new(start, DefaultBeatSeconds, AnimationPrimitive.SlideIn, "bubble0"),
            new(start + (span * 0.3), 0.7, AnimationPrimitive.ScalePulse, "typing"),
            new(start + (span * 0.55), 0.5, AnimationPrimitive.FadeIn, "bubble1")
        ];
    }

    private static List<AnimationBeat> Window(SceneVisualBrief brief, double start, double span)
    {
        var beats = new List<AnimationBeat>();

        if (!string.IsNullOrWhiteSpace(brief.Command))
        {
            beats.Add(new AnimationBeat(
                start,
                Math.Min(0.9, Math.Max(0.4, span * 0.35)),
                AnimationPrimitive.TypeText,
                "command",
                brief.Command));
        }

        if (brief.Progress > 0)
        {
            beats.Add(new AnimationBeat(
                start + (span * 0.35),
                Math.Min(1.2, Math.Max(0.5, span * 0.4)),
                AnimationPrimitive.Progress,
                "progress"));
        }

        beats.Add(new AnimationBeat(
            start + (span * 0.85),
            0.45,
            AnimationPrimitive.Checkmark,
            "check"));

        if (!string.IsNullOrWhiteSpace(brief.Note))
        {
            beats.Add(new AnimationBeat(start, 0.5, AnimationPrimitive.FadeIn, "note"));
        }

        return beats;
    }

    private static List<AnimationBeat> Hero(SceneVisualBrief brief, double start, double span)
    {
        var beats = new List<AnimationBeat>
        {
            new(start, 0.5, AnimationPrimitive.FadeIn, "card0")
        };

        if (brief.Cards.Count > 1)
        {
            beats.Add(new AnimationBeat(
                start + (span * 0.25),
                0.5,
                AnimationPrimitive.SlideIn,
                "chip0"));
        }

        beats.Add(new AnimationBeat(
            start + (span * 0.6),
            0.6,
            AnimationPrimitive.ScalePulse,
            "card0"));
        return beats;
    }

    private static List<AnimationBeat> Trim(List<AnimationBeat> beats, double duration)
    {
        var bounded = beats
            .Where(beat => beat.StartTime < duration)
            .Take(SceneChoreography.MaxBeats)
            .ToArray();

        return [.. bounded];
    }
}
