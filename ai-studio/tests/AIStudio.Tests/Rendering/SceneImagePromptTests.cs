using AIStudio.Application.Rendering.Visuals;
using Xunit;

namespace AIStudio.Tests.Rendering;

public sealed class SceneImagePromptTests
{
    [Fact]
    public void Build_UsesHeadingAndCardsAndSuppressesRenderedText()
    {
        var brief = new SceneVisualBrief(
            0,
            "Video 1",
            "Local AI flow",
            SceneVisualLayout.Hero,
            SceneVisualPalette.Ocean,
            [new SceneVisualCard("Laptop"), new SceneVisualCard("GPU")],
            Note: "should not leak");

        var prompt = SceneImagePrompt.Build(brief);

        Assert.Contains("Local AI flow", prompt);
        Assert.Contains("Laptop", prompt);
        Assert.Contains("GPU", prompt);
        Assert.Contains("no text", prompt);
        Assert.DoesNotContain("should not leak", prompt);
        Assert.DoesNotContain('\n', prompt);
    }
}
