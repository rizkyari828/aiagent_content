using AIStudio.Application.Creative;
using Xunit;

namespace AIStudio.Tests.Creative;

public sealed class CreativeDirectorPromptTests
{
    [Fact]
    public void PromptContainsTheApprovedIdea()
    {
        var prompt = Build();

        Assert.Contains("Run AI locally", prompt);
        Assert.Contains("Stop paying API fees", prompt);
        Assert.Contains("developers", prompt);
        Assert.Contains("educational", prompt);
        Assert.Contains("Your existing PC may already be enough", prompt);
    }

    [Fact]
    public void PromptForbidsInventingANewIdea()
    {
        var prompt = Build();

        Assert.Contains("Do not invent a new topic", prompt);
        Assert.Contains("never change WHAT the idea is", prompt);
    }

    [Fact]
    public void PromptContainsTheSafeRecipeCatalog()
    {
        var prompt = Build();

        Assert.Contains("tech-explainer", prompt);
        Assert.Contains("motion-comic", prompt);
        Assert.Contains("visual.diagram", prompt);
        Assert.Contains("requires ", prompt);
        Assert.Contains("resolvable=", prompt);
    }

    [Fact]
    public void PromptNamesTheRecipeIdTokenSeparatelyFromItsVersion()
    {
        // Real Qwen copied the catalog line "tech-explainer v1" into concept.recipeId.
        // The catalog must expose the exact id token and its version as separate fields.
        var prompt = Build();

        Assert.Contains("recipeId=tech-explainer", prompt);
        Assert.Contains("recipeId=motion-comic", prompt);
        Assert.Contains("recipeVersion=1", prompt);
    }

    [Fact]
    public void PromptRequiresAnExactRecipeIdTokenAndForbidsExtraJson()
    {
        var prompt = Build();

        Assert.Contains("copied exactly from a recipeId above", prompt);
        Assert.Contains("no version suffix and no spaces", prompt);
        Assert.Contains("do not add extra properties", prompt);
        Assert.Contains("do not use markdown fences", prompt);
    }

    [Fact]
    public void PromptExcludesProviderImplementationDetails()
    {
        var prompt = Build();

        foreach (var providerId in new[]
        {
            "voxcpm2", "ace-step", "comfyui", "blender", "manim",
            "animated-svg", "svg-still", "ffmpeg-compose", "ffmpeg-subtitle-burn"
        })
        {
            Assert.DoesNotContain(providerId, prompt, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void PromptContainsTheJsonContractAndCapabilityAwareness()
    {
        var prompt = Build();

        Assert.Contains("\"concept\"", prompt);
        Assert.Contains("\"treatment\"", prompt);
        Assert.Contains("Available:", prompt);
        Assert.Contains("Unavailable:", prompt);
    }

    [Fact]
    public void PromptIsDeterministic()
    {
        Assert.Equal(Build(), Build());
    }

    private static string Build() =>
        CreativeDirectorPrompt.Build(
            CreativeTestSupport.Idea(),
            CreativeTestSupport.PlanningContext().Build(),
            new CreativeDirectionOptions());
}
