using AIStudio.Application.Capabilities;
using AIStudio.Application.ProductionRecipes;
using Xunit;

namespace AIStudio.Tests.ProductionRecipes;

public sealed class SeedProductionRecipesTests
{
    [Fact]
    public void TechExplainerDeclaresItsCapabilitiesAsData()
    {
        var recipe = SeedProductionRecipes.All.Single(r => r.Id.Value == "tech-explainer");

        Assert.Equal(1, recipe.Version.Value);

        var primaries = recipe.Requirements
            .Select(requirement => requirement.Capability.CapabilityId)
            .ToList();

        Assert.Equal(
            [
                CapabilityIds.SpeechNarration,
                CapabilityIds.VisualDiagram,
                CapabilityIds.MusicInstrumental,
                CapabilityIds.MediaCompose,
                CapabilityIds.SubtitleBurned
            ],
            primaries);

        var diagram = recipe.Requirements
            .Single(requirement => requirement.Capability.CapabilityId.Equals(CapabilityIds.VisualDiagram));

        Assert.Equal(
            [CapabilityIds.VisualUiMotion, CapabilityIds.VisualStill],
            diagram.Capability.FallbackCapabilityIds);
    }

    [Fact]
    public void MotionComicDeclaresItsCapabilitiesAsData()
    {
        var recipe = SeedProductionRecipes.All.Single(r => r.Id.Value == "motion-comic");

        var image = recipe.Requirements
            .Single(requirement => requirement.Capability.CapabilityId.Equals(CapabilityIds.VisualAiImage));

        Assert.Equal([CapabilityIds.VisualStill], image.Capability.FallbackCapabilityIds);
        Assert.True(image.IsRequired);
    }

    [Fact]
    public void SeedCatalogStaysSmallAndDataDriven()
    {
        // Two seed recipes are enough to prove the architecture; more formats are
        // expected as data later, not as new C# types here.
        Assert.Equal(2, SeedProductionRecipes.All.Count);
        Assert.All(SeedProductionRecipes.All, recipe =>
        {
            Assert.True(ProductionRecipeId.IsValid(recipe.Id.Value));
            Assert.True(recipe.Version.IsValid);
            Assert.NotEmpty(recipe.Requirements);
        });
    }
}
