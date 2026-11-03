using AIStudio.Domain.Approvals;

namespace AIStudio.Application.Approvals;

/// <summary>
/// The newest terminal FinalVideoQa job whose persisted payload targets the requested
/// render. The Application workflow deserializes <see cref="Result"/> and verifies the
/// pass evidence itself instead of trusting the job status alone, so a later failed QA
/// invalidates an earlier approval.
/// </summary>
public sealed record FinalVideoQaEvidence(
    Guid JobId,
    string? Result,
    bool Succeeded,
    DateTimeOffset CompletedAt);

/// <summary>
/// Approval-owned persistence boundary. QA/render evidence stays read-only: this port
/// can read jobs but never mutates job state.
/// </summary>
public interface IFinalApprovalRepository
{
    Task<FinalApproval?> FindByProjectIdAsync(
        Guid contentProjectId,
        CancellationToken cancellationToken);

    Task<FinalVideoQaEvidence?> FindLatestQaAsync(
        Guid contentProjectId,
        Guid renderJobId,
        CancellationToken cancellationToken);

    Task<bool> HasNewerRenderAsync(
        Guid contentProjectId,
        Guid renderJobId,
        DateTimeOffset renderCreatedAt,
        CancellationToken cancellationToken);

    void Add(FinalApproval approval);

    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}

public interface IPublicationRepository
{
    Task<PublicationRecord?> FindByProjectIdAsync(
        Guid contentProjectId,
        CancellationToken cancellationToken);

    void Add(PublicationRecord publication);

    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}

public sealed record PublicationSnapshot(
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
    DateTimeOffset CreatedAt);

public sealed record PublicationWriteResult(
    bool Created,
    PublicationSnapshot Publication);
