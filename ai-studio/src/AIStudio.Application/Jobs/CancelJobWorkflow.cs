namespace AIStudio.Application.Jobs;

/// <summary>
/// Operator-initiated job cancellation. It delegates the durable, status-guarded
/// transition to <see cref="IJobQueue"/>; it never touches worker ownership or leases
/// itself. A cancelled job stops being claimed and can no longer be completed or
/// failed by a worker because the queue guards those updates on the running status.
/// </summary>
public sealed class CancelJobWorkflow(
    IJobQueue jobQueue,
    TimeProvider timeProvider)
{
    public async Task<JobCancelResult> CancelAsync(
        Guid jobId,
        CancellationToken cancellationToken)
    {
        if (jobId == Guid.Empty)
        {
            throw new ArgumentException("Job ID cannot be empty.", nameof(jobId));
        }

        return await jobQueue.CancelAsync(
            jobId,
            timeProvider.GetUtcNow(),
            cancellationToken);
    }
}
