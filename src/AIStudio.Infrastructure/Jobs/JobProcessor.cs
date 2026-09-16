using AIStudio.Application.Jobs;
using AIStudio.Domain.Jobs;
using Microsoft.Extensions.Logging;

namespace AIStudio.Infrastructure.Jobs;

public sealed class JobProcessor(
    IJobQueue jobQueue,
    IEnumerable<IJobHandler> handlers,
    TimeProvider timeProvider,
    ILogger<JobProcessor> logger)
{
    public async Task<bool> ProcessNextAsync(
        string workerId,
        TimeSpan leaseDuration,
        CancellationToken stoppingToken)
    {
        var now = timeProvider.GetUtcNow();
        var job = await jobQueue.ClaimNextAsync(
            workerId,
            now,
            now.Add(leaseDuration),
            stoppingToken);

        if (job is null)
        {
            return false;
        }

        logger.LogInformation(
            "Job {JobId} ({JobType}) claimed by {WorkerId}; recovered lease: {RecoveredLease}.",
            job.Id,
            job.Type,
            workerId,
            job.RecoveredFromExpiredLease);

        var handler = handlers.SingleOrDefault(candidate => candidate.CanHandle(job.Type));
        if (handler is null)
        {
            await RecordFailureAsync(
                job,
                workerId,
                "unsupported_job_type",
                $"No handler is registered for job type {job.Type}.",
                stoppingToken);
            return true;
        }

        using var executionCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        using var renewalCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);

        var renewalTask = MaintainLeaseAsync(
            job.Id,
            workerId,
            leaseDuration,
            executionCancellation,
            renewalCancellation.Token);

        try
        {
            var result = await handler.ExecuteAsync(job, executionCancellation.Token);
            var completed = await jobQueue.CompleteAsync(
                job.Id,
                workerId,
                result,
                timeProvider.GetUtcNow(),
                stoppingToken);

            if (completed)
            {
                logger.LogInformation("Job {JobId} succeeded.", job.Id);
            }
            else
            {
                logger.LogWarning(
                    "Job {JobId} completion was ignored because ownership was lost.",
                    job.Id);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            logger.LogInformation(
                "Job {JobId} execution stopped with host shutdown; lease recovery remains available.",
                job.Id);
        }
        catch (OperationCanceledException) when (executionCancellation.IsCancellationRequested)
        {
            logger.LogWarning(
                "Job {JobId} execution was cancelled after its lease ownership was lost.",
                job.Id);
        }
        catch (JobExecutionException exception)
        {
            var summary = string.IsNullOrWhiteSpace(exception.Message)
                ? exception.GetType().Name
                : exception.Message;
            await RecordFailureAsync(
                job,
                workerId,
                exception.ErrorCode,
                summary[..Math.Min(summary.Length, Job.MaxErrorSummaryLength)],
                stoppingToken);
        }
        catch (Exception exception)
        {
            var summary = string.IsNullOrWhiteSpace(exception.Message)
                ? exception.GetType().Name
                : exception.Message;
            await RecordFailureAsync(
                job,
                workerId,
                "job_handler_failed",
                summary[..Math.Min(summary.Length, Job.MaxErrorSummaryLength)],
                stoppingToken);
        }
        finally
        {
            await renewalCancellation.CancelAsync();
            try
            {
                await renewalTask;
            }
            catch (OperationCanceledException) when (renewalCancellation.IsCancellationRequested)
            {
            }
        }

        return true;
    }

    private async Task MaintainLeaseAsync(
        Guid jobId,
        string workerId,
        TimeSpan leaseDuration,
        CancellationTokenSource executionCancellation,
        CancellationToken cancellationToken)
    {
        var renewalInterval = TimeSpan.FromTicks(leaseDuration.Ticks / 3);

        while (!cancellationToken.IsCancellationRequested)
        {
            await Task.Delay(renewalInterval, timeProvider, cancellationToken);

            var now = timeProvider.GetUtcNow();
            var renewed = await jobQueue.RenewLeaseAsync(
                jobId,
                workerId,
                now,
                now.Add(leaseDuration),
                cancellationToken);

            if (renewed)
            {
                continue;
            }

            logger.LogWarning("Job {JobId} lease renewal failed; ownership was lost.", jobId);
            await executionCancellation.CancelAsync();
            return;
        }
    }

    private async Task RecordFailureAsync(
        ClaimedJob job,
        string workerId,
        string errorCode,
        string errorSummary,
        CancellationToken cancellationToken)
    {
        var failure = await jobQueue.FailAsync(
            job.Id,
            workerId,
            errorCode,
            errorSummary,
            timeProvider.GetUtcNow(),
            cancellationToken);

        if (failure is null)
        {
            logger.LogWarning(
                "Job {JobId} failure was ignored because ownership was lost.",
                job.Id);
            return;
        }

        if (failure.RetryScheduled)
        {
            logger.LogWarning(
                "Job {JobId} failed; retry {RetryCount}/{MaxRetries} scheduled.",
                job.Id,
                failure.RetryCount,
                job.MaxRetries);
            return;
        }

        logger.LogError(
            "Job {JobId} failed and retry allowance is exhausted.",
            job.Id);
    }
}
