using AIStudio.Application.Jobs;
using AIStudio.Application.Jobs.GenerateIdea;
using AIStudio.Application.Jobs.GenerateScript;
using AIStudio.Application.Jobs.GenerateStoryboard;
using AIStudio.Application.Scripts;
using AIStudio.Domain.Jobs;
using AIStudio.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AIStudio.Tests.Jobs;

public sealed class GenerateIdeaDependencyInjectionTests
{
    [Fact]
    public void Infrastructure_RegistersExactlyOneGenerateIdeaHandler()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] =
                    "Host=127.0.0.1;Database=test;Username=test;Password=test",
                ["Ai:Provider"] = "Ollama",
                ["Ollama:BaseUrl"] = "http://127.0.0.1:11434",
                ["Ollama:DefaultModel"] = "configured-model",
                ["Ollama:TimeoutSeconds"] = "30",
                ["JobWorker:Enabled"] = "false",
                ["JobWorker:PollInterval"] = "00:00:01",
                ["JobWorker:LeaseDuration"] = "00:02:00"
            })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddInfrastructure(configuration);
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });
        using var scope = provider.CreateScope();

        var handlers = scope.ServiceProvider
            .GetServices<IJobHandler>()
            .Where(handler => handler.CanHandle(JobType.GenerateIdea))
            .ToArray();

        var handler = Assert.Single(handlers);
        Assert.IsType<GenerateIdeaJobHandler>(handler);
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IJobReader>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IScriptReviewRepository>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<ScriptReviewWorkflow>());
        Assert.NotNull(
            scope.ServiceProvider.GetRequiredService<GenerateIdeaWorkflow>());
    }

    [Fact]
    public void Infrastructure_RegistersExactlyOneGenerateStoryboardHandler()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] =
                    "Host=127.0.0.1;Database=test;Username=test;Password=test",
                ["Ai:Provider"] = "Ollama",
                ["Ollama:BaseUrl"] = "http://127.0.0.1:11434",
                ["Ollama:DefaultModel"] = "configured-model",
                ["Ollama:TimeoutSeconds"] = "30",
                ["JobWorker:Enabled"] = "false",
                ["JobWorker:PollInterval"] = "00:00:01",
                ["JobWorker:LeaseDuration"] = "00:02:00"
            })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddInfrastructure(configuration);
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });
        using var scope = provider.CreateScope();

        var handlers = scope.ServiceProvider
            .GetServices<IJobHandler>()
            .Where(handler => handler.CanHandle(JobType.GenerateStoryboard))
            .ToArray();

        var handler = Assert.Single(handlers);
        Assert.IsType<GenerateStoryboardJobHandler>(handler);
        Assert.NotNull(
            scope.ServiceProvider.GetRequiredService<GenerateStoryboardWorkflow>());
    }
}
