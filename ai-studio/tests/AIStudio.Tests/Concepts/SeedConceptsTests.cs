using AIStudio.Application.Concepts;
using AIStudio.Application.ProductionRecipes;
using Xunit;

namespace AIStudio.Tests.Concepts;

public sealed class SeedConceptsTests
{
    [Fact]
    public void TechExplainerConceptDeclaresItselfAsData()
    {
        var concept = SeedConcepts.All.Single(c => c.Id.Value == "local-ai-tech-explainer");

        Assert.Equal("youtube-longform", concept.Format);
        Assert.Equal("clean-tech", concept.Style);
        Assert.Equal("developers", concept.Audience);
        Assert.Equal("tech-explainer", concept.RecipeId.Value);
        Assert.Equal(1, concept.RecipeVersion!.Value.Value);
    }

    [Fact]
    public void MotionComicConceptOmitsRecipeVersionToRequestLatest()
    {
        var concept = SeedConcepts.All.Single(c => c.Id.Value == "local-ai-motion-comic");

        Assert.Equal("youtube-short", concept.Format);
        Assert.Equal("motion-comic", concept.Style);
        Assert.Equal("motion-comic", concept.RecipeId.Value);
        Assert.Null(concept.RecipeVersion);
    }

    [Fact]
    public void SeedCatalogStaysSmallAndReferencesTrustedRecipes()
    {
        Assert.Equal(2, SeedConcepts.All.Count);

        var recipeIds = SeedProductionRecipes.All.Select(recipe => recipe.Id.Value).ToHashSet();
        Assert.All(SeedConcepts.All, concept =>
        {
            Assert.Empty(concept.Validate());
            Assert.Contains(concept.RecipeId.Value, recipeIds);
        });
    }

    [Fact]
    public void SeedConceptsResolveReadyWithAllProvidersEnabled()
    {
        var resolver = ConceptTestSupport.Resolver();

        Assert.All(SeedConcepts.All, concept =>
            Assert.Equal(ConceptResolutionStatus.Ready, resolver.Resolve(concept).Status));
    }
}
