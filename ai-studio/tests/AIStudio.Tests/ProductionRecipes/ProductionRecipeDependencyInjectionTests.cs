using AIStudio.Application.ProductionRecipes;
using AIStudio.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AIStudio.Tests.ProductionRecipes;

public sealed class ProductionRecipeDependencyInjectionTests
{
    [Fact]
    public void ResolvesSeedRegistryAsSingleton()
    {
        using var provider = BuildProvider();

        var first = provider.GetRequiredService<IProductionRecipeRegistry>();
        var second = provider.GetRequiredService<IProductionRecipeRegistry>();

        Assert.IsType<ProductionRecipeRegistry>(first);
        Assert.Same(first, second);
        Assert.Equal(2, first.Recipes.Count);
    }

    [Fact]
    public void ResolvesRecipeAgainstConfiguredProviders()
    {
        using var provider = BuildProvider(new Dictionary<string, string?>
        {
            ["SpeechSynthesis:Enabled"] = "true",
            ["MusicGeneration:Enabled"] = "true",
            ["Manim:Enabled"] = "true"
        });

        var registry = provider.GetRequiredService<IProductionRecipeRegistry>();
        var resolver = provider.GetRequiredService<IProductionRecipeResolver>();
        var recipe = registry.GetLatest(new ProductionRecipeId("tech-explainer"));

        var resolution = resolver.Resolve(recipe);

        Assert.Equal(ProductionRecipeStatus.FullySupported, resolution.Status);
    }

    [Fact]
    public void DefaultConfigurationReportsDisabledProviderGaps()
    {
        using var provider = BuildProvider();

        var registry = provider.GetRequiredService<IProductionRecipeRegistry>();
        var resolver = provider.GetRequiredService<IProductionRecipeResolver>();
        var recipe = registry.GetLatest(new ProductionRecipeId("tech-explainer"));

        var resolution = resolver.Resolve(recipe);

        Assert.False(resolution.IsProducible);
        Assert.NotEmpty(resolution.Gaps);
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
