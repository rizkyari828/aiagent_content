using AIStudio.Application.Bibles;
using AIStudio.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AIStudio.Tests.Bibles;

public sealed class BibleDependencyInjectionTests
{
    [Fact]
    public void ResolvesEmptyBibleRegistriesAsSingletons()
    {
        using var provider = BuildProvider();

        var characters = provider.GetRequiredService<ICharacterBibleRegistry>();
        var worlds = provider.GetRequiredService<IWorldBibleRegistry>();

        Assert.IsType<CharacterBibleRegistry>(characters);
        Assert.IsType<WorldBibleRegistry>(worlds);
        Assert.Same(characters, provider.GetRequiredService<ICharacterBibleRegistry>());
        Assert.Same(worlds, provider.GetRequiredService<IWorldBibleRegistry>());
        Assert.Empty(characters.Characters);
        Assert.Empty(worlds.Worlds);
    }

    [Fact]
    public void ResolvedRegistriesAcceptTrustedData()
    {
        using var provider = BuildProvider();

        var characters = provider.GetRequiredService<ICharacterBibleRegistry>();
        var worlds = provider.GetRequiredService<IWorldBibleRegistry>();

        characters.Register(BibleTestSupport.Character("rio"));
        worlds.Register(BibleTestSupport.World("rio-bedroom"));

        Assert.True(characters.TryGetLatest(new CharacterBibleId("rio"), out _));
        Assert.True(worlds.TryGetLatest(new WorldBibleId("rio-bedroom"), out _));
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
