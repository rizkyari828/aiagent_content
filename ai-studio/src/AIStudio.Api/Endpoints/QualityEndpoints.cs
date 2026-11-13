using AIStudio.Application.Quality;
using AIStudio.Application.Research;

namespace AIStudio.Api.Endpoints;

public static class QualityEndpoints
{
    public static IEndpointRouteBuilder MapQualityApi(
        this IEndpointRouteBuilder endpoints)
    {
        var api = endpoints.MapGroup("/api");

        api.MapGet("/content-projects/{contentProjectId}/pre-render-check", PreRenderCheckAsync)
            .WithName("PreRenderQualityCheck")
            .Produces<PreRenderCheckReport>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesValidationProblem();

        api.MapGet("/content-projects/{contentProjectId}/packaging", GetPackagingAsync)
            .WithName("GetPackagingMetadata")
            .Produces<PackagingMetadata>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesValidationProblem();

        api.MapPost("/content-projects/{contentProjectId}/packaging", GeneratePackagingAsync)
            .WithName("GeneratePackagingMetadata")
            .Produces<PackagingMetadata>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesValidationProblem();

        return endpoints;
    }

    private static async Task<IResult> PreRenderCheckAsync(
        string contentProjectId,
        double? targetDurationSeconds,
        PreRenderQualityWorkflow workflow,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(contentProjectId, out var projectId) || projectId == Guid.Empty)
        {
            return InvalidIdentifier("contentProjectId");
        }

        var report = await workflow.CheckProjectAsync(projectId, targetDurationSeconds, cancellationToken);
        return report is null
            ? ProjectNotFound(projectId)
            : Results.Ok(report);
    }

    private static async Task<IResult> GetPackagingAsync(
        string contentProjectId,
        PackagingWorkflow workflow,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(contentProjectId, out var projectId) || projectId == Guid.Empty)
        {
            return InvalidIdentifier("contentProjectId");
        }

        var metadata = await workflow.GetAsync(projectId, cancellationToken);
        return metadata is null
            ? Results.Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "Packaging metadata not found.",
                detail: $"Content project '{projectId}' does not have packaging metadata yet.",
                extensions: new Dictionary<string, object?> { ["code"] = "packaging_not_found" })
            : Results.Ok(metadata);
    }

    private static async Task<IResult> GeneratePackagingAsync(
        string contentProjectId,
        HttpRequest httpRequest,
        PackagingWorkflow workflow,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(contentProjectId, out var projectId) || projectId == Guid.Empty)
        {
            return InvalidIdentifier("contentProjectId");
        }

        GeneratePackagingRequest? body = null;
        if (httpRequest.HasJsonContentType() && (httpRequest.ContentLength ?? 1) > 0)
        {
            try
            {
                body = await httpRequest.ReadFromJsonAsync<GeneratePackagingRequest>(cancellationToken);
            }
            catch (System.Text.Json.JsonException)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["request"] = ["Request body must be a valid JSON object."]
                });
            }
        }

        try
        {
            var result = await workflow.GenerateAsync(projectId, body?.Provider, body?.Model, cancellationToken);
            return result is null
                ? ProjectNotFound(projectId)
                : Results.Ok(result);
        }
        catch (ResearchException ex)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Packaging generation conflict.",
                detail: ex.Message,
                extensions: new Dictionary<string, object?> { ["code"] = ex.ErrorCode });
        }
    }

    private static IResult ProjectNotFound(Guid projectId) =>
        Results.Problem(
            statusCode: StatusCodes.Status404NotFound,
            title: "Content project not found.",
            detail: $"Content project '{projectId}' does not exist.",
            extensions: new Dictionary<string, object?> { ["code"] = "content_project_not_found" });

    private static IResult InvalidIdentifier(string fieldName) =>
        Results.ValidationProblem(new Dictionary<string, string[]>
        {
            [fieldName] = ["A non-empty GUID is required."]
        });

    public sealed record GeneratePackagingRequest(
        string? Provider = null,
        string? Model = null);
}
