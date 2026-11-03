using AIStudio.Application.Approvals;

namespace AIStudio.Api.Endpoints;

/// <summary>
/// A3 final-approval HTTP surface. Transport only: render/QA/artifact validation,
/// invalidation, and revision/idempotency semantics live in
/// <see cref="FinalApprovalWorkflow"/>.
/// </summary>
public static class FinalApprovalEndpoints
{
    public static IEndpointRouteBuilder MapFinalApprovalApi(
        this IEndpointRouteBuilder endpoints)
    {
        var approvals = endpoints.MapGroup(
            "/api/content-projects/{contentProjectId}/final-approvals");

        approvals.MapPost("", ApproveAsync)
            .WithName("ApproveFinalVideo")
            .Produces<FinalApprovalResponse>(StatusCodes.Status201Created)
            .Produces<FinalApprovalResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesValidationProblem();

        approvals.MapGet("", GetStatusAsync)
            .WithName("GetFinalApproval")
            .Produces<FinalApprovalStatusResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesValidationProblem();

        return endpoints;
    }

    internal static async Task<IResult> ApproveAsync(
        string contentProjectId,
        ApproveFinalApprovalRequest request,
        FinalApprovalWorkflow workflow,
        CancellationToken cancellationToken)
    {
        if (!TryParseIdentifier(contentProjectId, out var projectId))
        {
            return InvalidIdentifier("contentProjectId");
        }

        if (request.RenderJobId == Guid.Empty)
        {
            return InvalidIdentifier("renderJobId");
        }

        try
        {
            var result = await workflow.ApproveAsync(
                projectId,
                request.RenderJobId,
                request.Destination,
                request.Notes,
                request.ApproverLabel,
                cancellationToken);

            if (result is null)
            {
                return ContentProjectNotFound(projectId);
            }

            var response = FinalApprovalResponse.From(result.Approval);
            return result.Created
                ? Results.Created(
                    $"/api/content-projects/{projectId}/final-approvals",
                    response)
                : Results.Ok(response);
        }
        catch (FinalApprovalException exception)
        {
            return ApprovalProblem(exception);
        }
        catch (ArgumentException exception)
        {
            return InvalidRequest(exception);
        }
    }

    internal static async Task<IResult> GetStatusAsync(
        string contentProjectId,
        FinalApprovalWorkflow workflow,
        CancellationToken cancellationToken)
    {
        if (!TryParseIdentifier(contentProjectId, out var projectId))
        {
            return InvalidIdentifier("contentProjectId");
        }

        var status = await workflow.EvaluateAsync(projectId, cancellationToken);
        return status is null
            ? ContentProjectNotFound(projectId)
            : Results.Ok(FinalApprovalStatusResponse.From(status));
    }

    private static IResult ApprovalProblem(FinalApprovalException exception) =>
        exception.ErrorCode switch
        {
            "final_approval_render_missing" => CodedProblem(
                StatusCodes.Status404NotFound,
                "Render job not found.",
                exception.Message,
                exception.ErrorCode),
            _ => CodedProblem(
                StatusCodes.Status409Conflict,
                "Final approval cannot be granted.",
                exception.Message,
                exception.ErrorCode)
        };

    internal static IResult ContentProjectNotFound(Guid projectId) =>
        CodedProblem(
            StatusCodes.Status404NotFound,
            "Content project not found.",
            $"Content project '{projectId}' does not exist.",
            "content_project_not_found");

    internal static IResult InvalidIdentifier(string fieldName) =>
        Results.ValidationProblem(
            new Dictionary<string, string[]>
            {
                [fieldName] = ["A non-empty GUID is required."]
            },
            extensions: new Dictionary<string, object?>
            {
                ["code"] = "final_approval_invalid_request"
            });

    internal static IResult InvalidRequest(ArgumentException exception) =>
        Results.ValidationProblem(
            new Dictionary<string, string[]>
            {
                [exception.ParamName ?? "request"] = [exception.Message]
            },
            extensions: new Dictionary<string, object?>
            {
                ["code"] = "final_approval_invalid_request"
            });

    internal static IResult CodedProblem(
        int statusCode,
        string title,
        string detail,
        string code) =>
        Results.Problem(
            statusCode: statusCode,
            title: title,
            detail: detail,
            extensions: new Dictionary<string, object?>
            {
                ["code"] = code
            });

    private static bool TryParseIdentifier(string value, out Guid id) =>
        Guid.TryParse(value, out id) && id != Guid.Empty;
}

public sealed record ApproveFinalApprovalRequest(
    Guid RenderJobId,
    string Destination,
    string? Notes,
    string? ApproverLabel);

public sealed record FinalApprovalResponse(
    Guid Id,
    Guid ContentProjectId,
    Guid SourceRenderJobId,
    string ArtifactPath,
    string ArtifactContentHash,
    long ArtifactByteSize,
    string Destination,
    string? Notes,
    string? ApproverLabel,
    int Revision,
    DateTimeOffset ApprovedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    public static FinalApprovalResponse From(FinalApprovalSnapshot approval) =>
        new(
            approval.Id,
            approval.ContentProjectId,
            approval.SourceRenderJobId,
            approval.ArtifactPath,
            approval.ArtifactContentHash,
            approval.ArtifactByteSize,
            approval.Destination,
            approval.Notes,
            approval.ApproverLabel,
            approval.Revision,
            approval.ApprovedAt,
            approval.CreatedAt,
            approval.UpdatedAt);
}

public sealed record FinalApprovalStatusResponse(
    string Status,
    string? Reason,
    FinalApprovalResponse? Approval)
{
    public static FinalApprovalStatusResponse From(FinalApprovalStatus status) =>
        new(
            ToApiStatus(status.State),
            status.Reason,
            status.Approval is null
                ? null
                : FinalApprovalResponse.From(status.Approval));

    private static string ToApiStatus(FinalApprovalState state) =>
        state switch
        {
            FinalApprovalState.Approved => "approved",
            FinalApprovalState.Invalid => "invalid",
            FinalApprovalState.Missing => "missing",
            _ => throw new ArgumentOutOfRangeException(nameof(state), state, null)
        };
}
