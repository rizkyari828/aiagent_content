using System.Text.Json;
using AIStudio.Application.ProductionRecipes;
using Xunit;

namespace AIStudio.Tests.ProductionRecipes;

public sealed class ProductionRecipeSerializationTests
{
    [Fact]
    public void RecipeRoundTripsThroughPlainSystemTextJson()
    {
        var recipe = SeedProductionRecipes.All.Single(r => r.Id.Value == "tech-explainer");

        var json = JsonSerializer.Serialize(recipe);
        var restored = JsonSerializer.Deserialize<ProductionRecipe>(json);

        Assert.NotNull(restored);
        Assert.Equal(recipe.Id, restored!.Id);
        Assert.Equal(recipe.Version, restored.Version);
        Assert.Empty(restored.Validate());
        Assert.Equal(recipe.Requirements.Count, restored.Requirements.Count);

        var original = recipe.Requirements[1].Capability;
        var roundTripped = restored.Requirements[1].Capability;
        Assert.Equal(original.CapabilityId, roundTripped.CapabilityId);
        Assert.Equal(original.FallbackCapabilityIds, roundTripped.FallbackCapabilityIds);
        Assert.True(restored.Requirements[1].IsRequired);
    }
}
