using AIStudio.Application.Jobs;
using AIStudio.Domain.Content;

namespace AIStudio.Application.Content;

/// <summary>
/// Project detail projection. <see cref="LastJob"/> is a narrow summary only: job
/// payload, result, worker, error, and lease data never leave the job module.
/// </summary>
public sealed record ContentProjectDetails(
    Guid Id,
    string Title,
    string? Brief,
    ContentStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    ProjectJobSummary? LastJob);
