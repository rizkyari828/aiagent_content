using AIStudio.Application.Capabilities;
using AIStudio.Application.ProductionRecipes;
using Xunit;

namespace AIStudio.Tests.ProductionRecipes;

public sealed class ProductionRecipeResolverTests
{
    [Fact]
    public void FullySupportedWhenEveryPrimaryProviderIsEnabled()
    {
        var recipe = SeedRecipe("tech-explainer");

        var resolution = ProductionRecipeTestSupport.Resolver().Resolve(recipe);

        Assert.Equal(ProductionRecipeStatus.FullySupported, resolution.Status);
        Assert.True(resolution.IsProducible);
        Assert.Empty(resolution.Gaps);
        Assert.All(resolution.Requirements, requirement =>
        {
            Assert.True(requirement.IsSatisfied);
            Assert.False(requirement.Capability.UsedFallback);
        });
    }

    [Fact]
    public void SupportedWithFallbacksWhenPrimaryVisualIsDisabled()
    {
        var recipe = SeedRecipe("tech-explainer");

        var resolution = ProductionRecipeTestSupport.Resolver(manimEnabled: false).Resolve(recipe);

        Assert.Equal(ProductionRecipeStatus.SupportedWithFallbacks, resolution.Status);
        Assert.True(resolution.IsProducible);
        Assert.Empty(resolution.Gaps);

        var visual = resolution.Requirements.Single(
            requirement => requirement.Requirement.Capability.CapabilityId.Equals(CapabilityIds.VisualDiagram));

        Assert.True(visual.Capability.UsedFallback);
        Assert.Equal(CapabilityIds.VisualUiMotion, visual.Capability.ResolvedCapabilityId);
        Assert.Equal("animated-svg", visual.Capability.Provider!.ProviderId);
    }

    [Fact]
    public void UnsupportedWhenRequiredCapabilityDisabledWithoutFallback()
    {
        var recipe = ProductionRecipeTestSupport.Recipe(
            "diagram-only",
            1,
            ProductionRecipeRequirement.Required(CapabilityIds.VisualDiagram));

        var resolution = ProductionRecipeTestSupport.Resolver(manimEnabled: false).Resolve(recipe);

        Assert.Equal(ProductionRecipeStatus.Unsupported, resolution.Status);
        Assert.False(resolution.IsProducible);
        var gap = Assert.Single(resolution.Gaps);
        Assert.Equal(CapabilityGapReason.ProviderDisabled, gap.Reason);
        Assert.Equal(["manim"], gap.KnownProviderIds);
    }

    [Fact]
    public void OptionalCapabilityUnavailableDoesNotBlockProduction()
    {
        var recipe = ProductionRecipeTestSupport.Recipe(
            "narrated-still",
            1,
            ProductionRecipeRequirement.Required(CapabilityIds.SpeechNarration),
            ProductionRecipeRequirement.Optional(CapabilityIds.VisualThreeD));

        var resolution = ProductionRecipeTestSupport.Resolver(threeDEnabled: false).Resolve(recipe);

        Assert.Equal(ProductionRecipeStatus.FullySupported, resolution.Status);
        Assert.True(resolution.IsProducible);

        var optional = resolution.Requirements.Single(
            requirement => requirement.Requirement.Capability.CapabilityId.Equals(CapabilityIds.VisualThreeD));
        Assert.False(optional.IsSatisfied);
        Assert.False(optional.Requirement.IsRequired);

        var gap = Assert.Single(resolution.Gaps);
        Assert.Equal(CapabilityIds.VisualThreeD, gap.CapabilityId);
        Assert.Equal(CapabilityGapReason.ProviderDisabled, gap.Reason);
    }

    [Fact]
    public void OptionalCapabilityEnabledStaysFullySupported()
    {
        var recipe = ProductionRecipeTestSupport.Recipe(
            "narrated-still",
            1,
            ProductionRecipeRequirement.Required(CapabilityIds.SpeechNarration),
            ProductionRecipeRequirement.Optional(CapabilityIds.VisualThreeD));

        var resolution = ProductionRecipeTestSupport.Resolver(threeDEnabled: true).Resolve(recipe);

        Assert.Equal(ProductionRecipeStatus.FullySupported, resolution.Status);
        Assert.True(resolution.Requirements.Single(
            requirement => requirement.Requirement.Capability.CapabilityId.Equals(CapabilityIds.VisualThreeD)).IsSatisfied);
        Assert.Empty(resolution.Gaps);
    }

    [Fact]
    public void RequiredFallbackDowngradesStatusWhileOptionalGapStaysNonBlocking()
    {
        var recipe = ProductionRecipeTestSupport.Recipe(
            "mixed",
            1,
            ProductionRecipeRequirement.Required(CapabilityIds.VisualDiagram, CapabilityIds.VisualUiMotion),
            ProductionRecipeRequirement.Optional(CapabilityIds.VisualThreeD));

        var resolution = ProductionRecipeTestSupport.Resolver(manimEnabled: false, threeDEnabled: false)
            .Resolve(recipe);

        Assert.Equal(ProductionRecipeStatus.SupportedWithFallbacks, resolution.Status);
        Assert.True(resolution.IsProducible);
        var gap = Assert.Single(resolution.Gaps);
        Assert.Equal(CapabilityIds.VisualThreeD, gap.CapabilityId);
    }

    [Fact]
    public void MissingCapabilityReportsNotImplemented()
    {
        var recipe = ProductionRecipeTestSupport.Recipe(
            "missing-capability",
            1,
            ProductionRecipeRequirement.Required(new CapabilityId("visual.image_to_video")));

        var resolution = ProductionRecipeTestSupport.Resolver().Resolve(recipe);

        Assert.Equal(ProductionRecipeStatus.Unsupported, resolution.Status);
        Assert.Equal(CapabilityGapReason.CapabilityNotImplemented, Assert.Single(resolution.Gaps).Reason);
    }

    [Fact]
    public void FutureCapabilityIsValidDataButUnresolved()
    {
        var future = new CapabilityId("visual.character_animation");
        var recipe = ProductionRecipeTestSupport.Recipe(
            "character-format",
            1,
            ProductionRecipeRequirement.Required(future));

        // The recipe is valid data...
        Assert.Empty(recipe.Validate());

        // ...but no fake provider is added, so resolution reports an explicit gap.
        var registry = ProductionRecipeTestSupport.CapabilityRegistry();
        Assert.False(registry.IsRegistered(future));
        Assert.Empty(registry.GetProviders(future));

        var resolution = new ProductionRecipeResolver(registry).Resolve(recipe);
        Assert.Equal(ProductionRecipeStatus.Unsupported, resolution.Status);
        Assert.Equal(CapabilityGapReason.CapabilityNotImplemented, resolution.Gaps[0].Reason);
        Assert.Equal(future, resolution.Gaps[0].CapabilityId);
    }

    [Fact]
    public void DelegatesEachRequirementToCapabilityRegistryInOrder()
    {
        var recording = new RecordingCapabilityRegistry(ProductionRecipeTestSupport.CapabilityRegistry());
        var recipe = SeedRecipe("tech-explainer");

        var resolution = new ProductionRecipeResolver(recording).Resolve(recipe);

        Assert.True(resolution.IsProducible);
        Assert.Equal(
            recipe.Requirements.Select(requirement => requirement.Capability.CapabilityId),
            recording.RequestedCapabilities);
    }

    [Fact]
    public void InheritsDeterministicProviderSelectionFromCapabilityRegistry()
    {
        var capabilities = ProductionCapabilityCatalog.Capabilities;
        var providers = new[]
        {
            new CapabilityProviderDescriptor
            {
                ProviderId = "manim",
                CapabilityId = CapabilityIds.VisualDiagram,
                Priority = 100,
                Enabled = true
            },
            new CapabilityProviderDescriptor
            {
                ProviderId = "manim-hi",
                CapabilityId = CapabilityIds.VisualDiagram,
                Priority = 200,
                Enabled = true
            }
        };
        var resolver = new ProductionRecipeResolver(new CapabilityRegistry(capabilities, providers));
        var recipe = ProductionRecipeTestSupport.Recipe(
            "diagram-only",
            1,
            ProductionRecipeRequirement.Required(CapabilityIds.VisualDiagram));

        var resolution = resolver.Resolve(recipe);

        Assert.Equal("manim-hi", resolution.Requirements[0].Capability.Provider!.ProviderId);
    }

    [Fact]
    public void ResolutionNeedsNoProviderInstances()
    {
        // The capability registry is built from descriptors only; no provider
        // service is registered or constructed anywhere in the resolution path.
        var recipe = SeedRecipe("motion-comic");

        var resolution = ProductionRecipeTestSupport.Resolver().Resolve(recipe);

        Assert.Equal(ProductionRecipeStatus.FullySupported, resolution.Status);
        Assert.All(resolution.Requirements, requirement => Assert.NotNull(requirement.Capability.Provider));
    }

    private static ProductionRecipe SeedRecipe(string id) =>
        SeedProductionRecipes.All.Single(recipe => recipe.Id.Value == id);
}
