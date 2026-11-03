using AIStudio.Domain.Content;

namespace AIStudio.Application.Content;

/// <summary>Computed production-completeness states. Never persisted.</summary>
public enum ContentReadinessStatus
{
    NotStarted = 0,
    InProgress = 1,
    QaPassed = 2,
    ReadyToPublish = 3,
    Blocked = 4
}

/// <summary>Stable readiness reason codes.</summary>
public static class ContentReadinessCodes
{
    public const string RenderMissing = "readiness_render_missing";
    public const string QaFailed = "readiness_qa_failed";
    public const string FinalApprovalMissing = "readiness_final_approval_missing";
    public const string FinalApprovalInvalid = "readiness_final_approval_invalid";
    public const string FinalApprovalChangesRequested =
        "readiness_final_approval_changes_requested";
    public const string PublicationMissing = "readiness_publication_missing";
}

/// <summary>
/// One computed reason. <see cref="Blocking"/> separates "cannot be ready yet" from
/// the informational next step (<c>readiness_publication_missing</c>), so a ready
/// project is never reported as blocked by an unpublished record.
/// </summary>
public sealed record ContentReadinessReason(
    string Code,
    string Message,
    bool Blocking);

/// <summary>
/// Read-time readiness projection. <see cref="ProjectStatus"/> is the persisted
/// <see cref="ContentStatus"/>; <see cref="Status"/> is derived from evidence and is
/// never stored. This keeps readiness correct after artifacts, QA, or approvals change.
/// </summary>
public sealed record ContentReadiness(
    Guid ContentProjectId,
    ContentStatus ProjectStatus,
    ContentReadinessStatus Status,
    IReadOnlyList<ContentReadinessReason> Reasons)
{
    public bool IsReadyToPublish => Status == ContentReadinessStatus.ReadyToPublish;

    public IReadOnlyList<string> BlockingReasonCodes =>
        Reasons.Where(reason => reason.Blocking).Select(reason => reason.Code).ToArray();
}

/// <summary>Result of an explicit, readiness-guarded <c>ReadyToPublish</c> transition.</summary>
public sealed record ContentProjectTransitionResult(
    bool Transitioned,
    ContentReadiness Readiness);
