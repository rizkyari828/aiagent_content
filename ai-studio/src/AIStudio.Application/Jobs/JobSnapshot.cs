using AIStudio.Domain.Jobs;

namespace AIStudio.Application.Jobs;

public sealed record JobSnapshot(
    Guid Id,
    Guid ContentProjectId,
    JobType Type,
    JobStatus Status,
    int RetryCount,
    int MaxRetries,
    string? Result,
    string? ErrorCode,
    string? ErrorSummary,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt);
