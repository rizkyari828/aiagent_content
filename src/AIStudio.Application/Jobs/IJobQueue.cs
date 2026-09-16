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
}
