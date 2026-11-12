using System.Text.Json;
using AIStudio.Api.Endpoints;
using AIStudio.Application.Capabilities;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Xunit;

namespace AIStudio.Tests.Api;

public sealed class CapabilityApiContractTests
{
    [Fact]
    public void GetCapabilities_ReturnsOkWithDeterministicOrder()
    {
        var providers = ProductionCapabilityCatalog.CreateProviders(
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
            geminiSpeechEnabled: true);
        var registry = new CapabilityRegistry(ProductionCapabilityCatalog.Capabilities, providers);

        var result = CapabilityEndpoints.GetCapabilities(registry);

        var ok = Assert.IsType<Ok<CapabilitiesResponse>>(result);
        Assert.Equal(StatusCodes.Status200OK, ok.StatusCode);
        Assert.NotNull(ok.Value);
        Assert.NotEmpty(ok.Value.Capabilities);

        var ids = ok.Value.Capabilities.Select(c => c.Id).ToList();

        // Verify deterministic sorting (ordinal comparison)
        var expectedSortedIds = ids.OrderBy(id => id, StringComparer.Ordinal).ToList();
        Assert.Equal(expectedSortedIds, ids);

        // Core required capabilities are all present
        Assert.Contains("text.generation", ids);
        Assert.Contains("visual.ai_image", ids);
        Assert.Contains("visual.character_animation", ids);
        Assert.Contains("speech.narration", ids);
        Assert.Contains("music.instrumental", ids);
        Assert.Contains("visual.diagram", ids);
        Assert.Contains("visual.still", ids);
        Assert.Contains("visual.three_d", ids);
        Assert.Contains("visual.ui_motion", ids);
        Assert.Contains("media.compose", ids);
        Assert.Contains("subtitle.burned", ids);
    }

    [Fact]
    public void GetCapabilities_IncludesLocalAndCloudProvidersWithAccurateEnabledFlags()
    {
        // Configure known mix of local and cloud providers with distinct Enabled flags
        var providers = ProductionCapabilityCatalog.CreateProviders(
            speechEnabled: true,
            musicEnabled: true,
            manimEnabled: true,
            imageEnabled: false,
            threeDEnabled: true,
            videoEnabled: true,
            textEnabled: true,
            geminiTextEnabled: false,
            geminiImageEnabled: true,
            geminiVideoEnabled: false,
            geminiSpeechEnabled: true);
        var registry = new CapabilityRegistry(ProductionCapabilityCatalog.Capabilities, providers);

        var result = CapabilityEndpoints.GetCapabilities(registry);

        var ok = Assert.IsType<Ok<CapabilitiesResponse>>(result);
        var capabilities = ok.Value!.Capabilities.ToDictionary(c => c.Id);

        // text.generation: local enabled (ollama), cloud disabled (gemini-text, deepseek, openai)
        Assert.True(capabilities.TryGetValue("text.generation", out var textCap));
        Assert.Equal(4, textCap.Providers.Count);
        Assert.Equal("ollama", textCap.Providers[0].ProviderId);
        Assert.Equal(100, textCap.Providers[0].Priority);
        Assert.True(textCap.Providers[0].Enabled);
        Assert.Equal("local_service_gpu", textCap.Providers[0].RuntimeCategory);
        Assert.Equal("gemini-text", textCap.Providers[1].ProviderId);
        Assert.Equal(50, textCap.Providers[1].Priority);
        Assert.False(textCap.Providers[1].Enabled);
        Assert.Equal("cloud_api", textCap.Providers[1].RuntimeCategory);
        Assert.Equal("deepseek", textCap.Providers[2].ProviderId);
        Assert.Equal(45, textCap.Providers[2].Priority);
        Assert.False(textCap.Providers[2].Enabled);
        Assert.Equal("cloud_api", textCap.Providers[2].RuntimeCategory);
        Assert.Equal("openai", textCap.Providers[3].ProviderId);
        Assert.Equal(40, textCap.Providers[3].Priority);
        Assert.False(textCap.Providers[3].Enabled);
        Assert.Equal("cloud_api", textCap.Providers[3].RuntimeCategory);

        // visual.ai_image: local disabled (comfyui-flux), cloud enabled (gemini-nanobanana)
        Assert.True(capabilities.TryGetValue("visual.ai_image", out var imageCap));
        Assert.Equal(2, imageCap.Providers.Count);
        Assert.Equal("comfyui-flux", imageCap.Providers[0].ProviderId);
        Assert.Equal(100, imageCap.Providers[0].Priority);
        Assert.False(imageCap.Providers[0].Enabled);
        Assert.Equal("local_service_gpu", imageCap.Providers[0].RuntimeCategory);
        Assert.Equal("gemini-nanobanana", imageCap.Providers[1].ProviderId);
        Assert.Equal(50, imageCap.Providers[1].Priority);
        Assert.True(imageCap.Providers[1].Enabled);
        Assert.Equal("cloud_api", imageCap.Providers[1].RuntimeCategory);

        // visual.character_animation: local enabled (comfyui-i2v), cloud disabled (gemini-veo)
        Assert.True(capabilities.TryGetValue("visual.character_animation", out var animCap));
        Assert.Equal(2, animCap.Providers.Count);
        Assert.Equal("comfyui-i2v", animCap.Providers[0].ProviderId);
        Assert.Equal(100, animCap.Providers[0].Priority);
        Assert.True(animCap.Providers[0].Enabled);
        Assert.Equal("local_service_gpu", animCap.Providers[0].RuntimeCategory);
        Assert.Equal("gemini-veo", animCap.Providers[1].ProviderId);
        Assert.Equal(50, animCap.Providers[1].Priority);
        Assert.False(animCap.Providers[1].Enabled);
        Assert.Equal("cloud_api", animCap.Providers[1].RuntimeCategory);

        // speech.narration: local enabled (voxcpm2), cloud enabled (gemini-tts)
        Assert.True(capabilities.TryGetValue("speech.narration", out var speechCap));
        Assert.Equal(2, speechCap.Providers.Count);
        Assert.Equal("voxcpm2", speechCap.Providers[0].ProviderId);
        Assert.Equal(100, speechCap.Providers[0].Priority);
        Assert.True(speechCap.Providers[0].Enabled);
        Assert.Equal("local_python_gpu", speechCap.Providers[0].RuntimeCategory);
        Assert.Equal("gemini-tts", speechCap.Providers[1].ProviderId);
        Assert.Equal(50, speechCap.Providers[1].Priority);
        Assert.True(speechCap.Providers[1].Enabled);
        Assert.Equal("cloud_api", speechCap.Providers[1].RuntimeCategory);

        // music.instrumental: local enabled (ace-step)
        Assert.True(capabilities.TryGetValue("music.instrumental", out var musicCap));
        Assert.Single(musicCap.Providers);
        Assert.Equal("ace-step", musicCap.Providers[0].ProviderId);
        Assert.Equal(100, musicCap.Providers[0].Priority);
        Assert.True(musicCap.Providers[0].Enabled);
        Assert.Equal("local_python_gpu", musicCap.Providers[0].RuntimeCategory);
    }

    [Fact]
    public void GetCapabilities_PayloadContainsNoSlashPathOrSecretFields()
    {
        var providers = ProductionCapabilityCatalog.CreateProviders(
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
            geminiSpeechEnabled: true);
        var registry = new CapabilityRegistry(ProductionCapabilityCatalog.Capabilities, providers);

        var result = CapabilityEndpoints.GetCapabilities(registry);

        var ok = Assert.IsType<Ok<CapabilitiesResponse>>(result);
        var json = Serialize(ok.Value);

        Assert.True(json.TryGetProperty("capabilities", out var capabilitiesElement));
        Assert.Equal(JsonValueKind.Array, capabilitiesElement.ValueKind);

        var sensitiveKeywords = new[]
        {
            "path", "file", "secret", "token", "key", "url", "uri", "connection", "password"
        };

        var allowedCapabilityProperties = new HashSet<string>(StringComparer.Ordinal)
        {
            "id", "displayName", "category", "providers"
        };

        var allowedProviderProperties = new HashSet<string>(StringComparer.Ordinal)
        {
            "providerId", "capabilityId", "priority", "enabled", "runtimeCategory"
        };

        foreach (var cap in capabilitiesElement.EnumerateArray())
        {
            foreach (var prop in cap.EnumerateObject())
            {
                Assert.Contains(prop.Name, allowedCapabilityProperties);
                foreach (var keyword in sensitiveKeywords)
                {
                    Assert.DoesNotContain(keyword, prop.Name, StringComparison.OrdinalIgnoreCase);
                }

                if (prop.Value.ValueKind == JsonValueKind.String)
                {
                    var val = prop.Value.GetString()!;
                    Assert.DoesNotContain("/", val);
                    Assert.DoesNotContain("\\", val);
                }
            }

            Assert.True(cap.TryGetProperty("providers", out var providersElement));
            Assert.Equal(JsonValueKind.Array, providersElement.ValueKind);

            foreach (var prov in providersElement.EnumerateArray())
            {
                foreach (var prop in prov.EnumerateObject())
                {
                    Assert.Contains(prop.Name, allowedProviderProperties);
                    foreach (var keyword in sensitiveKeywords)
                    {
                        Assert.DoesNotContain(keyword, prop.Name, StringComparison.OrdinalIgnoreCase);
                    }

                    if (prop.Value.ValueKind == JsonValueKind.String)
                    {
                        var val = prop.Value.GetString()!;
                        Assert.DoesNotContain("/", val);
                        Assert.DoesNotContain("\\", val);
                    }
                }
            }
        }
    }

    [Fact]
    public void GetCapabilities_ThrowsOnNullRegistry()
    {
        Assert.Throws<ArgumentNullException>(() => CapabilityEndpoints.GetCapabilities(null!));
    }

    private static JsonElement Serialize<T>(T value) =>
        JsonSerializer.SerializeToElement(
            value,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
}
