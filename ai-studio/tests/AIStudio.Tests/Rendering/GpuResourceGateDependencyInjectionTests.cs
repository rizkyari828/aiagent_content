using AIStudio.Application.AI;
using AIStudio.Application.Rendering;
using AIStudio.Application.Rendering.Visuals;
using AIStudio.Infrastructure;
using AIStudio.Infrastructure.Gpu;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AIStudio.Tests.Rendering;

public sealed class GpuResourceGateDependencyInjectionTests
{
    [Fact]
    public void InfrastructureResolvesOneSharedSingletonGate()
    {
        using var provider = BuildProvider();

        var first = provider.GetRequiredService<IGpuResourceGate>();
        var second = provider.GetRequiredService<IGpuResourceGate>();

        Assert.IsType<GpuResourceGate>(first);
        Assert.Same(first, second);
    }

    [Fact]
    public void HeavyProvidersResolveWithTheSharedGate()
    {
        using var provider = BuildProvider();

        // Resolution proves the shared gate is wired into every heavy provider.
        Assert.NotNull(provider.GetRequiredService<IAiTextGenerator>());
        Assert.NotNull(provider.GetRequiredService<IImageGenerationProvider>());
        Assert.NotNull(provider.GetRequiredService<IThreeDRenderingProvider>());
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
