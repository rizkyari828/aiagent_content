using AIStudio.Application.Approvals;
using AIStudio.Domain.Approvals;

namespace AIStudio.Api.Endpoints;

/// <summary>
/// A3 final-approval HTTP surface. Transport only: render/QA/artifact validation,
/// invalidation, decision validation, and revision/idempotency semantics live in
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

        var decision = FinalApprovalDecision.Approved;
        if (!string.IsNullOrWhiteSpace(request.Decision)
            && !FinalApprovalDecisionTokens.TryParse(request.Decision, out decision))
        {
            return DecisionInvalid();
        }

        var note = request.Note;
        if (decision == FinalApprovalDecision.ChangesRequested
            && string.IsNullOrWhiteSpace(note))
        {
            return NoteRequired();
        }

        if (note is not null && note.Trim().Length > FinalApproval.MaxDecisionNoteLength)
        {
            return NoteInvalid();
        }

        try
        {
            var result = decision == FinalApprovalDecision.ChangesRequested
                ? await workflow.RequestChangesAsync(
                    projectId,
                    request.RenderJobId,
                    request.Destination,
                    note!,
                    request.ApproverLabel,
                    cancellationToken)
                : await workflow.ApproveAsync(
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
                "The review decision could not be recorded.",
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
        CodedValidationProblem(
            "final_approval_invalid_request",
            "The final approval request is invalid.",
            "Use the content project and render job identifiers in their GET form, then retry.",
            fieldName,
            "A non-empty GUID is required.");

    internal static IResult InvalidRequest(ArgumentException exception) =>
        CodedValidationProblem(
            "final_approval_invalid_request",
            "The final approval request is invalid.",
            "Fix the highlighted field and retry; no review decision was recorded.",
            exception.ParamName ?? "request",
            exception.Message);

    private static IResult DecisionInvalid() =>
        CodedValidationProblem(
            "final_approval_decision_invalid",
            "The review decision is not recognized.",
            "Use 'approved' to approve the render, or 'changes_requested' to send it back with a note.",
            "decision",
            $"Decision must be '{FinalApprovalDecisionTokens.Approved}' or '{FinalApprovalDecisionTokens.ChangesRequested}'.");

    private static IResult NoteRequired() =>
        CodedValidationProblem(
            "final_approval_note_required",
            "A note is required when requesting changes.",
            "Describe what must change so the production can be fixed (for example: 'Subtitles are cut off in scene 3; re-render with the safe margin'). The note is limited to 2000 characters.",
            "note",
            "Provide a decision note when decision is 'changes_requested'.");

    private static IResult NoteInvalid() =>
        CodedValidationProblem(
            "final_approval_note_invalid",
            "The decision note is too long.",
            $"Shorten the note to at most {FinalApproval.MaxDecisionNoteLength} characters and retry.",
            "note",
            $"A decision note is limited to {FinalApproval.MaxDecisionNoteLength} characters.");

    internal static IResult CodedValidationProblem(
        string code,
        string title,
        string detail,
        string fieldName,
        string message) =>
        Results.ValidationProblem(
            new Dictionary<string, string[]>
            {
                [fieldName] = [message]
            },
            detail: detail,
            title: title,
            extensions: new Dictionary<string, object?>
            {
                ["code"] = code
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

/// <summary>
/// POST body. <see cref="Decision"/> and <see cref="Note"/> are optional so every
/// pre-review-decision client keeps working: an omitted decision means <c>approved</c>,
/// exactly like the original endpoint. <see cref="Notes"/> is the pre-existing approval
/// note and remains separate from the new decision note.
/// </summary>
public sealed record ApproveFinalApprovalRequest(
    Guid RenderJobId,
    string Destination,
    string? Notes,
    string? ApproverLabel,
    string? Decision = null,
    string? Note = null);

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
    string Decision,
    string? DecisionNote,
    DateTimeOffset DecidedAt,
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
            FinalApprovalDecisionTokens.ToToken(approval.Decision),
            approval.DecisionNote,
            approval.DecidedAt,
            approval.Revision,
            approval.ApprovedAt,
            approval.CreatedAt,
            approval.UpdatedAt);
}

/// <summary>
/// Read projection. <see cref="Reason"/> is the original single reason and stays for
/// compatibility; <see cref="Reasons"/> is the same value as a list. The decision fields
/// are repeated at the top level so a client can render the decision without descending
/// into <see cref="Approval"/>.
/// </summary>
public sealed record FinalApprovalStatusResponse(
    string Status,
    string? Reason,
    IReadOnlyList<string> Reasons,
    string? Decision,
    string? DecisionNote,
    DateTimeOffset? DecidedAt,
    FinalApprovalResponse? Approval)
{
    public static FinalApprovalStatusResponse From(FinalApprovalStatus status)
    {
        var approval = status.Approval is null
            ? null
            : FinalApprovalResponse.From(status.Approval);

        return new FinalApprovalStatusResponse(
            ToApiStatus(status.State),
            status.Reason,
            status.Reason is null ? [] : [status.Reason],
            approval?.Decision,
            approval?.DecisionNote,
            approval?.DecidedAt,
            approval);
    }

    private static string ToApiStatus(FinalApprovalState state) =>
        state switch
        {
            FinalApprovalState.Approved => "approved",
            FinalApprovalState.ChangesRequested => FinalApprovalDecisionTokens.ChangesRequested,
            FinalApprovalState.Invalid => "invalid",
            FinalApprovalState.Missing => "missing",
            _ => throw new ArgumentOutOfRangeException(nameof(state), state, null)
        };
}
