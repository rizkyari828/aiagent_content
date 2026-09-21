using AIStudio.Application.Creative;
using Xunit;

namespace AIStudio.Tests.Creative;

public sealed class CreativePlanningContextTests
{
    [Fact]
    public void SummarizesTheRecipeCatalogSafely()
    {
        var context = CreativeTestSupport.PlanningContext().Build();

        var techExplainer = context.Recipes.Single(recipe => recipe.Id.Value == "tech-explainer");
        Assert.Equal("Tech explainer", techExplainer.DisplayName);
        Assert.Contains("visual.diagram", techExplainer.Capabilities);
        Assert.True(techExplainer.Resolvable);
        Assert.False(techExplainer.UsesFallbacks);
    }

    [Fact]
    public void ReportsFallbacksAndUnresolvableRecipesHonestly()
    {
        var fallbackContext = CreativeTestSupport.PlanningContext(manimEnabled: false).Build();
        var techExplainer = fallbackContext.Recipes.Single(recipe => recipe.Id.Value == "tech-explainer");
        Assert.True(techExplainer.Resolvable);
        Assert.True(techExplainer.UsesFallbacks);

        var unsupportedContext = CreativeTestSupport.PlanningContext(musicEnabled: false).Build();
        var unsupported = unsupportedContext.Recipes.Single(recipe => recipe.Id.Value == "tech-explainer");
        Assert.False(unsupported.Resolvable);
    }

    [Fact]
    public void ReportsAvailableAndUnavailableCapabilities()
    {
        var context = CreativeTestSupport.PlanningContext(speechEnabled: false).Build();

        Assert.Contains("visual.still", context.AvailableCapabilities);
        Assert.Contains("speech.narration", context.UnavailableCapabilities);
    }

    [Fact]
    public void ContextNeverCarriesProviderIds()
    {
        var context = CreativeTestSupport.PlanningContext().Build();
        var text = string.Join(
            "|",
            context.Recipes.Select(recipe =>
                $"{recipe.Id.Value}:{string.Join(",", recipe.Capabilities)}:{recipe.DisplayName}")
                .Concat(context.AvailableCapabilities)
                .Concat(context.UnavailableCapabilities));

        Assert.DoesNotContain("voxcpm", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("blender", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ffmpeg", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("comfyui", text, StringComparison.OrdinalIgnoreCase);
    }
}
