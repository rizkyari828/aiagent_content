using AIStudio.Application.Jobs;
using AIStudio.Application.Jobs.GenerateIdea;
using AIStudio.Application.Jobs.GenerateScript;
using AIStudio.Domain.Jobs;

namespace AIStudio.Api.Endpoints;

public static class GenerateIdeaEndpoints
{
    public static IEndpointRouteBuilder MapGenerateIdeaApi(
        this IEndpointRouteBuilder endpoints)
    {
        var api = endpoints.MapGroup("/api");

        api.MapPost("/content-projects", CreateProjectAsync)
            .WithName("CreateContentProject")
            .Produces<ContentProjectResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem();

        api.MapPost(
                "/content-projects/{contentProjectId}/generate-idea-jobs",
                EnqueueGenerateIdeaAsync)
            .WithName("EnqueueGenerateIdea")
            .Produces<EnqueueJobResponse>(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesValidationProblem();

        api.MapPost(
                "/content-projects/{contentProjectId}/generate-script-jobs",
                EnqueueGenerateScriptAsync)
            .WithName("EnqueueGenerateScript")
            .Produces<EnqueueJobResponse>(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesValidationProblem();

        api.MapGet("/jobs/{jobId}", GetJobAsync)
            .WithName("GetJob")
            .Produces<JobResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesValidationProblem();

        return endpoints;
    }

    private static async Task<IResult> CreateProjectAsync(
        CreateContentProjectRequest request,
        GenerateIdeaWorkflow workflow,
        CancellationToken cancellationToken)
    {
        try
        {
            var project = await workflow.CreateProjectAsync(
                request.Title,
                request.Brief,
                cancellationToken);

            return Results.Json(
                new ContentProjectResponse(
                    project.Id,
                    project.Title,
                    project.Brief,
                    "draft"),
                statusCode: StatusCodes.Status201Created);
        }
        catch (ArgumentException exception)
        {
            return InvalidRequest(exception);
        }
    }

    private static async Task<IResult> EnqueueGenerateIdeaAsync(
        string contentProjectId,
        EnqueueGenerateIdeaRequest request,
        GenerateIdeaWorkflow workflow,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(contentProjectId, out var parsedContentProjectId)
            || parsedContentProjectId == Guid.Empty)
        {
            return InvalidIdentifier("contentProjectId");
        }

        try
        {
            var jobId = await workflow.EnqueueAsync(
                parsedContentProjectId,
                request.Topic,
                request.TargetAudience,
                request.Language,
                cancellationToken);

            if (jobId is null)
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status404NotFound,
                    title: "Content project not found.",
                    detail: $"Content project '{parsedContentProjectId}' does not exist.");
            }

            return Results.Accepted(
                $"/api/jobs/{jobId}",
                new EnqueueJobResponse(jobId.Value, "queued"));
        }
        catch (JobExecutionException exception)
            when (exception.ErrorCode == "generate_idea_invalid_payload")
        {
            return Results.ValidationProblem(
                new Dictionary<string, string[]>
                {
                    ["request"] = [exception.Message]
                });
        }
        catch (ArgumentException exception)
        {
            return InvalidRequest(exception);
        }
    }

    private static async Task<IResult> EnqueueGenerateScriptAsync(
        string contentProjectId,
        EnqueueGenerateScriptRequest request,
        GenerateScriptWorkflow workflow,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(contentProjectId, out var parsedContentProjectId)
            || parsedContentProjectId == Guid.Empty)
        {
            return InvalidIdentifier("contentProjectId");
        }

        try
        {
            var jobId = await workflow.EnqueueAsync(
                parsedContentProjectId,
                request.SelectedIdea,
                request.Language,
                cancellationToken);

            if (jobId is null)
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status404NotFound,
                    title: "Content project not found.",
                    detail: $"Content project '{parsedContentProjectId}' does not exist.");
            }

            return Results.Accepted(
                $"/api/jobs/{jobId}",
                new EnqueueJobResponse(jobId.Value, "queued"));
        }
        catch (JobExecutionException exception)
            when (exception.ErrorCode == "generate_script_invalid_payload")
        {
            return Results.ValidationProblem(
                new Dictionary<string, string[]>
                {
                    ["request"] = [exception.Message]
                });
        }
        catch (ArgumentException exception)
        {
            return InvalidRequest(exception);
        }
    }

    private static async Task<IResult> GetJobAsync(
        string jobId,
        GenerateIdeaWorkflow workflow,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(jobId, out var parsedJobId)
            || parsedJobId == Guid.Empty)
        {
            return InvalidIdentifier("jobId");
        }

        try
        {
            var job = await workflow.FindJobAsync(parsedJobId, cancellationToken);
            return job is null
                ? Results.Problem(
                    statusCode: StatusCodes.Status404NotFound,
                    title: "Job not found.",
                    detail: $"Job '{parsedJobId}' does not exist.")
                : Results.Ok(JobResponse.From(job));
        }
        catch (ArgumentException exception)
        {
            return InvalidRequest(exception);
        }
    }

    private static IResult InvalidIdentifier(string fieldName) =>
        Results.ValidationProblem(
            new Dictionary<string, string[]>
            {
                [fieldName] = ["A non-empty GUID is required."]
            });

    private static IResult InvalidRequest(ArgumentException exception) =>
        Results.ValidationProblem(
            new Dictionary<string, string[]>
            {
                [exception.ParamName ?? "request"] = [exception.Message]
            });
}

public sealed record CreateContentProjectRequest(string Title, string? Brief);

public sealed record ContentProjectResponse(
    Guid Id,
    string Title,
    string? Brief,
    string Status);

public sealed record EnqueueGenerateIdeaRequest(
    string Topic,
    string? TargetAudience,
    string? Language);

public sealed record EnqueueGenerateScriptRequest(
    GenerateIdeaResult SelectedIdea,
    string? Language);

public sealed record EnqueueJobResponse(Guid JobId, string Status);

public sealed record JobResponse(
    Guid Id,
    Guid ContentProjectId,
    string Type,
    string Status,
    int RetryCount,
    int MaxRetries,
    object? Result,
    string? ErrorCode,
    string? ErrorSummary,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt)
{
    public static JobResponse From(JobDetails details) =>
        new(
            details.Id,
            details.ContentProjectId,
            details.Type.ToString(),
            ToApiStatus(details.Status),
            details.RetryCount,
            details.MaxRetries,
            details.Result,
            details.ErrorCode,
            details.ErrorSummary,
            details.CreatedAt,
            details.UpdatedAt,
            details.StartedAt,
            details.CompletedAt);

    private static string ToApiStatus(JobStatus status) =>
        status switch
        {
            JobStatus.Queued => "queued",
            JobStatus.Running => "running",
            JobStatus.Succeeded => "completed",
            JobStatus.Failed => "failed",
            JobStatus.Cancelled => "cancelled",
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, null)
        };
}
