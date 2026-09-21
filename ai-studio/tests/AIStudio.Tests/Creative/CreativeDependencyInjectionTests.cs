using AIStudio.Application.Creative;
using AIStudio.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AIStudio.Tests.Creative;

public sealed class CreativeDependencyInjectionTests
{
    [Fact]
    public void ResolvesTheCreativeDirectorAndPlanningContext()
    {
        using var provider = BuildProvider();

        Assert.IsType<CreativePlanningContextProvider>(
            provider.GetRequiredService<ICreativePlanningContextProvider>());

        using var scope = provider.CreateScope();
        Assert.IsType<CreativeDirector>(scope.ServiceProvider.GetRequiredService<ICreativeDirector>());
    }

    [Fact]
    public void PlanningContextProjectsTheTrustedRegistriesWithoutProviders()
    {
        using var provider = BuildProvider();

        var context = provider.GetRequiredService<ICreativePlanningContextProvider>().Build();

        Assert.Equal(2, context.Recipes.Count);
        Assert.Contains("visual.still", context.AvailableCapabilities);
        Assert.Contains("speech.narration", context.UnavailableCapabilities);
    }

    private static ServiceProvider BuildProvider()
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

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddInfrastructure(configuration);
        return services.BuildServiceProvider();
    }
}
