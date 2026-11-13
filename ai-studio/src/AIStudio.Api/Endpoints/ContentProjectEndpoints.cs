using System.Text.Json;
using System.Text.Json.Serialization;
using AIStudio.Application.Content;
using AIStudio.Application.Jobs;

namespace AIStudio.Api.Endpoints;

/// <summary>
/// Content-project read/list/update plus computed readiness and the single guarded
/// <c>ReadyToPublish</c> transition. Transport only: validation, readiness evidence,
/// and transition gating live in the Application workflows.
/// </summary>
public static class ContentProjectEndpoints
{
    internal const string InvalidRequestCode = "content_project_invalid_request";

    public static IEndpointRouteBuilder MapContentProjectApi(
        this IEndpointRouteBuilder endpoints)
    {
        var projects = endpoints.MapGroup("/api/content-projects");

        projects.MapGet("", ListAsync)
            .WithName("ListContentProjects")
            .Produces<ContentProjectSummaryResponse[]>()
            .ProducesValidationProblem();

        projects.MapGet("/{contentProjectId}", GetAsync)
            .WithName("GetContentProject")
            .Produces<ContentProjectDetailResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesValidationProblem();

        projects.MapPut("/{contentProjectId}", UpdateAsync)
            .WithName("UpdateContentProject")
            .Produces<ContentProjectDetailResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesValidationProblem();

        projects.MapGet("/{contentProjectId}/readiness", GetReadinessAsync)
            .WithName("GetContentReadiness")
            .Produces<ContentReadinessResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesValidationProblem();

        projects.MapPost("/{contentProjectId}/transitions", TransitionAsync)
            .WithName("TransitionContentProject")
            .Produces<ContentProjectTransitionResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesValidationProblem();

        return endpoints;
    }

    internal static async Task<IResult> ListAsync(
        int? take,
        ContentProjectWorkflow workflow,
        CancellationToken cancellationToken)
    {
        try
        {
            var items = await workflow.ListAsync(
                take ?? ContentProjectWorkflow.DefaultTake,
                cancellationToken);
            return Results.Ok(items.Select(ContentProjectSummaryResponse.From).ToArray());
        }
        catch (ArgumentOutOfRangeException exception)
        {
            return InvalidRequest(exception);
        }
    }

    internal static async Task<IResult> GetAsync(
        string contentProjectId,
        ContentProjectWorkflow workflow,
        CancellationToken cancellationToken)
    {
        if (!TryParseIdentifier(contentProjectId, out var projectId))
        {
            return InvalidIdentifier("contentProjectId");
        }

        var details = await workflow.FindAsync(projectId, cancellationToken);
        return details is null
            ? ContentProjectNotFound(projectId)
            : Results.Ok(ContentProjectDetailResponse.From(details));
    }

    internal static async Task<IResult> UpdateAsync(
        string contentProjectId,
        UpdateContentProjectRequest request,
        ContentProjectWorkflow workflow,
        CancellationToken cancellationToken)
    {
        if (!TryParseIdentifier(contentProjectId, out var projectId))
        {
            return InvalidIdentifier("contentProjectId");
        }

        if (request.AdditionalProperties is { Count: > 0 } unknown)
        {
            return UnknownFields(unknown.Keys);
        }

        try
        {
            var details = await workflow.UpdateAsync(
                projectId,
                request.Title!,
                request.Brief,
                cancellationToken);

            return details is null
                ? ContentProjectNotFound(projectId)
                : Results.Ok(ContentProjectDetailResponse.From(details));
        }
        catch (ArgumentException exception)
        {
            return InvalidRequest(exception);
        }
    }

    internal static async Task<IResult> GetReadinessAsync(
        string contentProjectId,
        ContentReadinessWorkflow workflow,
        CancellationToken cancellationToken)
    {
        if (!TryParseIdentifier(contentProjectId, out var projectId))
        {
            return InvalidIdentifier("contentProjectId");
        }

        var readiness = await workflow.EvaluateAsync(projectId, cancellationToken);
        return readiness is null
            ? ContentProjectNotFound(projectId)
            : Results.Ok(ContentReadinessResponse.From(readiness));
    }

    internal static async Task<IResult> TransitionAsync(
        string contentProjectId,
        TransitionContentProjectRequest request,
        ContentReadinessWorkflow workflow,
        CancellationToken cancellationToken)
    {
        if (!TryParseIdentifier(contentProjectId, out var projectId))
        {
            return InvalidIdentifier("contentProjectId");
        }

        var isReadyToPublish = IsTarget(request.To, "ready_to_publish");
        var isPublished = IsTarget(request.To, "published");

        if (!isReadyToPublish && !isPublished)
        {
            return Results.ValidationProblem(
                new Dictionary<string, string[]>
                {
                    ["to"] = ["Only explicit 'ready_to_publish' and 'published' transitions are supported."]
                },
                extensions: new Dictionary<string, object?>
                {
                    ["code"] = "content_project_transition_not_supported"
                });
        }

        try
        {
            var result = isReadyToPublish
                ? await workflow.MarkReadyToPublishAsync(projectId, cancellationToken)
                : await workflow.MarkPublishedAsync(projectId, cancellationToken);
            return result is null
                ? ContentProjectNotFound(projectId)
                : Results.Ok(ContentProjectTransitionResponse.From(result));
        }
        catch (ContentProjectException exception)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Content project transition rejected.",
                detail: exception.Message,
                extensions: new Dictionary<string, object?>
                {
                    ["code"] = exception.ErrorCode,
                    ["reasons"] = exception.ReasonCodes
                });
        }
    }

    internal static IResult ContentProjectNotFound(Guid projectId) =>
        Results.Problem(
            statusCode: StatusCodes.Status404NotFound,
            title: "Content project not found.",
            detail: $"Content project '{projectId}' does not exist.",
            extensions: new Dictionary<string, object?>
            {
                ["code"] = "content_project_not_found"
            });

    internal static IResult InvalidIdentifier(string fieldName) =>
        Results.ValidationProblem(
            new Dictionary<string, string[]>
            {
                [fieldName] = ["A non-empty GUID is required."]
            },
            extensions: new Dictionary<string, object?>
            {
                ["code"] = InvalidRequestCode
            });

    internal static IResult InvalidRequest(ArgumentException exception) =>
        Results.ValidationProblem(
            new Dictionary<string, string[]>
            {
                [exception.ParamName ?? "request"] = [exception.Message]
            },
            extensions: new Dictionary<string, object?>
            {
                ["code"] = InvalidRequestCode
            });

    private static IResult UnknownFields(IEnumerable<string> fieldNames) =>
        Results.ValidationProblem(
            fieldNames.ToDictionary(
                fieldName => fieldName,
                _ => new[] { "This field is not editable through the project update." },
                StringComparer.Ordinal),
            extensions: new Dictionary<string, object?>
            {
                ["code"] = InvalidRequestCode
            });

    private static bool IsTarget(string? target, string expected) =>
        string.Equals(
            target?.Trim(),
            expected,
            StringComparison.OrdinalIgnoreCase);

    private static bool TryParseIdentifier(string value, out Guid id) =>
        Guid.TryParse(value, out id) && id != Guid.Empty;
}

/// <summary>
/// Full replacement of the editable project fields. Any other member — including
/// immutable data such as <c>id</c>, <c>status</c>, or timestamps — is captured by
/// <see cref="AdditionalProperties"/> and rejected instead of being silently ignored.
/// </summary>
public sealed class UpdateContentProjectRequest
{
    public string? Title { get; init; }

    public string? Brief { get; init; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalProperties { get; init; }
}

public sealed record TransitionContentProjectRequest(string? To);

public sealed record ContentProjectSummaryResponse(
    Guid Id,
    string Title,
    string Status,
    DateTimeOffset UpdatedAt)
{
    public static ContentProjectSummaryResponse From(ContentProjectSummary summary) =>
        new(
            summary.Id,
            summary.Title,
            ApiStatus.ToApiStatus(summary.Status),
            summary.UpdatedAt);
}

public sealed record ContentProjectLastJobResponse(
    Guid Id,
    string Type,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? CompletedAt)
{
    public static ContentProjectLastJobResponse From(ProjectJobSummary summary) =>
        new(
            summary.Id,
            summary.Type.ToString(),
            ApiStatus.ToApiStatus(summary.Status),
            summary.CreatedAt,
            summary.UpdatedAt,
            summary.CompletedAt);
}

public sealed record ContentProjectDetailResponse(
    Guid Id,
    string Title,
    string? Brief,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    ContentProjectLastJobResponse? LastJob)
{
    public static ContentProjectDetailResponse From(ContentProjectDetails details) =>
        new(
            details.Id,
            details.Title,
            details.Brief,
            ApiStatus.ToApiStatus(details.Status),
            details.CreatedAt,
            details.UpdatedAt,
            details.LastJob is null
                ? null
                : ContentProjectLastJobResponse.From(details.LastJob));
}

public sealed record ContentReadinessReasonResponse(
    string Code,
    string Message,
    bool Blocking)
{
    public static ContentReadinessReasonResponse From(ContentReadinessReason reason) =>
        new(reason.Code, reason.Message, reason.Blocking);
}

public sealed record ContentReadinessResponse(
    Guid ContentProjectId,
    string Status,
    bool ReadyToPublish,
    string ProjectStatus,
    ContentReadinessReasonResponse[] Reasons)
{
    public static ContentReadinessResponse From(ContentReadiness readiness) =>
        new(
            readiness.ContentProjectId,
            ApiStatus.ToApiStatus(readiness.Status),
            readiness.IsReadyToPublish,
            ApiStatus.ToApiStatus(readiness.ProjectStatus),
            readiness.Reasons.Select(ContentReadinessReasonResponse.From).ToArray());
}

public sealed record ContentProjectTransitionResponse(
    Guid ContentProjectId,
    bool Transitioned,
    string Status,
    string ProjectStatus,
    ContentReadinessReasonResponse[] Reasons)
{
    public static ContentProjectTransitionResponse From(ContentProjectTransitionResult result) =>
        new(
            result.Readiness.ContentProjectId,
            result.Transitioned,
            ApiStatus.ToApiStatus(result.Readiness.Status),
            ApiStatus.ToApiStatus(result.Readiness.ProjectStatus),
            result.Readiness.Reasons.Select(ContentReadinessReasonResponse.From).ToArray());
}
