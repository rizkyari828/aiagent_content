using AIStudio.Domain.Jobs;

namespace AIStudio.Application.Jobs;

/// <summary>
/// Narrow project-scoped job projection for content/project read models. It never
/// exposes the persisted payload or result, so internal job evidence cannot leak
/// through a project endpoint.
/// </summary>
public sealed record ProjectJobSummary(
    Guid Id,
    JobType Type,
    JobStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? CompletedAt);

public interface IProjectJobReader
{
    Task<ProjectJobSummary?> FindLatestAsync(
        Guid contentProjectId,
        CancellationToken cancellationToken);

    Task<ProjectJobSummary?> FindLatestAsync(
        Guid contentProjectId,
        JobType type,
        CancellationToken cancellationToken);
}
