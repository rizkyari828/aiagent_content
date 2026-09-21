using AIStudio.Application.Capabilities;
using AIStudio.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AIStudio.Tests.Capabilities;

public sealed class CapabilityRegistryDependencyInjectionTests
{
    [Fact]
    public void ResolvesOneSingletonRegistry()
    {
        using var provider = BuildProvider();

        var first = provider.GetRequiredService<ICapabilityRegistry>();
        var second = provider.GetRequiredService<ICapabilityRegistry>();

        Assert.IsType<CapabilityRegistry>(first);
        Assert.Same(first, second);
    }

    [Fact]
    public void RegistryReflectsConfiguredProviderEnablement()
    {
        using var provider = BuildProvider(new Dictionary<string, string?>
        {
            ["Manim:Enabled"] = "true",
            ["Blender:Enabled"] = "false",
            ["ComfyUi:Enabled"] = "false",
            ["SpeechSynthesis:Enabled"] = "false",
            ["MusicGeneration:Enabled"] = "false"
        });

        var registry = provider.GetRequiredService<ICapabilityRegistry>();

        // Resolution is metadata-only: it succeeds without any installed runtime.
        Assert.True(registry.Resolve(CapabilityRequirement.For(CapabilityIds.VisualDiagram)).IsResolved);

        var image = registry.Resolve(CapabilityRequirement.For(CapabilityIds.VisualAiImage));
        Assert.False(image.IsResolved);
        Assert.Equal(CapabilityGapReason.ProviderDisabled, image.Gap!.Reason);

        var blur = registry.Resolve(CapabilityRequirement.For(CapabilityIds.VisualThreeD));
        Assert.False(blur.IsResolved);
        Assert.Equal(CapabilityGapReason.ProviderDisabled, blur.Gap!.Reason);
    }

    [Fact]
    public void UnknownCapabilityReportsNotImplementedWithoutAnExternalRuntime()
    {
        using var provider = BuildProvider();

        var registry = provider.GetRequiredService<ICapabilityRegistry>();
        var resolution = registry.Resolve(CapabilityRequirement.For(
            new CapabilityId("visual.image_to_video")));

        Assert.False(resolution.IsResolved);
        Assert.Equal(CapabilityGapReason.CapabilityNotImplemented, resolution.Gap!.Reason);
    }

    private static ServiceProvider BuildProvider(
        Dictionary<string, string?>? overrides = null)
    {
        var values = new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] =
                "Host=127.0.0.1;Database=test;Username=test;Password=test",
            ["Ai:Provider"] = "Ollama",
            ["Ollama:BaseUrl"] = "http://127.0.0.1:11434",
            ["Ollama:DefaultModel"] = "test-model",
            ["Ollama:TimeoutSeconds"] = "30",
            ["GpuResourceGate:Enabled"] = "true",
            ["JobWorker:Enabled"] = "false",
            ["JobWorker:PollInterval"] = "00:00:01",
            ["JobWorker:LeaseDuration"] = "00:02:00"
        };

        if (overrides is not null)
        {
            foreach (var (key, value) in overrides)
            {
                values[key] = value;
            }
        }

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddInfrastructure(configuration);
        return services.BuildServiceProvider();
    }
}
