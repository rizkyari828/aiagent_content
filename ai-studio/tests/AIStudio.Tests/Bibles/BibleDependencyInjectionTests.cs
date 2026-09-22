using AIStudio.Application.Bibles;
using AIStudio.Application.IdentityAssets;
using AIStudio.Infrastructure.Assets;
using AIStudio.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AIStudio.Tests.Bibles;

public sealed class BibleDependencyInjectionTests : IDisposable
{
    private readonly string assetsRoot = Path.Combine(
        Path.GetTempPath(),
        "aistudio-bible-di-tests",
        Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(assetsRoot))
        {
            Directory.Delete(assetsRoot, recursive: true);
        }
    }

    [Fact]
    public void ResolvesEmptyBibleRegistriesAsSingletons()
    {
        using var provider = BuildProvider();

        var characters = provider.GetRequiredService<ICharacterBibleRegistry>();
        var worlds = provider.GetRequiredService<IWorldBibleRegistry>();
        var identityAssets = provider.GetRequiredService<IIdentityAssetRegistry>();
        var resolver = provider.GetRequiredService<IIdentityAssetResolver>();
        var store = provider.GetRequiredService<IIdentityAssetStore>();

        Assert.IsType<CharacterBibleRegistry>(characters);
        Assert.IsType<WorldBibleRegistry>(worlds);
        Assert.IsType<IdentityAssetRegistry>(identityAssets);
        Assert.IsType<IdentityAssetResolver>(resolver);
        Assert.IsType<LocalIdentityAssetStore>(store);
        Assert.Same(characters, provider.GetRequiredService<ICharacterBibleRegistry>());
        Assert.Same(worlds, provider.GetRequiredService<IWorldBibleRegistry>());
        Assert.Same(identityAssets, provider.GetRequiredService<IIdentityAssetRegistry>());
        Assert.Same(resolver, provider.GetRequiredService<IIdentityAssetResolver>());
        Assert.Same(store, provider.GetRequiredService<IIdentityAssetStore>());
        Assert.Empty(characters.Characters);
        Assert.Empty(worlds.Worlds);
        Assert.Empty(identityAssets.Assets);
    }

    [Fact]
    public void ResolvesIdentityAssetAuthoringWorkflows()
    {
        using var provider = BuildProvider();

        Assert.IsType<ImportIdentityAssetWorkflow>(
            provider.GetRequiredService<ImportIdentityAssetWorkflow>());
        Assert.IsType<ApproveIdentityAssetWorkflow>(
            provider.GetRequiredService<ApproveIdentityAssetWorkflow>());
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

    private ServiceProvider BuildProvider()
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
            ["Assets:RootPath"] = assetsRoot,
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
