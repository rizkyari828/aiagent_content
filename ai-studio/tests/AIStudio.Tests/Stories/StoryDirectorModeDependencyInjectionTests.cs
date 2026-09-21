using AIStudio.Application.Stories;
using AIStudio.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace AIStudio.Tests.Stories;

public sealed class StoryDirectorModeDependencyInjectionTests
{
    [Fact]
    public void DefaultsToTheDeterministicDirector()
    {
        using var provider = BuildProvider(mode: null);

        Assert.IsType<StoryDirector>(provider.GetRequiredService<IStoryDirector>());
        Assert.Equal(
            StoryDirectorOptions.DeterministicMode,
            provider.GetRequiredService<IOptions<StoryDirectorOptions>>().Value.Mode);
    }

    [Fact]
    public void QwenModeResolvesTheQwenBackedDirector()
    {
        using var provider = BuildProvider(StoryDirectorOptions.QwenMode);

        Assert.IsType<QwenStoryDirector>(provider.GetRequiredService<IStoryDirector>());
    }

    [Fact]
    public void InvalidModeFailsClearly()
    {
        using var provider = BuildProvider("bogus");

        var exception = Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<IStoryDirector>());

        Assert.Contains("StoryDirector:Mode", exception.Message);
    }

    [Fact]
    public void ResolvesExactlyOneImplementationPerConfiguration()
    {
        using var provider = BuildProvider(StoryDirectorOptions.QwenMode);

        var first = provider.GetRequiredService<IStoryDirector>();
        var second = provider.GetRequiredService<IStoryDirector>();

        Assert.IsType<QwenStoryDirector>(first);
        Assert.Same(first, second);
    }

    private static ServiceProvider BuildProvider(string? mode)
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

        if (mode is not null)
        {
            values["StoryDirector:Mode"] = mode;
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
