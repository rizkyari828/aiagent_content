using AIStudio.Application.Jobs.GenerateStoryboard;
using AIStudio.Application.Rendering.Visuals;
using Xunit;

namespace AIStudio.Tests.Rendering;

public sealed class SceneVisualPlannerTests
{
    [Theory]
    [InlineData("Chat pertama", "Bubble percakapan dengan jawaban AI", SceneVisualLayout.Chat)]
    [InlineData("Install Ollama", "Terminal browser dan progress download", SceneVisualLayout.Window)]
    [InlineData("Yang dibutuhkan", "Checklist tiga panel berisi daftar", SceneVisualLayout.Cards)]
    [InlineData("Opening", "Laptop terbuka dengan layar menyala", SceneVisualLayout.Hero)]
    public void ClassifyLayout_UsesExplicitKeywords(
        string heading,
        string visual,
        SceneVisualLayout expected)
    {
        var layout = SceneVisualPlanner.ClassifyLayout(new StoryboardScene(heading, visual));

        Assert.Equal(expected, layout);
    }

    [Fact]
    public void PlanAll_SelectsAtMostOneAnimationScenePerTemplate()
    {
        var storyboard = GenerateStoryboardResult.Deserialize(
            GenerateSceneVisualsTestData.AnimatedStoryboard);

        var plans = SceneVisualPlanner.PlanAll(storyboard, enableAnimation: true);

        Assert.Equal(SceneAnimationTemplate.LocalAiFlow, plans[0].Template);
        Assert.Equal(SceneAnimationTemplate.ChatFlow, plans[1].Template);
        Assert.All(plans, plan => Assert.Equal(SceneVisualEngine.ManimAnimation, plan.Engine));
    }

    [Fact]
    public void PlanAll_KeepsSvgStillWhenAnimationIsDisabled()
    {
        var storyboard = GenerateStoryboardResult.Deserialize(
            GenerateSceneVisualsTestData.AnimatedStoryboard);

        var plans = SceneVisualPlanner.PlanAll(storyboard, enableAnimation: false);

        Assert.All(plans, plan =>
        {
            Assert.Equal(SceneVisualEngine.SvgStill, plan.Engine);
            Assert.Equal(SceneAnimationTemplate.None, plan.Template);
            Assert.Null(plan.Animation);
        });
    }

    [Fact]
    public void PlanAll_PopulatesAnimationParametersForAnimatedScenes()
    {
        var storyboard = GenerateStoryboardResult.Deserialize(
            GenerateSceneVisualsTestData.AnimatedStoryboard);

        var plan = Assert.Single(
            SceneVisualPlanner.PlanAll(storyboard, enableAnimation: true),
            candidate => candidate.Brief.SceneIndex == 0);

        Assert.NotNull(plan.Animation);
        Assert.False(string.IsNullOrWhiteSpace(plan.Animation!.PrimaryText));
        Assert.False(string.IsNullOrWhiteSpace(plan.Animation.Kicker));
        Assert.Equal(SceneVisualPlanner.AnimationDurationSeconds, plan.Animation.DurationSeconds);
        Assert.Contains(plan.Animation.Palette, Enum.GetValues<SceneVisualPalette>());
    }

    [Fact]
    public void PlanAll_CyclesPalettesAndKeepsSceneIndexes()
    {
        var storyboard = GenerateStoryboardResult.Deserialize(
            GenerateSceneVisualsTestData.AnimatedStoryboard);

        var plans = SceneVisualPlanner.PlanAll(storyboard, enableAnimation: false);

        Assert.Equal([0, 1], plans.Select(plan => plan.Brief.SceneIndex).ToArray());
        Assert.NotEqual(plans[0].Brief.Palette, plans[1].Brief.Palette);
    }

    [Theory]
    [InlineData(SceneAnimationTemplate.None, SceneVisualEngine.SvgStill)]
    [InlineData(SceneAnimationTemplate.LocalAiFlow, SceneVisualEngine.ManimAnimation)]
    [InlineData(SceneAnimationTemplate.ChatFlow, SceneVisualEngine.ManimAnimation)]
    public void Select_MapsTemplateToEngine(
        SceneAnimationTemplate template,
        SceneVisualEngine expected)
    {
        Assert.Equal(expected, SceneVisualEngineSelector.Select(template));
    }
}
