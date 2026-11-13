using AIStudio.Application.Capabilities;
using Xunit;

namespace AIStudio.Tests.Capabilities;

public sealed class ProductionCapabilityCatalogTests
{
    [Fact]
    public void CatalogRegistersEveryKnownCapability()
    {
        var ids = ProductionCapabilityCatalog.Capabilities.Select(capability => capability.Id).ToHashSet();

        Assert.Contains(CapabilityIds.TextGeneration, ids);
        Assert.Contains(CapabilityIds.SpeechNarration, ids);
        Assert.Contains(CapabilityIds.MusicInstrumental, ids);
        Assert.Contains(CapabilityIds.VisualStill, ids);
        Assert.Contains(CapabilityIds.VisualDiagram, ids);
        Assert.Contains(CapabilityIds.VisualUiMotion, ids);
        Assert.Contains(CapabilityIds.VisualAiImage, ids);
        Assert.Contains(CapabilityIds.VisualThreeD, ids);
        Assert.Contains(CapabilityIds.VisualCharacterAnimation, ids);
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
                threeDEnabled: true,
                videoEnabled: true));

        AssertProvider(registry, CapabilityIds.TextGeneration, "ollama");
        AssertProvider(registry, CapabilityIds.SpeechNarration, "voxcpm2");
        AssertProvider(registry, CapabilityIds.MusicInstrumental, "ace-step");
        AssertProvider(registry, CapabilityIds.VisualStill, "svg-still");
        AssertProvider(registry, CapabilityIds.VisualDiagram, "manim");
        AssertProvider(registry, CapabilityIds.VisualUiMotion, "animated-svg");
        AssertProvider(registry, CapabilityIds.VisualAiImage, "comfyui-flux");
        AssertProvider(registry, CapabilityIds.VisualThreeD, "blender");
        AssertProvider(registry, CapabilityIds.VisualCharacterAnimation, "comfyui-i2v");
        AssertProvider(registry, CapabilityIds.MediaCompose, "ffmpeg-compose");
        AssertProvider(registry, CapabilityIds.SubtitleBurned, "ffmpeg-subtitle-burn");
    }

    [Fact]
    public void CharacterAnimationProviderIsDisabledByDefault()
    {
        var registry = new CapabilityRegistry(
            ProductionCapabilityCatalog.Capabilities,
            ProductionCapabilityCatalog.CreateProviders(
                speechEnabled: false,
                musicEnabled: false,
                manimEnabled: false,
                imageEnabled: false,
                threeDEnabled: false,
                videoEnabled: false));

        var resolution = registry.Resolve(
            CapabilityRequirement.For(CapabilityIds.VisualCharacterAnimation));

        Assert.False(resolution.IsResolved);
        Assert.Equal(CapabilityGapReason.ProviderDisabled, resolution.Gap!.Reason);
        Assert.Equal("comfyui-i2v", registry
            .GetProviders(CapabilityIds.VisualCharacterAnimation)[0]
            .ProviderId);
        Assert.False(registry
            .GetProviders(CapabilityIds.VisualCharacterAnimation)[0]
            .Enabled);
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
                threeDEnabled: false,
                videoEnabled: false));

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
        Assert.Equal(
            CapabilityGapReason.ProviderDisabled,
            registry.Resolve(CapabilityRequirement.For(CapabilityIds.VisualCharacterAnimation)).Gap!.Reason);
    }

    [Fact]
    public void DescriptorsNeverCarryExecutablePaths()
    {
        var providers = ProductionCapabilityCatalog.CreateProviders(
            speechEnabled: true,
            musicEnabled: true,
            manimEnabled: true,
            imageEnabled: true,
            threeDEnabled: true,
            videoEnabled: true);

        Assert.All(providers, provider =>
        {
            Assert.DoesNotContain('/', provider.ProviderId);
            Assert.DoesNotContain('/', provider.RuntimeCategory);
        });
    }

    [Fact]
    public void RegistersMultipleProvidersPerCapabilityInPriorityOrder()
    {
        var registry = new CapabilityRegistry(
            ProductionCapabilityCatalog.Capabilities,
            ProductionCapabilityCatalog.CreateProviders(
                speechEnabled: true,
                musicEnabled: true,
                manimEnabled: true,
                imageEnabled: true,
                threeDEnabled: true,
                videoEnabled: true,
                textEnabled: true,
                geminiTextEnabled: true,
                geminiImageEnabled: true,
                geminiVideoEnabled: true,
                geminiSpeechEnabled: true,
                deepseekEnabled: true,
                openAiEnabled: true));

        var textProviders = registry.GetProviders(CapabilityIds.TextGeneration);
        Assert.Equal(5, textProviders.Count);
        Assert.Equal("ollama", textProviders[0].ProviderId);
        Assert.Equal(100, textProviders[0].Priority);
        Assert.Equal("local_service_gpu", textProviders[0].RuntimeCategory);
        Assert.True(textProviders[0].Enabled);

        Assert.Equal("gemini-text", textProviders[1].ProviderId);
        Assert.Equal(50, textProviders[1].Priority);
        Assert.Equal("cloud_api", textProviders[1].RuntimeCategory);
        Assert.True(textProviders[1].Enabled);

        Assert.Equal("deepseek", textProviders[2].ProviderId);
        Assert.Equal(45, textProviders[2].Priority);
        Assert.Equal("cloud_api", textProviders[2].RuntimeCategory);
        Assert.True(textProviders[2].Enabled);

        Assert.Equal("openai", textProviders[3].ProviderId);
        Assert.Equal(40, textProviders[3].Priority);
        Assert.Equal("cloud_api", textProviders[3].RuntimeCategory);
        Assert.True(textProviders[3].Enabled);

        var speechProviders = registry.GetProviders(CapabilityIds.SpeechNarration);
        Assert.Equal(2, speechProviders.Count);
        Assert.Equal("voxcpm2", speechProviders[0].ProviderId);
        Assert.Equal(100, speechProviders[0].Priority);
        Assert.Equal("gemini-tts", speechProviders[1].ProviderId);
        Assert.Equal(50, speechProviders[1].Priority);

        var imageProviders = registry.GetProviders(CapabilityIds.VisualAiImage);
        Assert.Equal(2, imageProviders.Count);
        Assert.Equal("comfyui-flux", imageProviders[0].ProviderId);
        Assert.Equal(100, imageProviders[0].Priority);
        Assert.Equal("gemini-nanobanana", imageProviders[1].ProviderId);
        Assert.Equal(50, imageProviders[1].Priority);

        var videoProviders = registry.GetProviders(CapabilityIds.VisualCharacterAnimation);
        Assert.Equal(2, videoProviders.Count);
        Assert.Equal("comfyui-i2v", videoProviders[0].ProviderId);
        Assert.Equal(100, videoProviders[0].Priority);
        Assert.Equal("gemini-veo", videoProviders[1].ProviderId);
        Assert.Equal(50, videoProviders[1].Priority);
    }

    [Fact]
    public void AutomaticallyFallsBackToCloudProviderWhenLocalProviderIsDisabled()
    {
        var registry = new CapabilityRegistry(
            ProductionCapabilityCatalog.Capabilities,
            ProductionCapabilityCatalog.CreateProviders(
                speechEnabled: false,
                musicEnabled: false,
                manimEnabled: false,
                imageEnabled: false,
                threeDEnabled: false,
                videoEnabled: false,
                textEnabled: false,
                geminiTextEnabled: true,
                geminiImageEnabled: true,
                geminiVideoEnabled: true,
                geminiSpeechEnabled: true));

        var textResolution = registry.Resolve(CapabilityRequirement.For(CapabilityIds.TextGeneration));
        Assert.True(textResolution.IsResolved);
        Assert.Equal("gemini-text", textResolution.Provider!.ProviderId);
        Assert.False(textResolution.UsedFallback);

        var speechResolution = registry.Resolve(CapabilityRequirement.For(CapabilityIds.SpeechNarration));
        Assert.True(speechResolution.IsResolved);
        Assert.Equal("gemini-tts", speechResolution.Provider!.ProviderId);
        Assert.False(speechResolution.UsedFallback);

        var imageResolution = registry.Resolve(CapabilityRequirement.For(CapabilityIds.VisualAiImage));
        Assert.True(imageResolution.IsResolved);
        Assert.Equal("gemini-nanobanana", imageResolution.Provider!.ProviderId);
        Assert.False(imageResolution.UsedFallback);

        var videoResolution = registry.Resolve(CapabilityRequirement.For(CapabilityIds.VisualCharacterAnimation));
        Assert.True(videoResolution.IsResolved);
        Assert.Equal("gemini-veo", videoResolution.Provider!.ProviderId);
        Assert.False(videoResolution.UsedFallback);
    }

    [Fact]
    public void AllTextProvidersDisabledReportsProviderDisabledGapWithAllKnownProviders()
    {
        var registry = new CapabilityRegistry(
            ProductionCapabilityCatalog.Capabilities,
            ProductionCapabilityCatalog.CreateProviders(
                speechEnabled: false,
                musicEnabled: false,
                manimEnabled: false,
                imageEnabled: false,
                threeDEnabled: false,
                videoEnabled: false,
                textEnabled: false,
                geminiTextEnabled: false,
                geminiImageEnabled: false,
                geminiVideoEnabled: false,
                geminiSpeechEnabled: false,
                deepseekEnabled: false,
                openAiEnabled: false));

        var resolution = registry.Resolve(CapabilityRequirement.For(CapabilityIds.TextGeneration));
        Assert.False(resolution.IsResolved);
        Assert.Equal(CapabilityGapReason.ProviderDisabled, resolution.Gap!.Reason);
        Assert.Equal(CapabilityIds.TextGeneration, resolution.Gap.CapabilityId);
        Assert.Equal(["ollama", "gemini-text", "deepseek", "openai", "gemini-cli"], resolution.Gap.KnownProviderIds);
    }

    [Fact]
    public void ResolvesExplicitPreferredProviderWhenEnabled()
    {
        var registry = new CapabilityRegistry(
            ProductionCapabilityCatalog.Capabilities,
            ProductionCapabilityCatalog.CreateProviders(
                speechEnabled: true,
                musicEnabled: true,
                manimEnabled: true,
                imageEnabled: true,
                threeDEnabled: true,
                videoEnabled: true,
                textEnabled: true,
                geminiTextEnabled: true,
                deepseekEnabled: true,
                openAiEnabled: true));

        var geminiReq = CapabilityRequirement.ForProvider(CapabilityIds.TextGeneration, "gemini-text");
        var geminiRes = registry.Resolve(geminiReq);
        Assert.True(geminiRes.IsResolved);
        Assert.Equal("gemini-text", geminiRes.Provider!.ProviderId);

        var deepseekReq = CapabilityRequirement.ForProvider(CapabilityIds.TextGeneration, "deepseek");
        var deepseekRes = registry.Resolve(deepseekReq);
        Assert.True(deepseekRes.IsResolved);
        Assert.Equal("deepseek", deepseekRes.Provider!.ProviderId);

        var openAiReq = CapabilityRequirement.ForProvider(CapabilityIds.TextGeneration, "openai");
        var openAiRes = registry.Resolve(openAiReq);
        Assert.True(openAiRes.IsResolved);
        Assert.Equal("openai", openAiRes.Provider!.ProviderId);
    }

    [Fact]
    public void ExplicitPreferredProviderDoesNotSilentlyFallBackWhenDisabled()
    {
        var registry = new CapabilityRegistry(
            ProductionCapabilityCatalog.Capabilities,
            ProductionCapabilityCatalog.CreateProviders(
                speechEnabled: true,
                musicEnabled: true,
                manimEnabled: true,
                imageEnabled: true,
                threeDEnabled: true,
                videoEnabled: true,
                textEnabled: true,
                geminiTextEnabled: false,
                deepseekEnabled: false,
                openAiEnabled: false));

        var requirement = CapabilityRequirement.ForProvider(CapabilityIds.TextGeneration, "gemini-text");
        var resolution = registry.Resolve(requirement);

        Assert.False(resolution.IsResolved);
        Assert.Equal(CapabilityGapReason.ProviderDisabled, resolution.Gap!.Reason);
        Assert.Equal(CapabilityIds.TextGeneration, resolution.Gap.CapabilityId);
        Assert.Equal(["ollama", "gemini-text", "deepseek", "openai", "gemini-cli"], resolution.Gap.KnownProviderIds);

        var deepseekReq = CapabilityRequirement.ForProvider(CapabilityIds.TextGeneration, "deepseek");
        var deepseekRes = registry.Resolve(deepseekReq);
        Assert.False(deepseekRes.IsResolved);
        Assert.Equal(CapabilityGapReason.ProviderDisabled, deepseekRes.Gap!.Reason);

        var openAiReq = CapabilityRequirement.ForProvider(CapabilityIds.TextGeneration, "openai");
        var openAiRes = registry.Resolve(openAiReq);
        Assert.False(openAiRes.IsResolved);
        Assert.Equal(CapabilityGapReason.ProviderDisabled, openAiRes.Gap!.Reason);
    }

    [Fact]
    public void ExplicitPreferredProviderReportsNotRegisteredWhenUnknown()
    {
        var registry = new CapabilityRegistry(
            ProductionCapabilityCatalog.Capabilities,
            ProductionCapabilityCatalog.CreateProviders(
                speechEnabled: true,
                musicEnabled: true,
                manimEnabled: true,
                imageEnabled: true,
                threeDEnabled: true,
                videoEnabled: true));

        var requirement = CapabilityRequirement.ForProvider(CapabilityIds.TextGeneration, "unknown-llm");
        var resolution = registry.Resolve(requirement);

        Assert.False(resolution.IsResolved);
        Assert.Equal(CapabilityGapReason.ProviderNotRegistered, resolution.Gap!.Reason);
        Assert.Equal(CapabilityIds.TextGeneration, resolution.Gap.CapabilityId);
        Assert.Equal(["ollama", "gemini-text", "deepseek", "openai", "gemini-cli"], resolution.Gap.KnownProviderIds);
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
