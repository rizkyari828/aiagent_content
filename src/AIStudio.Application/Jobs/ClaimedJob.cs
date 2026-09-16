using AIStudio.Domain.Jobs;

namespace AIStudio.Application.Jobs;

public sealed record ClaimedJob(
    Guid Id,
    Guid ContentProjectId,
    JobType Type,
    string InputVersionHash,
    string Payload,
    int RetryCount,
    int MaxRetries,
    bool RecoveredFromExpiredLease);
