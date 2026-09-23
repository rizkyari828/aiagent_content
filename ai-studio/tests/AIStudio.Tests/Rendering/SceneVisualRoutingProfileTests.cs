using AIStudio.Application.Capabilities;
using AIStudio.Application.Jobs.GenerateStoryboard;
using AIStudio.Application.ProductionRecipes;
using AIStudio.Application.Rendering.Visuals;
using Xunit;

namespace AIStudio.Tests.Rendering;

/// <summary>
/// Recipe-derived visual routing profile behavior. Fakes only; no provider runs.
/// </summary>
public sealed class SceneVisualRoutingProfileTests
{
    [Fact]
    public void ProfileFromRecipe_MotionComicPrefersAiImageForNarrative()
    {
        var recipe = Recipe("motion-comic", CapabilityIds.VisualAiImage, CapabilityIds.VisualStill);

        var profile = SceneVisualRoutingProfile.FromRecipe(recipe);

        Assert.Equal(SceneVisualEngine.AiImage, profile.NarrativeEngine);
    }

    [Fact]
    public void ProfileFromRecipe_TechExplainerKeepsDefault()
    {
        var recipe = Recipe(
            "tech-explainer",
            CapabilityIds.VisualDiagram,
            CapabilityIds.VisualUiMotion,
            CapabilityIds.VisualStill);

        var profile = SceneVisualRoutingProfile.FromRecipe(recipe);

        Assert.Equal(SceneVisualEngine.AnimatedSvg, profile.NarrativeEngine);
    }

    [Fact]
    public void ProfileFromRecipe_ReadsCapabilityNotRecipeId()
    {
        // A brand-new format id with the same AI-image capability gets the same
        // narrative preference: routing derives from capability data, never a
        // hardcoded id or a text keyword.
        var recipe = Recipe("some-future-format", CapabilityIds.VisualAiImage, CapabilityIds.VisualStill);

        var profile = SceneVisualRoutingProfile.FromRecipe(recipe);

        Assert.Equal(SceneVisualEngine.AiImage, profile.NarrativeEngine);
    }

    [Fact]
    public void Profile_ExposesNoInfrastructureTypes()
    {
        var propertyTypes = typeof(SceneVisualRoutingProfile)
            .GetProperties()
            .Select(property => property.PropertyType);

        Assert.All(
            propertyTypes,
            type => Assert.DoesNotContain(
                "AIStudio.Infrastructure",
                type.FullName ?? string.Empty,
                StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("Student enters a quiet study room", SceneVisualIntent.Generic)]
    [InlineData("Student notices a mysterious interface", SceneVisualIntent.Generic)]
    [InlineData("A shadowy figure appears behind the student", SceneVisualIntent.Generic)]
    [InlineData("Close emotional reaction and payoff", SceneVisualIntent.Generic)]
    public void Director_NarrativeFixtureScenesClassifyAsGeneric(
        string heading,
        SceneVisualIntent expected)
    {
        var scene = new StoryboardScene(heading, "A calm moment with no technical terms.");

        Assert.Equal(expected, SceneVisualDirector.Classify(scene, 0, 4));
    }

    [Fact]
    public void Director_GenericNarrativePrefersAiImageWithProfile()
    {
        var direction = Direct("Student enters a quiet study room", MotionComicProfile());

        Assert.Equal(SceneVisualIntent.Generic, direction.Intent);
        Assert.Equal(SceneVisualEngine.AiImage, direction.PreferredEngine);
        Assert.Equal(SceneVisualEngine.AnimatedSvg, direction.FallbackEngine);
    }

    [Fact]
    public void Director_GenericNarrativeUnchangedWithoutProfile()
    {
        var direction = Direct("Student enters a quiet study room");

        Assert.Equal(SceneVisualEngine.AnimatedSvg, direction.PreferredEngine);
        Assert.Equal(SceneVisualEngine.SvgStill, direction.FallbackEngine);
    }

    [Theory]
    [InlineData("Langkah 1: Download dan Install Ollama", SceneVisualEngine.ManimAnimation)]
    [InlineData("Langkah 2: Tarik Model AI Ringan", SceneVisualEngine.ManimAnimation)]
    [InlineData("Yang Kamu Butuhkan (Cuma Ini)", SceneVisualEngine.AnimatedSvg)]
    [InlineData("Tips Biar Makin Nyaman", SceneVisualEngine.AnimatedSvg)]
    [InlineData("Langkah 3: Chat Pertama dengan AI Offline", SceneVisualEngine.AnimatedSvg)]
    public void Director_TechnicalAndUiIntentsIgnoreNarrativeProfile(
        string heading,
        SceneVisualEngine expected)
    {
        var direction = Direct(heading, MotionComicProfile());

        Assert.Equal(expected, direction.PreferredEngine);
    }

    [Fact]
    public void Director_OpeningKeepsThreeDOverride()
    {
        var direction = Direct("Opening Hook", MotionComicProfile());

        Assert.Equal(SceneVisualIntent.Opening, direction.Intent);
        Assert.Equal(SceneVisualEngine.ThreeD, direction.PreferredEngine);
    }

    [Fact]
    public void Planner_MotionComicGenericSelectsAiImageWhenEnabled()
    {
        var storyboard = NarrativeStoryboard();
        var durations = Enumerable.Repeat(3.5, storyboard.Scenes.Count).ToArray();

        var plans = SceneVisualPlanner.PlanAll(
            storyboard,
            durations,
            enableAnimation: true,
            enableAiImages: true,
            routingProfile: MotionComicProfile());

        Assert.All(plans, plan => Assert.Equal(SceneVisualEngine.AiImage, plan.Engine));
        Assert.All(plans, plan => Assert.Equal(SceneVisualEngine.AiImage, plan.IntendedEngine));
        Assert.All(plans, plan => Assert.False(plan.IsFallback));
    }

    [Fact]
    public void Planner_MotionComicGenericFallsBackWhenAiImageDisabled()
    {
        var storyboard = NarrativeStoryboard();
        var durations = Enumerable.Repeat(3.5, storyboard.Scenes.Count).ToArray();

        var plans = SceneVisualPlanner.PlanAll(
            storyboard,
            durations,
            enableAnimation: false,
            enableAiImages: false,
            routingProfile: MotionComicProfile());

        Assert.All(plans, plan => Assert.Equal(SceneVisualEngine.AnimatedSvg, plan.Engine));
        Assert.All(plans, plan => Assert.Equal(SceneVisualEngine.AiImage, plan.IntendedEngine));
        Assert.All(plans, plan => Assert.True(plan.IsFallback));
    }

    [Fact]
    public void Planner_GenericUnchangedWithoutProfile()
    {
        var storyboard = NarrativeStoryboard();
        var durations = Enumerable.Repeat(3.5, storyboard.Scenes.Count).ToArray();

        var plans = SceneVisualPlanner.PlanAll(
            storyboard,
            durations,
            enableAnimation: false,
            enableAiImages: true);

        Assert.All(plans, plan => Assert.Equal(SceneVisualEngine.AnimatedSvg, plan.Engine));
    }

    private static SceneVisualRoutingProfile MotionComicProfile() =>
        SceneVisualRoutingProfile.FromRecipe(
            Recipe("motion-comic", CapabilityIds.VisualAiImage, CapabilityIds.VisualStill));

    private static ProductionRecipe Recipe(
        string id,
        CapabilityId visual,
        params CapabilityId[] visualFallbacks) =>
        new()
        {
            Id = new ProductionRecipeId(id),
            Version = new ProductionRecipeVersion(1),
            DisplayName = id,
            Requirements =
            [
                ProductionRecipeRequirement.Required(CapabilityIds.SpeechNarration),
                ProductionRecipeRequirement.Required(visual, visualFallbacks),
                ProductionRecipeRequirement.Required(CapabilityIds.MusicInstrumental),
                ProductionRecipeRequirement.Required(CapabilityIds.MediaCompose),
                ProductionRecipeRequirement.Required(CapabilityIds.SubtitleBurned)
            ]
        };

    private static SceneVisualDirection Direct(
        string heading,
        SceneVisualRoutingProfile? profile = null) =>
        SceneVisualDirector.Direct(
            new StoryboardScene(heading, "Deskripsi visual."),
            0,
            1,
            3.5,
            profile);

    private static GenerateStoryboardResult NarrativeStoryboard() =>
        GenerateStoryboardResult.Deserialize(GenerateSceneVisualsTestData.NarrativeStoryboard);
}
