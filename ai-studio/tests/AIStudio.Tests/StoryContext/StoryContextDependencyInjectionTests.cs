using AIStudio.Application.Bibles;
using AIStudio.Application.StoryContext;
using AIStudio.Infrastructure;
using AIStudio.Tests.Bibles;
using AIStudio.Tests.Stories;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using StoryContextBuilderType = AIStudio.Application.StoryContext.StoryContextBuilder;
using StoryContextBuilderContract = AIStudio.Application.StoryContext.IStoryContextBuilder;

namespace AIStudio.Tests.StoryContext;

public sealed class StoryContextDependencyInjectionTests
{
    [Fact]
    public void ResolvesBuilderAsSingleton()
    {
        using var provider = BuildProvider();

        var first = provider.GetRequiredService<StoryContextBuilderContract>();
        var second = provider.GetRequiredService<StoryContextBuilderContract>();

        Assert.IsType<StoryContextBuilderType>(first);
        Assert.Same(first, second);
    }

    [Fact]
    public void BuildsFromResolvedRegistries()
    {
        using var provider = BuildProvider();

        var builder = provider.GetRequiredService<StoryContextBuilderContract>();
        var characters = provider.GetRequiredService<ICharacterBibleRegistry>();
        var worlds = provider.GetRequiredService<IWorldBibleRegistry>();

        characters.Register(BibleTestSupport.Character("rio"));
        worlds.Register(BibleTestSupport.World("bedroom"));

        var plan = StoryTestSupport.Plan(
            [
                StoryTestSupport.Beat("beat-01", 1, role: "hook", duration: 60,
                    characterRefs: ["rio"], worldRefs: ["bedroom"])
            ],
            targetDuration: 60);

        var result = builder.Build(StoryContextTestSupport.Request(plan: plan));

        Assert.True(result.IsValid);
        Assert.Equal(["rio"], result.Context.Characters.Select(character => character.Id.Value));
        Assert.Equal(["bedroom"], result.Context.Worlds.Select(world => world.Id.Value));
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
