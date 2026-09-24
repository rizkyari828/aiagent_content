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

    [Fact]
    public void Build_NarrativeUsesStoryboardVisualDescription()
    {
        const string visual =
            "Wide static shot of a dim study room at night. A single desk lamp casts "
            + "a warm cone of light over a cluttered wooden desk.";
        var brief = new SceneVisualBrief(
            0,
            "Video 1",
            "Jam Dua Pagi",
            SceneVisualLayout.Hero,
            SceneVisualPalette.Ocean,
            [new SceneVisualCard("Laptop")],
            VisualDescription: visual);

        var prompt = SceneImagePrompt.Build(brief, narrative: true);

        // The storyboard cinematic description must be the scene content.
        Assert.Contains("Wide static shot of a dim study room at night", prompt);
        // SVG/card labels must never substitute for narrative content.
        Assert.DoesNotContain("Laptop", prompt);
        // The technical explainer style must not leak into a narrative prompt.
        Assert.DoesNotContain("technology explainer", prompt);
        Assert.Contains("no text", prompt);
    }

    [Fact]
    public void Build_TechnicalModeIgnoresVisualDescription()
    {
        var brief = new SceneVisualBrief(
            0,
            "Video 1",
            "Local AI flow",
            SceneVisualLayout.Hero,
            SceneVisualPalette.Ocean,
            [new SceneVisualCard("Laptop")],
            VisualDescription: "A dim study room at night with a desk lamp.");

        var prompt = SceneImagePrompt.Build(brief, narrative: false);

        Assert.Contains("Local AI flow", prompt);
        Assert.Contains("Laptop", prompt);
        Assert.DoesNotContain("dim study room at night", prompt);
    }

    [Fact]
    public void Build_NarrativeWithoutDescriptionFallsBackToBrief()
    {
        var brief = new SceneVisualBrief(
            0,
            "Video 1",
            "Local AI flow",
            SceneVisualLayout.Hero,
            SceneVisualPalette.Ocean,
            [new SceneVisualCard("Laptop")]);

        var prompt = SceneImagePrompt.Build(brief, narrative: true);

        Assert.Contains("Local AI flow", prompt);
        Assert.Contains("Laptop", prompt);
    }

    [Fact]
    public void Build_NarrativeUsesCallerArtDirectionOverNeutralStyle()
    {
        const string artDirection =
            "original cinematic anime, detailed anime background, clean expressive "
            + "linework, soft cel shading, cinematic lighting, cohesive character "
            + "design, deep blue night tones with warm amber highlights";
        var brief = new SceneVisualBrief(
            0,
            "Video 1",
            "Jam Dua Pagi",
            SceneVisualLayout.Hero,
            SceneVisualPalette.Ocean,
            [new SceneVisualCard("Laptop")],
            VisualDescription: "Wide static shot of a dim study room at night.");

        var prompt = SceneImagePrompt.Build(brief, narrative: true, artDirection);

        // The canonical art direction leads the prompt, replacing the neutral fallback.
        Assert.StartsWith("original cinematic anime", prompt);
        Assert.Contains("deep blue night tones with warm amber highlights", prompt);
        Assert.Contains("Wide static shot of a dim study room at night", prompt);
        Assert.DoesNotContain("cinematic storyboard illustration", prompt);
        Assert.DoesNotContain("Laptop", prompt);
    }

    [Fact]
    public void Build_TechnicalModeIgnoresArtDirection()
    {
        var brief = new SceneVisualBrief(
            0,
            "Video 1",
            "Local AI flow",
            SceneVisualLayout.Hero,
            SceneVisualPalette.Ocean,
            [new SceneVisualCard("Laptop")],
            VisualDescription: "A dim study room at night.");

        var prompt = SceneImagePrompt.Build(
            brief,
            narrative: false,
            artDirection: "original cinematic anime, soft cel shading");

        Assert.Contains("technology explainer", prompt);
        Assert.DoesNotContain("cinematic anime", prompt);
    }
}
