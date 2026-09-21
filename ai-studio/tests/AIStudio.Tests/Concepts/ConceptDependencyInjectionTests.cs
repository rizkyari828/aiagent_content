using AIStudio.Application.Concepts;
using AIStudio.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AIStudio.Tests.Concepts;

public sealed class ConceptDependencyInjectionTests
{
    [Fact]
    public void ResolvesSeedRegistryAsSingleton()
    {
        using var provider = BuildProvider();

        var first = provider.GetRequiredService<IConceptRegistry>();
        var second = provider.GetRequiredService<IConceptRegistry>();

        Assert.IsType<ConceptRegistry>(first);
        Assert.Same(first, second);
        Assert.Equal(2, first.Concepts.Count);
    }

    [Fact]
    public void ResolvesSeedConceptAgainstEnabledProviders()
    {
        using var provider = BuildProvider(new Dictionary<string, string?>
        {
            ["SpeechSynthesis:Enabled"] = "true",
            ["MusicGeneration:Enabled"] = "true",
            ["Manim:Enabled"] = "true"
        });

        var registry = provider.GetRequiredService<IConceptRegistry>();
        var resolver = provider.GetRequiredService<IConceptResolver>();

        var resolution = resolver.Resolve(
            registry.GetLatest(new ConceptId("local-ai-tech-explainer")));

        Assert.Equal(ConceptResolutionStatus.Ready, resolution.Status);
    }

    [Fact]
    public void DefaultConfigurationReportsUnsupported()
    {
        using var provider = BuildProvider();

        var registry = provider.GetRequiredService<IConceptRegistry>();
        var resolver = provider.GetRequiredService<IConceptResolver>();

        var resolution = resolver.Resolve(
            registry.GetLatest(new ConceptId("local-ai-tech-explainer")));

        Assert.Equal(ConceptResolutionStatus.Unsupported, resolution.Status);
        Assert.False(resolution.IsProducible);
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
