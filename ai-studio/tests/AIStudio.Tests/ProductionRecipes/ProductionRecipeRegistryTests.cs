using AIStudio.Application.Capabilities;
using AIStudio.Application.ProductionRecipes;
using Xunit;

namespace AIStudio.Tests.ProductionRecipes;

public sealed class ProductionRecipeRegistryTests
{
    [Fact]
    public void ListsRegisteredRecipesInDeterministicOrder()
    {
        var registry = new ProductionRecipeRegistry(SeedProductionRecipes.All);

        Assert.Equal(
            ["motion-comic", "tech-explainer"],
            registry.Recipes.Select(recipe => recipe.Id.Value));
    }

    [Fact]
    public void RetrievesByStableRecipeIdAndVersion()
    {
        var registry = new ProductionRecipeRegistry(SeedProductionRecipes.All);

        var recipe = registry.Get(new ProductionRecipeId("tech-explainer"), new ProductionRecipeVersion(1));

        Assert.Equal("tech-explainer", recipe.Id.Value);
        Assert.True(registry.Contains(new ProductionRecipeId("tech-explainer"), new ProductionRecipeVersion(1)));
        Assert.False(registry.Contains(new ProductionRecipeId("tech-explainer"), new ProductionRecipeVersion(2)));
    }

    [Fact]
    public void GetLatestReturnsHighestVersion()
    {
        var v1 = ProductionRecipeTestSupport.Recipe(
            "evolving",
            1,
            ProductionRecipeRequirement.Required(CapabilityIds.SpeechNarration));
        var v2 = ProductionRecipeTestSupport.Recipe(
            "evolving",
            2,
            ProductionRecipeRequirement.Required(CapabilityIds.SpeechNarration));

        var registry = new ProductionRecipeRegistry([v2, v1]);

        Assert.Equal(2, registry.GetLatest(new ProductionRecipeId("evolving")).Version.Value);
        Assert.Equal(1, registry.Get(new ProductionRecipeId("evolving"), new ProductionRecipeVersion(1)).Version.Value);
    }

    [Fact]
    public void DuplicateIdAndVersionRegistrationIsRejected()
    {
        var first = ProductionRecipeTestSupport.Recipe(
            "duplicate",
            1,
            ProductionRecipeRequirement.Required(CapabilityIds.SpeechNarration));
        var second = ProductionRecipeTestSupport.Recipe(
            "duplicate",
            1,
            ProductionRecipeRequirement.Required(CapabilityIds.MusicInstrumental));

        Assert.Throws<InvalidOperationException>(() => new ProductionRecipeRegistry([first, second]));
    }

    [Fact]
    public void InvalidRecipeRegistrationIsRejected()
    {
        var invalid = new ProductionRecipe();

        Assert.Throws<InvalidOperationException>(() => new ProductionRecipeRegistry([invalid]));
    }

    [Fact]
    public void MissingRecipeThrows()
    {
        var registry = new ProductionRecipeRegistry(SeedProductionRecipes.All);

        Assert.Throws<KeyNotFoundException>(
            () => registry.GetLatest(new ProductionRecipeId("missing-format")));
    }
}
