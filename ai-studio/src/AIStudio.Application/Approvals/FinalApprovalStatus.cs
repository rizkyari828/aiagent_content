namespace AIStudio.Application.Approvals;

/// <summary>
/// Read-time A3 status. The persisted row is never treated as sufficient on its own:
/// render state, QA evidence, artifact hash, and render supersession are re-evaluated.
/// </summary>
public enum FinalApprovalState
{
    Missing = 0,
    Approved = 1,
    Invalid = 2
}

public sealed record FinalApprovalStatus(
    FinalApprovalState State,
    string? Reason,
    FinalApprovalSnapshot? Approval)
{
    public bool IsApproved => State == FinalApprovalState.Approved;

    public static FinalApprovalStatus Missing() =>
        new(FinalApprovalState.Missing, null, null);

    public static FinalApprovalStatus Approved(FinalApprovalSnapshot approval) =>
        new(FinalApprovalState.Approved, null, approval);

    public static FinalApprovalStatus Invalid(
        string reason,
        FinalApprovalSnapshot approval) =>
        new(FinalApprovalState.Invalid, reason, approval);
}

/// <summary>Canonical A3 projection: no worker, lease, or path-resolution internals.</summary>
public sealed record FinalApprovalSnapshot(
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
    DateTimeOffset UpdatedAt);

public sealed record FinalApprovalApproveResult(
    bool Created,
    FinalApprovalSnapshot Approval);

/// <summary>
/// Read-only QA verdict for one render, using the exact A3 pass rules. Readiness reuses
/// this instead of duplicating QA evidence interpretation.
/// </summary>
public enum FinalVideoQaVerdict
{
    Passed = 0,
    Failed = 1,
    Missing = 2,
    RenderNotSucceeded = 3
}
