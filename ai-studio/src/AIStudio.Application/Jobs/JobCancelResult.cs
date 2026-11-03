using AIStudio.Domain.Jobs;

namespace AIStudio.Application.Jobs;

public enum JobCancelOutcome
{
    Cancelled = 0,
    NotFound = 1,
    NotAllowed = 2
}

/// <summary>
/// Outcome of an operator cancel request. <see cref="Status"/> is the status the job
/// holds after the request: <c>Cancelled</c> for a successful cancel, the current
/// terminal status when cancel is rejected, and <c>null</c> when the job is unknown.
/// </summary>
public sealed record JobCancelResult(
    JobCancelOutcome Outcome,
    JobStatus? Status);
