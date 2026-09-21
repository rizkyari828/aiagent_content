using AIStudio.Application.Stories;
using AIStudio.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AIStudio.Tests.Stories;

public sealed class StoryDependencyInjectionTests
{
    [Fact]
    public void ResolvesSeedPatternRegistryAsSingleton()
    {
        using var provider = BuildProvider();

        var first = provider.GetRequiredService<INarrativePatternRegistry>();
        var second = provider.GetRequiredService<INarrativePatternRegistry>();

        Assert.IsType<NarrativePatternRegistry>(first);
        Assert.Same(first, second);
        Assert.Equal(2, first.Patterns.Count);
    }

    [Fact]
    public void ResolvesStoryDirectorAsSingleton()
    {
        using var provider = BuildProvider();

        var first = provider.GetRequiredService<IStoryDirector>();
        var second = provider.GetRequiredService<IStoryDirector>();

        Assert.IsType<StoryDirector>(first);
        Assert.Same(first, second);
    }

    [Fact]
    public void DirectorProducesAPlanFromTheSeededPatterns()
    {
        using var provider = BuildProvider();

        var director = provider.GetRequiredService<IStoryDirector>();
        var plan = director.Direct(StoryTestSupport.Request(targetDuration: 60)).Plan;

        Assert.Equal("problem-solution-short", plan.NarrativePattern.Value);
        Assert.Empty(plan.Validate());
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
