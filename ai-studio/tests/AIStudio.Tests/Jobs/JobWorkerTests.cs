using AIStudio.Application.Jobs;
using AIStudio.Domain.Jobs;
using AIStudio.Infrastructure.Jobs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace AIStudio.Tests.Jobs;

public sealed class JobWorkerTests
{
    [Fact]
    public async Task Processor_CompletesSuccessfulPlaceholderJob()
    {
        var queue = new RecordingJobQueue(CreateClaimedJob());
        var processor = new JobProcessor(
            queue,
            [new PlaceholderJobHandler()],
            TimeProvider.System,
            NullLogger<JobProcessor>.Instance);

        var processed = await processor.ProcessNextAsync(
            "worker-success",
            TimeSpan.FromMinutes(1),
            TestContext.Current.CancellationToken);

        Assert.True(processed);
        Assert.True(queue.Completed);
        Assert.False(queue.Failed);
    }

    [Fact]
    public async Task Processor_RecordsHandlerFailure()
    {
        var queue = new RecordingJobQueue(CreateClaimedJob());
        var processor = new JobProcessor(
            queue,
            [new ThrowingHandler()],
            TimeProvider.System,
            NullLogger<JobProcessor>.Instance);

        var processed = await processor.ProcessNextAsync(
            "worker-failure",
            TimeSpan.FromMinutes(1),
            TestContext.Current.CancellationToken);

        Assert.True(processed);
        Assert.False(queue.Completed);
        Assert.True(queue.Failed);
        Assert.Equal("job_handler_failed", queue.ErrorCode);
    }

    [Fact]
    public async Task Processor_PreservesTypedHandlerErrorCode()
    {
        var queue = new RecordingJobQueue(CreateClaimedJob());
        var processor = new JobProcessor(
            queue,
            [new TypedThrowingHandler()],
            TimeProvider.System,
            NullLogger<JobProcessor>.Instance);

        await processor.ProcessNextAsync(
            "worker-typed-failure",
            TimeSpan.FromMinutes(1),
            TestContext.Current.CancellationToken);

        Assert.True(queue.Failed);
        Assert.Equal("ai_timeout", queue.ErrorCode);
    }

    [Fact]
    public async Task Worker_StopCancelsLongPollingDelay()
    {
        var queue = new RecordingJobQueue(null);
        var services = new ServiceCollection();
        services.AddSingleton<IJobQueue>(queue);
        services.AddSingleton<IJobHandler, PlaceholderJobHandler>();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<Microsoft.Extensions.Logging.ILogger<JobProcessor>>(NullLogger<JobProcessor>.Instance);
        services.AddScoped<JobProcessor>();
        await using var provider = services.BuildServiceProvider();

        var worker = new JobWorker(
            provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new JobWorkerOptions
            {
                Enabled = true,
                PollInterval = TimeSpan.FromMinutes(5),
                LeaseDuration = TimeSpan.FromMinutes(1)
            }),
            NullLogger<JobWorker>.Instance);

        await worker.StartAsync(TestContext.Current.CancellationToken);
        await queue.ClaimObserved.Task.WaitAsync(
            TimeSpan.FromSeconds(2),
            TestContext.Current.CancellationToken);

        await worker.StopAsync(TestContext.Current.CancellationToken)
            .WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);

        Assert.True(queue.ClaimObserved.Task.IsCompleted);
    }

    private static ClaimedJob CreateClaimedJob() =>
        new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            JobType.ResearchTopic,
            "input-v1",
            """{"topic":"worker"}""",
            0,
            1,
            false);

    private sealed class ThrowingHandler : IJobHandler
    {
        public bool CanHandle(JobType type) => true;

        public Task<string> ExecuteAsync(
            ClaimedJob job,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Expected handler failure.");
    }

    private sealed class TypedThrowingHandler : IJobHandler
    {
        public bool CanHandle(JobType type) => true;

        public Task<string> ExecuteAsync(
            ClaimedJob job,
            CancellationToken cancellationToken) =>
            throw new JobExecutionException("ai_timeout", "Expected timeout.");
    }

    private sealed class RecordingJobQueue(ClaimedJob? job) : IJobQueue
    {
        private int claimCount;

        public TaskCompletionSource ClaimObserved { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool Completed { get; private set; }

        public bool Failed { get; private set; }

        public string? ErrorCode { get; private set; }

        public Task<ClaimedJob?> ClaimNextAsync(
            string workerId,
            DateTimeOffset utcNow,
            DateTimeOffset leaseExpiresAt,
            CancellationToken cancellationToken)
        {
            ClaimObserved.TrySetResult();
            return Task.FromResult(
                Interlocked.Increment(ref claimCount) == 1 ? job : null);
        }

        public Task<bool> CompleteAsync(
            Guid jobId,
            string workerId,
            string result,
            DateTimeOffset utcNow,
            CancellationToken cancellationToken)
        {
            Completed = true;
            return Task.FromResult(true);
        }

        public Task<JobFailureResult?> FailAsync(
            Guid jobId,
            string workerId,
            string errorCode,
            string errorSummary,
            DateTimeOffset utcNow,
            CancellationToken cancellationToken)
        {
            Failed = true;
            ErrorCode = errorCode;
            return Task.FromResult<JobFailureResult?>(
                new(JobStatus.Queued, 1, true));
        }

        public Task<bool> RenewLeaseAsync(
            Guid jobId,
            string workerId,
            DateTimeOffset utcNow,
            DateTimeOffset leaseExpiresAt,
            CancellationToken cancellationToken) =>
            Task.FromResult(true);
    }
}
