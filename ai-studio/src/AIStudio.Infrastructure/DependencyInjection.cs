using AIStudio.Application.Abstractions.Persistence;
using AIStudio.Application.AI;
using AIStudio.Application.Assets;
using AIStudio.Application.Content;
using AIStudio.Application.Jobs;
using AIStudio.Application.Jobs.FinalVideoQa;
using AIStudio.Application.Jobs.GenerateIdea;
using AIStudio.Application.Jobs.GenerateScript;
using AIStudio.Application.Jobs.GenerateStoryboard;
using AIStudio.Application.Jobs.RenderVideo;
using AIStudio.Application.Narration;
using AIStudio.Application.Rendering;
using AIStudio.Application.Jobs.GenerateSceneVisuals;
using AIStudio.Application.Rendering.Visuals;
using AIStudio.Application.Scripts;
using AIStudio.Application.Subtitles;
using AIStudio.Infrastructure.AI;
using AIStudio.Infrastructure.Assets;
using AIStudio.Infrastructure.Content;
using AIStudio.Infrastructure.Jobs;
using AIStudio.Infrastructure.Narration;
using AIStudio.Infrastructure.Persistence;
using AIStudio.Infrastructure.Rendering;
using AIStudio.Infrastructure.Scripts;
using AIStudio.Infrastructure.Subtitles;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Npgsql;

namespace AIStudio.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection");
        ValidateConnectionString(connectionString);

        services.AddDbContextFactory<ApplicationDbContext>(options =>
            options.UseNpgsql(connectionString));

        services.AddScoped<IApplicationDbContext>(serviceProvider =>
            serviceProvider.GetRequiredService<ApplicationDbContext>());

        AddJobWorker(services, configuration);
        AddAiGateway(services, configuration);
        AddAssetStorage(services, configuration);
        AddRendering(services, configuration);

        return services;
    }

    private static void AddJobWorker(
        IServiceCollection services,
        IConfiguration configuration)
    {
        services
            .AddOptions<JobWorkerOptions>()
            .Bind(configuration.GetSection(JobWorkerOptions.SectionName))
            .Validate(
                options => options.PollInterval > TimeSpan.Zero,
                "JobWorker:PollInterval must be greater than zero.")
            .Validate(
                options => options.LeaseDuration >= TimeSpan.FromSeconds(3),
                "JobWorker:LeaseDuration must be at least three seconds.")
            .ValidateOnStart();

        services.AddSingleton(TimeProvider.System);
        services.AddScoped<IJobQueue, PostgreSqlJobQueue>();
        services.AddScoped<IContentProjectReader, ContentProjectReader>();
        services.AddScoped<IJobReader, JobReader>();
        services.AddScoped<IScriptReviewRepository, ScriptReviewRepository>();
        services.AddScoped<IAssetRepository, AssetRepository>();
        services.AddScoped<INarrationRepository, NarrationRepository>();
        services.AddScoped<ISubtitleRepository, SubtitleRepository>();
        services.AddSingleton<IAssetFileStore, LocalAssetFileStore>();
        services.AddSingleton<IProcessRunner, SystemProcessRunner>();
        services.AddSingleton<IMediaInspector, FfprobeMediaInspector>();
        services.AddSingleton<IVideoRenderer, FfmpegVideoRenderer>();
        services.AddSingleton<ISceneVisualRenderer, FfmpegSceneVisualRenderer>();
        services.AddSingleton<IManimSceneRenderer, ProcessManimSceneRenderer>();
        services.AddScoped<IJobHandler, GenerateIdeaJobHandler>();
        services.AddScoped<IJobHandler, GenerateScriptJobHandler>();
        services.AddScoped<IJobHandler, GenerateStoryboardJobHandler>();
        services.AddScoped<IJobHandler, RenderVideoJobHandler>();
        services.AddScoped<IJobHandler, FinalVideoQaJobHandler>();
        services.AddScoped<IJobHandler, GenerateSceneVisualsJobHandler>();
        services.AddScoped<GenerateIdeaWorkflow>();
        services.AddScoped<GenerateScriptWorkflow>();
        services.AddScoped<GenerateStoryboardWorkflow>();
        services.AddScoped<ScriptReviewWorkflow>();
        services.AddScoped<AssetCollectionWorkflow>();
        services.AddScoped<NarrationWorkflow>();
        services.AddScoped<SubtitleWorkflow>();
        services.AddScoped<RenderVideoWorkflow>();
        services.AddScoped<FinalVideoQaWorkflow>();
        services.AddScoped<GenerateSceneVisualsWorkflow>();
        services.AddSingleton<IJobHandler, PlaceholderJobHandler>();
        services.AddScoped<JobProcessor>();
        services.AddHostedService<JobWorker>();
    }

    private static void AddAiGateway(
        IServiceCollection services,
        IConfiguration configuration)
    {
        services
            .AddOptions<AiOptions>()
            .Bind(configuration.GetSection(AiOptions.SectionName))
            .Validate(
                options => string.Equals(
                    options.Provider,
                    "Ollama",
                    StringComparison.OrdinalIgnoreCase),
                "Ai:Provider must be 'Ollama'.")
            .ValidateOnStart();

        services
            .AddOptions<OllamaOptions>()
            .Bind(configuration.GetSection(OllamaOptions.SectionName))
            .Validate(
                options => IsValidBaseUrl(options.BaseUrl),
                "Ollama:BaseUrl must be an absolute HTTP or HTTPS URL.")
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.DefaultModel),
                "Ollama:DefaultModel is required.")
            .Validate(
                options => options.TimeoutSeconds is >= 1 and <= 600,
                "Ollama:TimeoutSeconds must be between 1 and 600.")
            .ValidateOnStart();

        services.AddHttpClient<IAiTextGenerator, OllamaTextGenerator>(
            (serviceProvider, client) =>
            {
                var options = serviceProvider
                    .GetRequiredService<IOptions<OllamaOptions>>()
                    .Value;
                client.BaseAddress = new Uri(
                    $"{options.BaseUrl.TrimEnd('/')}/",
                    UriKind.Absolute);
                client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
            });
    }

    private static void AddAssetStorage(
        IServiceCollection services,
        IConfiguration configuration)
    {
        services
            .AddOptions<AssetStorageOptions>()
            .Bind(configuration.GetSection(AssetStorageOptions.SectionName))
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.RootPath),
                "Assets:RootPath is required.")
            .ValidateOnStart();
    }

    private static void AddRendering(
        IServiceCollection services,
        IConfiguration configuration)
    {
        services
            .AddOptions<RenderingOptions>()
            .Bind(configuration.GetSection(RenderingOptions.SectionName))
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.FfmpegPath),
                "Rendering:FfmpegPath is required.")
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.FfprobePath),
                "Rendering:FfprobePath is required.")
            .Validate(
                options => options.TimeoutSeconds is >= 1 and <= 3600,
                "Rendering:TimeoutSeconds must be between 1 and 3600.")
            .Validate(
                options => options.Width is >= 2 and <= 7680,
                "Rendering:Width must be between 2 and 7680.")
            .Validate(
                options => options.Height is >= 2 and <= 4320,
                "Rendering:Height must be between 2 and 4320.")
            .Validate(
                options => options.FrameRate is >= 1 and <= 120,
                "Rendering:FrameRate must be between 1 and 120.")
            .ValidateOnStart();

        services
            .AddOptions<ManimOptions>()
            .Bind(configuration.GetSection(ManimOptions.SectionName))
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.PythonPath),
                "Manim:PythonPath is required.")
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.ScriptPath),
                "Manim:ScriptPath is required.")
            .Validate(
                options => options.TimeoutSeconds is >= 1 and <= 3600,
                "Manim:TimeoutSeconds must be between 1 and 3600.")
            .ValidateOnStart();
    }

    private static bool IsValidBaseUrl(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    private static void ValidateConnectionString(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "Connection string 'DefaultConnection' is required. " +
                "Set ConnectionStrings__DefaultConnection in the environment.");
        }

        NpgsqlConnectionStringBuilder parsed;

        try
        {
            parsed = new NpgsqlConnectionStringBuilder(connectionString);
        }
        catch (ArgumentException exception)
        {
            throw new InvalidOperationException(
                "Connection string 'DefaultConnection' is invalid.",
                exception);
        }

        if (string.IsNullOrWhiteSpace(parsed.Host)
            || string.IsNullOrWhiteSpace(parsed.Database)
            || string.IsNullOrWhiteSpace(parsed.Username))
        {
            throw new InvalidOperationException(
                "Connection string 'DefaultConnection' must include Host, Database, and Username.");
        }
    }
}
