using AIStudio.Application.Abstractions.Persistence;
using AIStudio.Application.AI;
using AIStudio.Application.Jobs;
using AIStudio.Infrastructure.AI;
using AIStudio.Infrastructure.Jobs;
using AIStudio.Infrastructure.Persistence;
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
