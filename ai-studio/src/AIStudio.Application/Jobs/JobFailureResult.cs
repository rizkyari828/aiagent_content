using AIStudio.Domain.Jobs;

namespace AIStudio.Application.Jobs;

public sealed record JobFailureResult(
    JobStatus Status,
    int RetryCount,
    bool RetryScheduled);
