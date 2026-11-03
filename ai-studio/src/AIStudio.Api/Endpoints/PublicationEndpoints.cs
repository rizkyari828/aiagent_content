using AIStudio.Application.Approvals;

namespace AIStudio.Api.Endpoints;

/// <summary>
/// Manual publication-record HTTP surface. It records what the operator published; it
/// performs no upload, authentication, or platform API call.
/// </summary>
public static class PublicationEndpoints
{
    public static IEndpointRouteBuilder MapPublicationApi(
        this IEndpointRouteBuilder endpoints)
    {
        var publications = endpoints.MapGroup(
            "/api/content-projects/{contentProjectId}/publications");

        publications.MapPost("", CreateAsync)
            .WithName("CreatePublication")
            .Produces<PublicationResponse>(StatusCodes.Status201Created)
            .Produces<PublicationResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesValidationProblem();

        publications.MapGet("", GetAsync)
            .WithName("GetPublication")
            .Produces<PublicationResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesValidationProblem();

        return endpoints;
    }

    internal static async Task<IResult> CreateAsync(
        string contentProjectId,
        CreatePublicationRequest request,
        PublicationWorkflow workflow,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(contentProjectId, out var projectId) || projectId == Guid.Empty)
        {
            return Results.ValidationProblem(
                new Dictionary<string, string[]>
                {
                    ["contentProjectId"] = ["A non-empty GUID is required."]
                },
                extensions: new Dictionary<string, object?>
                {
                    ["code"] = "publication_invalid_request"
                });
        }

        try
        {
            var result = await workflow.CreateAsync(
                projectId,
                request.Platform,
                request.Url,
                request.VideoId,
                request.PublishedAt,
                request.Notes,
                cancellationToken);

            if (result is null)
            {
                return FinalApprovalEndpoints.ContentProjectNotFound(projectId);
            }

            var response = PublicationResponse.From(result.Publication);
            return result.Created
                ? Results.Created(
                    $"/api/content-projects/{projectId}/publications",
                    response)
                : Results.Ok(response);
        }
        catch (PublicationException exception)
        {
            return PublicationProblem(exception);
        }
        catch (ArgumentException exception)
        {
            return Results.ValidationProblem(
                new Dictionary<string, string[]>
                {
                    [exception.ParamName ?? "request"] = [exception.Message]
                },
                extensions: new Dictionary<string, object?>
                {
                    ["code"] = "publication_invalid_request"
                });
        }
    }

    internal static async Task<IResult> GetAsync(
        string contentProjectId,
        PublicationWorkflow workflow,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(contentProjectId, out var projectId) || projectId == Guid.Empty)
        {
            return Results.ValidationProblem(
                new Dictionary<string, string[]>
                {
                    ["contentProjectId"] = ["A non-empty GUID is required."]
                },
                extensions: new Dictionary<string, object?>
                {
                    ["code"] = "publication_invalid_request"
                });
        }

        var publication = await workflow.FindAsync(projectId, cancellationToken);
        return publication is null
            ? FinalApprovalEndpoints.CodedProblem(
                StatusCodes.Status404NotFound,
                "Publication record not found.",
                $"Content project '{projectId}' does not have a publication record.",
                "publication_not_found")
            : Results.Ok(PublicationResponse.From(publication));
    }

    private static IResult PublicationProblem(PublicationException exception) =>
        exception.ErrorCode switch
        {
            "publication_approval_required" or "publication_destination_mismatch"
                or "publication_conflict" => FinalApprovalEndpoints.CodedProblem(
                    StatusCodes.Status409Conflict,
                    "Publication record rejected.",
                    exception.Message,
                    exception.ErrorCode),
            _ => FinalApprovalEndpoints.CodedProblem(
                StatusCodes.Status400BadRequest,
                "Publication request rejected.",
                exception.Message,
                exception.ErrorCode)
        };
}

public sealed record CreatePublicationRequest(
    string Platform,
    string Url,
    string VideoId,
    DateTimeOffset PublishedAt,
    string? Notes);

public sealed record PublicationResponse(
    Guid Id,
    Guid ContentProjectId,
    Guid FinalApprovalId,
    int FinalApprovalRevision,
    string ArtifactContentHash,
    string Platform,
    string Url,
    string VideoId,
    DateTimeOffset PublishedAt,
    string? Notes,
    DateTimeOffset CreatedAt)
{
    public static PublicationResponse From(PublicationSnapshot publication) =>
        new(
            publication.Id,
            publication.ContentProjectId,
            publication.FinalApprovalId,
            publication.FinalApprovalRevision,
            publication.ArtifactContentHash,
            publication.Platform,
            publication.Url,
            publication.VideoId,
            publication.PublishedAt,
            publication.Notes,
            publication.CreatedAt);
}
