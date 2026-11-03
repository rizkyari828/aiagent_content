namespace AIStudio.Application.Jobs;

public interface IJobQueue
{
    Task<ClaimedJob?> ClaimNextAsync(
        string workerId,
        DateTimeOffset utcNow,
        DateTimeOffset leaseExpiresAt,
        CancellationToken cancellationToken);

    Task<bool> CompleteAsync(
        Guid jobId,
        string workerId,
        string result,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken);

    Task<JobFailureResult?> FailAsync(
        Guid jobId,
        string workerId,
        string errorCode,
        string errorSummary,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken);

    Task<bool> RenewLeaseAsync(
        Guid jobId,
        string workerId,
        DateTimeOffset utcNow,
        DateTimeOffset leaseExpiresAt,
        CancellationToken cancellationToken);

    /// <summary>
    /// Operator cancel: <c>Queued</c> and <c>Running</c> jobs become <c>Cancelled</c>;
    /// terminal jobs are rejected and unknown jobs report <see cref="JobCancelOutcome.NotFound"/>.
    /// Cancelling clears the lease, so the job is never claimed again, and the existing
    /// ownership-guarded completion/failure/renewal updates can no longer write it.
    /// </summary>
    Task<JobCancelResult> CancelAsync(
        Guid jobId,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken);
}
