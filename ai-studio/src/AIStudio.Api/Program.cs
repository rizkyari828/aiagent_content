using AIStudio.Api.ErrorHandling;
using AIStudio.Api.Endpoints;
using AIStudio.Api.Health;
using AIStudio.Infrastructure;
using AIStudio.Infrastructure.Health;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Diagnostics.HealthChecks;

var builder = WebApplication.CreateBuilder(args);

// One bounded upload surface. Import is the only route that accepts a package, so the
// process-wide request-body limit is raised to the configured package bound plus
// multipart overhead instead of relying on Kestrel's 30 MB development default.
var maxTransferBytes = builder.Configuration.GetValue<long?>(
    "ProjectTransfer:MaxPackageBytes") ?? 2L * 1024 * 1024 * 1024;
var maxRequestBytes = maxTransferBytes + (1024 * 1024);
builder.Services.Configure<FormOptions>(options =>
    options.MultipartBodyLengthLimit = maxRequestBytes);
builder.WebHost.ConfigureKestrel(options =>
    options.Limits.MaxRequestBodySize = maxRequestBytes);

builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole(options =>
{
    options.IncludeScopes = true;
    options.TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffZ";
    options.UseUtcTimestamp = true;
});

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services
    .AddHealthChecks()
    .AddCheck(
        "application",
        () => HealthCheckResult.Healthy("Application is running."),
        tags: ["live"])
    .AddCheck<DatabaseHealthCheck>(
        "postgresql",
        failureStatus: HealthStatus.Unhealthy,
        tags: ["ready"],
        timeout: TimeSpan.FromSeconds(5));

var app = builder.Build();

app.UseExceptionHandler();

app.MapGet("/", () => Results.Ok(new
{
    service = "AIStudio.Api",
    status = "running"
}));

app.MapHealthChecks("/health", new HealthCheckOptions
{
    ResponseWriter = HealthResponseWriter.WriteAsync
});

app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = registration => registration.Tags.Contains("live"),
    ResponseWriter = HealthResponseWriter.WriteAsync
});

app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = registration => registration.Tags.Contains("ready"),
    ResponseWriter = HealthResponseWriter.WriteAsync
});

app.MapGenerateIdeaApi();
app.MapContentProjectApi();
app.MapJobCancelApi();
app.MapScriptReviewApi();
app.MapStoryboardApi();
app.MapAssetApi();
app.MapNarrationApi();
app.MapSubtitleApi();
app.MapRenderApi();
app.MapFinalVideoQaApi();
app.MapVisualApi();
app.MapAudioApi();
app.MapIdentityAssetApi();
app.MapFinalApprovalApi();
app.MapPublicationApi();
app.MapTrackingApi();
app.MapProjectTransferApi();

app.Run();

public partial class Program;
