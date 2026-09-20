using AIStudio.Application.Jobs.GenerateStoryboard;
using AIStudio.Application.Rendering.Visuals;
using Xunit;

namespace AIStudio.Tests.Rendering;

/// <summary>
/// Deterministic visual direction + routing + choreography behavior. Fakes only;
/// no provider is invoked.
/// </summary>
public sealed class SceneVisualRoutingTests
{
    [Theory]
    [InlineData("Opening Hook", SceneVisualIntent.Opening)]
    [InlineData("Kenapa AI Lokal Itu Penting", SceneVisualIntent.TechnicalFlow)]
    [InlineData("Yang Kamu Butuhkan (Cuma Ini)", SceneVisualIntent.Requirements)]
    [InlineData("Langkah 1: Download dan Install Ollama", SceneVisualIntent.DownloadInstall)]
    [InlineData("Langkah 2: Tarik Model AI Ringan", SceneVisualIntent.ModelPull)]
    [InlineData("Langkah 3: Chat Pertama dengan AI Offline", SceneVisualIntent.Conversation)]
    [InlineData("Tips Biar Makin Nyaman", SceneVisualIntent.Tips)]
    [InlineData("Closing", SceneVisualIntent.Closing)]
    public void Director_ClassifiesCanonicalHeadings(string heading, SceneVisualIntent expected)
    {
        var scene = new StoryboardScene(heading, "Deskripsi visual.");

        Assert.Equal(expected, SceneVisualDirector.Classify(scene, 0, 8));
    }

    [Fact]
    public void Director_ResolvesPreferredEnginesForCanonicalIntents()
    {
        Assert.Equal(
            SceneVisualEngine.ThreeD,
            Direct("Opening Hook").PreferredEngine);
        Assert.Equal(
            SceneVisualEngine.ManimAnimation,
            Direct("Kenapa AI Lokal Itu Penting").PreferredEngine);
        Assert.Equal(
            SceneAnimationTemplate.ProcessFlow,
            Direct("Langkah 1: Download dan Install Ollama").ManimTemplate);
        Assert.Equal(
            SceneAnimationTemplate.ProcessFlow,
            Direct("Langkah 2: Tarik Model AI Ringan").ManimTemplate);
        Assert.Equal(
            SceneVisualEngine.AnimatedSvg,
            Direct("Langkah 3: Chat Pertama dengan AI Offline").PreferredEngine);
        Assert.Equal(
            SceneVisualEngine.AnimatedSvg,
            Direct("Tips Biar Makin Nyaman").PreferredEngine);
        Assert.Equal(
            SceneVisualEngine.AiImage,
            Direct("Closing").PreferredEngine);
    }

    [Fact]
    public void Router_IsDeterministicForIdenticalInputs()
    {
        var direction = Direct("Kenapa AI Lokal Itu Penting");

        var first = SceneVisualRouter.Select(direction, manimEnabled: true, imageEnabled: true, threeDEnabled: true);
        var second = SceneVisualRouter.Select(direction, manimEnabled: true, imageEnabled: true, threeDEnabled: true);

        Assert.Equal(first, second);
        Assert.Equal(SceneVisualEngine.ManimAnimation, first.SelectedEngine);
        Assert.False(first.IsFallback);
    }

    [Fact]
    public void Router_DegradesThreeDAlongTheDocumentedChain()
    {
        var direction = Direct("Opening Hook");

        var withThreeD = SceneVisualRouter.Select(direction, manimEnabled: true, imageEnabled: true, threeDEnabled: true);
        Assert.Equal(SceneVisualEngine.ThreeD, withThreeD.SelectedEngine);
        Assert.False(withThreeD.IsFallback);

        var manimOnly = SceneVisualRouter.Select(direction, manimEnabled: true, imageEnabled: true, threeDEnabled: false);
        Assert.Equal(SceneVisualEngine.ManimAnimation, manimOnly.SelectedEngine);
        Assert.True(manimOnly.IsFallback);

        var noGpu = SceneVisualRouter.Select(direction, manimEnabled: false, imageEnabled: false, threeDEnabled: false);
        Assert.Equal(SceneVisualEngine.AnimatedSvg, noGpu.SelectedEngine);
        Assert.True(noGpu.IsFallback);
    }

    [Fact]
    public void Router_DegradesAiImageWithoutForcingManim()
    {
        var direction = Direct("Closing");

        var enabled = SceneVisualRouter.Select(direction, manimEnabled: true, imageEnabled: true, threeDEnabled: false);
        Assert.Equal(SceneVisualEngine.AiImage, enabled.SelectedEngine);
        Assert.False(enabled.IsFallback);

        var disabled = SceneVisualRouter.Select(direction, manimEnabled: true, imageEnabled: false, threeDEnabled: false);
        Assert.Equal(SceneVisualEngine.AnimatedSvg, disabled.SelectedEngine);
        Assert.True(disabled.IsFallback);
    }

    [Fact]
    public void Router_RejectsUnsupportedChoreography()
    {
        var invalid = new SceneChoreography(
            [new AnimationBeat(0, 0.5, (AnimationPrimitive)99, "card0")]);
        Assert.False(invalid.IsValid());

        var negative = new SceneChoreography(
            [new AnimationBeat(-1, 0.5, AnimationPrimitive.FadeIn, "card0")]);
        Assert.False(negative.IsValid());

        // An invalid choreography degrades to a fully-visible static frame.
        var state = ChoreographyEvaluator.Evaluate(invalid, 1, 4);
        Assert.Equal(3, state.RevealedCount("card", 3));
        Assert.True(state.NoteVisible);
    }

    [Fact]
    public void Choreography_StaysWithinSceneDurationAndRevealsOverTime()
    {
        var brief = CardsBrief(4);
        var duration = 4.0;
        var choreography = SceneChoreographyPlanner.Build(brief, duration);

        Assert.True(choreography.IsValid());
        Assert.True(choreography.EndTime <= duration + 0.001);
        Assert.InRange(choreography.Beats.Count, 1, SceneChoreography.MaxBeats);

        var start = ChoreographyEvaluator.Evaluate(choreography, 0, duration);
        var end = ChoreographyEvaluator.Evaluate(choreography, duration, duration);

        Assert.True(end.RevealedCount("card", 4) >= start.RevealedCount("card", 4));
    }

    [Fact]
    public void Choreography_TypesCommandAndAdvancesProgress()
    {
        var brief = new SceneVisualBrief(
            3,
            "Video 1",
            "Download dan Install Ollama",
            SceneVisualLayout.Window,
            SceneVisualPalette.Slate,
            [],
            Command: "ollama pull llama3.1:8b",
            Progress: 0.75);
        var duration = 4.0;
        var choreography = SceneChoreographyPlanner.Build(brief, duration);

        var mid = ChoreographyEvaluator.Evaluate(choreography, duration * 0.6, duration);
        var end = ChoreographyEvaluator.Evaluate(choreography, duration, duration);

        Assert.True(end.Progress >= 0);
        Assert.True(mid.CommandText is null || mid.CommandText.Length <= "ollama pull llama3.1:8b".Length);
        Assert.True(end.Completion);
    }

    [Fact]
    public void ComposeFrame_RevealsCardsSequentially()
    {
        var brief = CardsBrief(3);
        var choreography = SceneChoreographyPlanner.Build(brief, 3.5);
        var early = ChoreographyEvaluator.Evaluate(choreography, 0.36, 3.5);

        var svg = SceneVisualSvg.ComposeFrame(brief, early);

        Assert.Contains(brief.Cards[0].Title, svg);
        Assert.DoesNotContain(brief.Cards[2].Title, svg);
    }

    [Fact]
    public void ComposeOverlay_StaysAboveSubtitleSafeArea()
    {
        var svg = SceneVisualSvg.ComposeOverlay(
            "AI LOKAL",
            SceneVisualPalette.Ocean,
            opacity: 1,
            scale: 1);

        Assert.True(SceneVisualSvg.SubtitleSafeTop < SceneVisualSvg.Height);
        Assert.Contains($"translate(320 {SceneVisualSvg.SubtitleSafeTop - 70})", svg);
    }

    [Fact]
    public void Director_OnlySelectsDefinedTrustedTemplates()
    {
        var headings = new[]
        {
            "Opening Hook",
            "Kenapa AI Lokal Itu Penting",
            "Yang Kamu Butuhkan (Cuma Ini)",
            "Langkah 1: Download dan Install Ollama",
            "Langkah 2: Tarik Model AI Ringan",
            "Langkah 3: Chat Pertama dengan AI Offline",
            "Tips Biar Makin Nyaman",
            "Closing"
        };

        var templates = Enum.GetValues<SceneAnimationTemplate>();
        var threeDTemplates = Enum.GetValues<SceneThreeDTemplate>();

        for (var index = 0; index < headings.Length; index++)
        {
            var direction = Direct(headings[index], index);
            Assert.Contains(direction.ManimTemplate, templates);
            Assert.Contains(direction.ThreeDTemplate, threeDTemplates);
            Assert.All(
                direction.Choreography.Beats,
                beat => Assert.True(Enum.IsDefined(beat.Primitive)));
        }
    }

    private static SceneVisualDirection Direct(string heading, int index = 0) =>
        SceneVisualDirector.Direct(
            new StoryboardScene(heading, "Deskripsi visual."),
            index,
            8,
            3.5);

    private static SceneVisualBrief CardsBrief(int count) =>
        new(
            2,
            "Video 1",
            "Yang Kamu Butuhkan",
            SceneVisualLayout.Cards,
            SceneVisualPalette.Ocean,
            Enumerable.Range(0, count)
                .Select(index => new SceneVisualCard($"Kartu {index}"))
                .ToArray());
}
