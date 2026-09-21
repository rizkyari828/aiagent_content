using AIStudio.Application.Capabilities;
using Xunit;

namespace AIStudio.Tests.Capabilities;

public sealed class ProductionCapabilityCatalogTests
{
    [Fact]
    public void CatalogRegistersEveryKnownCapability()
    {
        var ids = ProductionCapabilityCatalog.Capabilities.Select(capability => capability.Id).ToHashSet();

        Assert.Contains(CapabilityIds.SpeechNarration, ids);
        Assert.Contains(CapabilityIds.MusicInstrumental, ids);
        Assert.Contains(CapabilityIds.VisualStill, ids);
        Assert.Contains(CapabilityIds.VisualDiagram, ids);
        Assert.Contains(CapabilityIds.VisualUiMotion, ids);
        Assert.Contains(CapabilityIds.VisualAiImage, ids);
        Assert.Contains(CapabilityIds.VisualThreeD, ids);
        Assert.Contains(CapabilityIds.MediaCompose, ids);
        Assert.Contains(CapabilityIds.SubtitleBurned, ids);
    }

    [Fact]
    public void CatalogMapsTheCurrentTrustedStack()
    {
        var registry = new CapabilityRegistry(
            ProductionCapabilityCatalog.Capabilities,
            ProductionCapabilityCatalog.CreateProviders(
                speechEnabled: true,
                musicEnabled: true,
                manimEnabled: true,
                imageEnabled: true,
                threeDEnabled: true));

        AssertProvider(registry, CapabilityIds.SpeechNarration, "voxcpm2");
        AssertProvider(registry, CapabilityIds.MusicInstrumental, "ace-step");
        AssertProvider(registry, CapabilityIds.VisualStill, "svg-still");
        AssertProvider(registry, CapabilityIds.VisualDiagram, "manim");
        AssertProvider(registry, CapabilityIds.VisualUiMotion, "animated-svg");
        AssertProvider(registry, CapabilityIds.VisualAiImage, "comfyui-flux");
        AssertProvider(registry, CapabilityIds.VisualThreeD, "blender");
        AssertProvider(registry, CapabilityIds.MediaCompose, "ffmpeg-compose");
        AssertProvider(registry, CapabilityIds.SubtitleBurned, "ffmpeg-subtitle-burn");
    }

    [Fact]
    public void CpuCapabilitiesStayEnabledWhileOptionalProvidersFollowConfiguration()
    {
        var registry = new CapabilityRegistry(
            ProductionCapabilityCatalog.Capabilities,
            ProductionCapabilityCatalog.CreateProviders(
                speechEnabled: false,
                musicEnabled: false,
                manimEnabled: false,
                imageEnabled: false,
                threeDEnabled: false));

        Assert.True(registry.Resolve(CapabilityRequirement.For(CapabilityIds.VisualStill)).IsResolved);
        Assert.True(registry.Resolve(CapabilityRequirement.For(CapabilityIds.VisualUiMotion)).IsResolved);
        Assert.True(registry.Resolve(CapabilityRequirement.For(CapabilityIds.MediaCompose)).IsResolved);
        Assert.True(registry.Resolve(CapabilityRequirement.For(CapabilityIds.SubtitleBurned)).IsResolved);

        Assert.Equal(
            CapabilityGapReason.ProviderDisabled,
            registry.Resolve(CapabilityRequirement.For(CapabilityIds.SpeechNarration)).Gap!.Reason);
        Assert.Equal(
            CapabilityGapReason.ProviderDisabled,
            registry.Resolve(CapabilityRequirement.For(CapabilityIds.VisualDiagram)).Gap!.Reason);
        Assert.Equal(
            CapabilityGapReason.ProviderDisabled,
            registry.Resolve(CapabilityRequirement.For(CapabilityIds.VisualAiImage)).Gap!.Reason);
        Assert.Equal(
            CapabilityGapReason.ProviderDisabled,
            registry.Resolve(CapabilityRequirement.For(CapabilityIds.VisualThreeD)).Gap!.Reason);
    }

    [Fact]
    public void DescriptorsNeverCarryExecutablePaths()
    {
        var providers = ProductionCapabilityCatalog.CreateProviders(
            speechEnabled: true,
            musicEnabled: true,
            manimEnabled: true,
            imageEnabled: true,
            threeDEnabled: true);

        Assert.All(providers, provider =>
        {
            Assert.DoesNotContain('/', provider.ProviderId);
            Assert.DoesNotContain('/', provider.RuntimeCategory);
        });
    }

    private static void AssertProvider(
        ICapabilityRegistry registry,
        CapabilityId capabilityId,
        string expectedProviderId)
    {
        Assert.True(registry.IsRegistered(capabilityId));
        Assert.Equal(expectedProviderId, registry.GetProviders(capabilityId)[0].ProviderId);
    }
}
