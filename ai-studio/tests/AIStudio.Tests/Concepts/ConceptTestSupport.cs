using AIStudio.Application.Concepts;
using AIStudio.Application.ProductionRecipes;
using AIStudio.Tests.ProductionRecipes;

namespace AIStudio.Tests.Concepts;

internal static class ConceptTestSupport
{
    public static ConceptManifest Concept(
        string id,
        string format = "youtube-short",
        string style = "clean-tech",
        string recipeId = "tech-explainer",
        int duration = 45,
        int version = 1,
        ProductionRecipeVersion? recipeVersion = null) =>
        new()
        {
            Id = new ConceptId(id),
            Version = new ConceptVersion(version),
            Title = id,
            Description = "test concept",
            Audience = "developers",
            Format = format,
            Style = style,
            RecipeId = new ProductionRecipeId(recipeId),
            RecipeVersion = recipeVersion,
            Duration = duration,
            Tags = ["local-ai"]
        };

    public static ConceptResolver Resolver(
        bool speechEnabled = true,
        bool musicEnabled = true,
        bool manimEnabled = true,
        bool imageEnabled = true,
        bool threeDEnabled = true) =>
        new(
            new ProductionRecipeRegistry(SeedProductionRecipes.All),
            ProductionRecipeTestSupport.Resolver(
                speechEnabled,
                musicEnabled,
                manimEnabled,
                imageEnabled,
                threeDEnabled));
}

/// <summary>
/// Delegating recipe resolver that records which recipes were resolved, used to
/// prove the concept resolver delegates instead of re-implementing recipe logic.
/// </summary>
internal sealed class RecordingRecipeResolver : IProductionRecipeResolver
{
    private readonly IProductionRecipeResolver _inner;

    public RecordingRecipeResolver(IProductionRecipeResolver inner)
    {
        _inner = inner;
    }

    public List<ProductionRecipeId> ResolvedRecipeIds { get; } = [];

    public ProductionRecipeResolution Resolve(ProductionRecipe recipe)
    {
        ResolvedRecipeIds.Add(recipe.Id);
        return _inner.Resolve(recipe);
    }
}
