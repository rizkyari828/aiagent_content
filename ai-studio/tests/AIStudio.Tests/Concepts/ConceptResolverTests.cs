using AIStudio.Application.Capabilities;
using AIStudio.Application.Concepts;
using AIStudio.Application.ProductionRecipes;
using AIStudio.Tests.ProductionRecipes;
using Xunit;

namespace AIStudio.Tests.Concepts;

public sealed class ConceptResolverTests
{
    [Fact]
    public void KnownRecipeWithAllCapabilitiesIsReady()
    {
        var concept = ConceptTestSupport.Concept("local-ai-tech-explainer", recipeId: "tech-explainer");

        var resolution = ConceptTestSupport.Resolver().Resolve(concept);

        Assert.Equal(ConceptResolutionStatus.Ready, resolution.Status);
        Assert.True(resolution.IsProducible);
        Assert.Empty(resolution.Issues);
        Assert.Equal("tech-explainer", resolution.RecipeId.Value);
        Assert.Equal(1, resolution.RecipeVersion!.Value.Value);
        Assert.NotNull(resolution.RecipeResolution);
    }

    [Fact]
    public void KnownRecipeUsingFallbackIsReadyWithFallbacks()
    {
        var concept = ConceptTestSupport.Concept("local-ai-tech-explainer", recipeId: "tech-explainer");

        var resolution = ConceptTestSupport.Resolver(manimEnabled: false).Resolve(concept);

        Assert.Equal(ConceptResolutionStatus.ReadyWithFallbacks, resolution.Status);
        Assert.True(resolution.IsProducible);
        Assert.Empty(resolution.Issues);
    }

    [Fact]
    public void KnownRecipeWithUnresolvedRequiredCapabilityIsUnsupported()
    {
        var concept = ConceptTestSupport.Concept("local-ai-tech-explainer", recipeId: "tech-explainer");

        var resolution = ConceptTestSupport.Resolver(speechEnabled: false).Resolve(concept);

        Assert.Equal(ConceptResolutionStatus.Unsupported, resolution.Status);
        Assert.False(resolution.IsProducible);
        Assert.Contains(ConceptResolutionIssueCodes.RecipeUnsupported, resolution.Issues.Select(issue => issue.Code));
        Assert.NotEmpty(resolution.Gaps);
    }

    [Fact]
    public void MissingRecipeIsReportedDistinctlyFromCapabilityGap()
    {
        var concept = ConceptTestSupport.Concept(
            "future-anime-short",
            format: "anime-short",
            style: "anime-cinematic",
            recipeId: "future-anime-video");

        var resolution = ConceptTestSupport.Resolver().Resolve(concept);

        Assert.Equal(ConceptResolutionStatus.Unsupported, resolution.Status);
        Assert.Equal(
            ConceptResolutionIssueCodes.RecipeNotFound,
            Assert.Single(resolution.Issues).Code);
        Assert.Null(resolution.RecipeResolution);
        Assert.Empty(resolution.Gaps);
    }

    [Fact]
    public void ResolvesLatestRecipeVersionWhenNoneIsRequested()
    {
        var recipeV1 = ProductionRecipeTestSupport.Recipe(
            "evolving",
            1,
            ProductionRecipeRequirement.Required(CapabilityIds.SpeechNarration));
        var recipeV2 = ProductionRecipeTestSupport.Recipe(
            "evolving",
            2,
            ProductionRecipeRequirement.Required(CapabilityIds.SpeechNarration));
        var resolver = new ConceptResolver(
            new ProductionRecipeRegistry([recipeV2, recipeV1]),
            ProductionRecipeTestSupport.Resolver());

        var concept = ConceptTestSupport.Concept("latest-recipe", recipeId: "evolving");

        var resolution = resolver.Resolve(concept);

        Assert.Equal(2, resolution.RecipeVersion!.Value.Value);
        Assert.Equal(ConceptResolutionStatus.Ready, resolution.Status);
    }

    [Fact]
    public void OptionalCapabilityBehaviorIsInheritedFromRecipe()
    {
        var recipe = ProductionRecipeTestSupport.Recipe(
            "optional-threed",
            1,
            ProductionRecipeRequirement.Required(CapabilityIds.SpeechNarration),
            ProductionRecipeRequirement.Optional(CapabilityIds.VisualThreeD));
        var resolver = new ConceptResolver(
            new ProductionRecipeRegistry([recipe]),
            ProductionRecipeTestSupport.Resolver(threeDEnabled: false));

        var concept = ConceptTestSupport.Concept("optional-threed", recipeId: "optional-threed");

        var resolution = resolver.Resolve(concept);

        Assert.Equal(ConceptResolutionStatus.Ready, resolution.Status);
        Assert.True(resolution.IsProducible);
        var gap = Assert.Single(resolution.Gaps);
        Assert.Equal(CapabilityIds.VisualThreeD, gap.CapabilityId);
    }

    [Fact]
    public void DelegatesToProductionRecipeResolver()
    {
        var recording = new RecordingRecipeResolver(ProductionRecipeTestSupport.Resolver());
        var resolver = new ConceptResolver(new ProductionRecipeRegistry(SeedProductionRecipes.All), recording);

        var resolution = resolver.Resolve(ConceptTestSupport.Concept("delegated", recipeId: "tech-explainer"));

        Assert.True(resolution.IsProducible);
        Assert.Equal([new ProductionRecipeId("tech-explainer")], recording.ResolvedRecipeIds);
    }

    [Fact]
    public void ResolutionNeedsNoProviderInstances()
    {
        // The capability/recipe registries are descriptor-only; no provider service
        // is registered or constructed anywhere in the resolution path.
        var resolution = ConceptTestSupport.Resolver().Resolve(
            ConceptTestSupport.Concept("local-ai-motion-comic", recipeId: "motion-comic"));

        Assert.Equal(ConceptResolutionStatus.Ready, resolution.Status);
        Assert.All(resolution.RecipeResolution!.Requirements, requirement =>
            Assert.NotNull(requirement.Capability.Provider));
    }
}
