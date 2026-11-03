using AIStudio.Domain.Jobs;
using Xunit;

namespace AIStudio.Tests.Jobs;

public sealed class JobTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 15, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Lifecycle_TransitionsFromQueuedToSucceeded()
    {
        var job = CreateJob();

        job.Start("worker-1", Now.AddMinutes(5), Now.AddMinutes(1));
        job.Succeed("""{"output":"idea"}""", Now.AddMinutes(2));

        Assert.Equal(JobStatus.Succeeded, job.Status);
        Assert.Equal(Now.AddMinutes(1), job.StartedAt);
        Assert.Equal(Now.AddMinutes(2), job.CompletedAt);
        Assert.Null(job.LeaseExpiresAt);
        Assert.Equal("""{"output":"idea"}""", job.Result);
    }

    [Fact]
    public void Start_RejectsInvalidTransition()
    {
        var job = CreateJob();
        job.Start("worker-1", Now.AddMinutes(5), Now.AddMinutes(1));

        Assert.Throws<InvalidOperationException>(
            () => job.Start("worker-2", Now.AddMinutes(6), Now.AddMinutes(2)));
    }

    [Fact]
    public void RequeueForRetry_IncrementsRetryCountAndClearsFailure()
    {
        var job = CreateJob(maxRetries: 1);
        job.Start("worker-1", Now.AddMinutes(5), Now.AddMinutes(1));
        job.Fail("temporary", "Transient failure.", Now.AddMinutes(2));

        job.RequeueForRetry(Now.AddMinutes(3));

        Assert.Equal(JobStatus.Queued, job.Status);
        Assert.Equal(1, job.RetryCount);
        Assert.Null(job.StartedAt);
        Assert.Null(job.CompletedAt);
        Assert.Null(job.WorkerId);
        Assert.Null(job.ErrorCode);
        Assert.Null(job.ErrorSummary);
    }

    [Fact]
    public void RequeueForRetry_RejectsExhaustedAllowance()
    {
        var job = CreateJob(maxRetries: 0);
        job.Start("worker-1", Now.AddMinutes(5), Now.AddMinutes(1));
        job.Fail("permanent", "Permanent failure.", Now.AddMinutes(2));

        Assert.Throws<InvalidOperationException>(
            () => job.RequeueForRetry(Now.AddMinutes(3)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("[]")]
    [InlineData("not-json")]
    public void Create_RejectsInvalidPayload(string payload)
    {
        Assert.Throws<ArgumentException>(
            () => Job.Create(
                Guid.NewGuid(),
                JobType.GenerateIdea,
                "input-v1",
                payload,
                Now));
    }

    [Fact]
    public void Cancel_QueuedJobBecomesCancelled()
    {
        var job = CreateJob();

        job.Cancel(Now.AddMinutes(1));

        Assert.Equal(JobStatus.Cancelled, job.Status);
        Assert.Equal(Now.AddMinutes(1), job.CompletedAt);
        Assert.Null(job.LeaseExpiresAt);
        Assert.Null(job.Result);
    }

    [Fact]
    public void Cancel_RunningJobBecomesCancelledAndClearsLease()
    {
        var job = CreateJob();
        job.Start("worker-1", Now.AddMinutes(5), Now.AddMinutes(1));

        job.Cancel(Now.AddMinutes(2));

        Assert.Equal(JobStatus.Cancelled, job.Status);
        Assert.Null(job.LeaseExpiresAt);
        Assert.Equal("worker-1", job.WorkerId);
        Assert.Equal(Now.AddMinutes(2), job.CompletedAt);
    }

    [Theory]
    [InlineData("succeeded")]
    [InlineData("failed")]
    [InlineData("cancelled")]
    public void Cancel_RejectsTerminalJobs(string terminal)
    {
        var job = CreateJob();
        job.Start("worker-1", Now.AddMinutes(5), Now.AddMinutes(1));

        switch (terminal)
        {
            case "succeeded":
                job.Succeed("""{"output":"idea"}""", Now.AddMinutes(2));
                break;
            case "failed":
                job.Fail("permanent", "Permanent failure.", Now.AddMinutes(2));
                break;
            default:
                job.Cancel(Now.AddMinutes(2));
                break;
        }

        Assert.Throws<InvalidOperationException>(
            () => job.Cancel(Now.AddMinutes(3)));
    }

    private static Job CreateJob(int maxRetries = 2) =>
        Job.Create(
            Guid.NewGuid(),
            JobType.GenerateIdea,
            "input-v1",
            """{"topic":"local AI"}""",
            Now,
            maxRetries);
}
