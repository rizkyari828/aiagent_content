using AIStudio.Application.Capabilities;
using Xunit;

namespace AIStudio.Tests.Capabilities;

public sealed class CapabilityRegistryTests
{
    [Fact]
    public void ReportsRegisteredCapabilities()
    {
        var registry = Create(
            Provider("manim", CapabilityIds.VisualDiagram),
            Provider("animated-svg", CapabilityIds.VisualUiMotion));

        Assert.Contains(CapabilityIds.VisualDiagram, registry.Capabilities.Select(c => c.Id));
        Assert.True(registry.IsRegistered(CapabilityIds.VisualDiagram));
        Assert.False(registry.IsRegistered(new CapabilityId("visual.image_to_video")));
    }

    [Fact]
    public void ResolvesEnabledProvider()
    {
        var registry = Create(Provider("manim", CapabilityIds.VisualDiagram));

        var resolution = registry.Resolve(CapabilityRequirement.For(CapabilityIds.VisualDiagram));

        Assert.True(resolution.IsResolved);
        Assert.Equal("manim", resolution.Provider!.ProviderId);
        Assert.Equal(CapabilityIds.VisualDiagram, resolution.ResolvedCapabilityId);
        Assert.False(resolution.UsedFallback);
        Assert.Null(resolution.Gap);
        Assert.Equal(CapabilityAvailability.Enabled, resolution.Provider.Availability);
    }

    [Fact]
    public void MissingCapabilityReportsNotImplementedGap()
    {
        var registry = Create(Provider("manim", CapabilityIds.VisualDiagram));
        var missing = new CapabilityId("visual.image_to_video");

        var resolution = registry.Resolve(CapabilityRequirement.For(missing));

        Assert.False(resolution.IsResolved);
        Assert.Null(resolution.Provider);
        Assert.Equal(CapabilityGapReason.CapabilityNotImplemented, resolution.Gap!.Reason);
        Assert.Equal(missing, resolution.Gap.CapabilityId);
        Assert.Empty(resolution.Gap.KnownProviderIds);
    }

    [Fact]
    public void DisabledProviderReportsProviderDisabledGap()
    {
        var registry = Create(Provider("manim", CapabilityIds.VisualDiagram, enabled: false));

        var resolution = registry.Resolve(CapabilityRequirement.For(CapabilityIds.VisualDiagram));

        Assert.False(resolution.IsResolved);
        Assert.Equal(CapabilityGapReason.ProviderDisabled, resolution.Gap!.Reason);
        Assert.Equal(["manim"], resolution.Gap.KnownProviderIds);
        Assert.Equal(CapabilityAvailability.Registered, registry.GetProviders(CapabilityIds.VisualDiagram)[0].Availability);
    }

    [Fact]
    public void FallbackResolvesWhenPrimaryIsDisabled()
    {
        var registry = Create(
            Provider("comfyui-flux", CapabilityIds.VisualAiImage, enabled: false),
            Provider("animated-svg", CapabilityIds.VisualUiMotion));

        var resolution = registry.Resolve(CapabilityRequirement.For(
            CapabilityIds.VisualAiImage,
            CapabilityIds.VisualDiagram,
            CapabilityIds.VisualUiMotion));

        Assert.True(resolution.IsResolved);
        Assert.True(resolution.UsedFallback);
        Assert.Equal(CapabilityIds.VisualUiMotion, resolution.ResolvedCapabilityId);
        Assert.Equal("animated-svg", resolution.Provider!.ProviderId);
    }

    [Fact]
    public void PrimaryProviderWinsOverFallbackWhenEnabled()
    {
        var registry = Create(
            Provider("manim", CapabilityIds.VisualDiagram),
            Provider("animated-svg", CapabilityIds.VisualUiMotion));

        var resolution = registry.Resolve(CapabilityRequirement.For(
            CapabilityIds.VisualDiagram,
            CapabilityIds.VisualUiMotion));

        Assert.True(resolution.IsResolved);
        Assert.False(resolution.UsedFallback);
        Assert.Equal("manim", resolution.Provider!.ProviderId);
    }

    [Fact]
    public void FallbackGapKeepsThePrimaryReason()
    {
        var registry = Create(
            Provider("comfyui-flux", CapabilityIds.VisualAiImage, enabled: false),
            Provider("animated-svg", CapabilityIds.VisualUiMotion, enabled: false));

        var resolution = registry.Resolve(CapabilityRequirement.For(
            CapabilityIds.VisualAiImage,
            CapabilityIds.VisualUiMotion));

        Assert.False(resolution.IsResolved);
        Assert.Equal(CapabilityIds.VisualAiImage, resolution.Gap!.CapabilityId);
        Assert.Equal(CapabilityGapReason.ProviderDisabled, resolution.Gap.Reason);
    }

    [Fact]
    public void AvailableProvidersExcludeDisabledOnes()
    {
        var registry = Create(
            Provider("manim", CapabilityIds.VisualDiagram, enabled: false),
            Provider("manim-v2", CapabilityIds.VisualDiagram, enabled: true));

        Assert.Equal(2, registry.GetProviders(CapabilityIds.VisualDiagram).Count);
        Assert.Equal(["manim-v2"], registry.GetAvailableProviders(CapabilityIds.VisualDiagram).Select(p => p.ProviderId));
    }

    [Fact]
    public void ProviderOrderingIsDeterministic()
    {
        var registry = Create(
            Provider("low", CapabilityIds.VisualDiagram, priority: 1),
            Provider("high", CapabilityIds.VisualDiagram, priority: 200),
            Provider("mid-b", CapabilityIds.VisualDiagram, priority: 100),
            Provider("mid-a", CapabilityIds.VisualDiagram, priority: 100));

        Assert.Equal(
            ["high", "mid-a", "mid-b", "low"],
            registry.GetProviders(CapabilityIds.VisualDiagram).Select(p => p.ProviderId));
    }

    [Fact]
    public void FindGapMirrorsResolve()
    {
        var registry = Create(
            Provider("manim", CapabilityIds.VisualDiagram, enabled: false),
            Provider("svg-still", CapabilityIds.VisualStill));

        var gap = registry.FindGap(CapabilityRequirement.For(CapabilityIds.VisualDiagram));

        Assert.NotNull(gap);
        Assert.Equal(CapabilityGapReason.ProviderDisabled, gap!.Reason);
        Assert.Null(registry.FindGap(CapabilityRequirement.For(
            CapabilityIds.VisualDiagram,
            CapabilityIds.VisualStill)));
    }

    [Fact]
    public void DuplicateCapabilityRegistrationThrows()
    {
        var capabilities = new[]
        {
            new CapabilityDescriptor { Id = CapabilityIds.VisualDiagram },
            new CapabilityDescriptor { Id = CapabilityIds.VisualDiagram }
        };

        Assert.Throws<InvalidOperationException>(() => new CapabilityRegistry(
            capabilities,
            Array.Empty<CapabilityProviderDescriptor>()));
    }

    [Fact]
    public void DuplicateProviderRegistrationThrows()
    {
        var providers = new[]
        {
            Provider("manim", CapabilityIds.VisualDiagram),
            Provider("manim", CapabilityIds.VisualUiMotion)
        };

        Assert.Throws<InvalidOperationException>(() => new CapabilityRegistry(
            DistinctCapabilities(providers),
            providers));
    }

    [Fact]
    public void ProviderForUnknownCapabilityThrows()
    {
        Assert.Throws<InvalidOperationException>(() => new CapabilityRegistry(
            Array.Empty<CapabilityDescriptor>(),
            new[] { Provider("manim", CapabilityIds.VisualDiagram) }));
    }

    private static CapabilityRegistry Create(params CapabilityProviderDescriptor[] providers) =>
        new(DistinctCapabilities(providers), providers);

    private static IEnumerable<CapabilityDescriptor> DistinctCapabilities(
        IEnumerable<CapabilityProviderDescriptor> providers) =>
        providers
            .Select(provider => provider.CapabilityId)
            .Distinct()
            .Select(id => new CapabilityDescriptor
            {
                Id = id,
                DisplayName = id.Value,
                Category = "test"
            });

    private static CapabilityProviderDescriptor Provider(
        string providerId,
        CapabilityId capabilityId,
        bool enabled = true,
        int priority = 100) =>
        new()
        {
            ProviderId = providerId,
            CapabilityId = capabilityId,
            Enabled = enabled,
            Priority = priority,
            RuntimeCategory = "test"
        };
}
