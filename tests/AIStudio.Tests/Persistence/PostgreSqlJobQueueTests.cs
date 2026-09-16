using AIStudio.Domain.Content;
using AIStudio.Domain.Jobs;
using AIStudio.Infrastructure.Jobs;
using AIStudio.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AIStudio.Tests.Persistence;

public sealed class PostgreSqlJobQueueTests
{
    [Fact]
    public async Task ClaimAndComplete_TransitionsQueuedJobToSucceeded()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (factory, queue) = CreateQueue();
        var (projectId, jobId, now) = await SeedJobAsync(factory, maxRetries: 1, cancellationToken);

        try
        {
            var claimed = await queue.ClaimNextAsync(
                "worker-success",
                now.AddSeconds(1),
                now.AddMinutes(2),
                cancellationToken);

            Assert.NotNull(claimed);
            Assert.Equal(jobId, claimed.Id);
            Assert.False(claimed.RecoveredFromExpiredLease);

            await using (var context = await factory.CreateDbContextAsync(cancellationToken))
            {
                var running = await context.Jobs.AsNoTracking()
                    .SingleAsync(job => job.Id == jobId, cancellationToken);
                Assert.Equal(JobStatus.Running, running.Status);
                Assert.Equal("worker-success", running.WorkerId);
            }

            var completed = await queue.CompleteAsync(
                jobId,
                "worker-success",
                """{"status":"done"}""",
                now.AddSeconds(2),
                cancellationToken);

            Assert.True(completed);

            await using var verification = await factory.CreateDbContextAsync(cancellationToken);
            var succeeded = await verification.Jobs.AsNoTracking()
                .SingleAsync(job => job.Id == jobId, cancellationToken);
            Assert.Equal(JobStatus.Succeeded, succeeded.Status);
            Assert.NotNull(succeeded.CompletedAt);
            Assert.Null(succeeded.LeaseExpiresAt);
        }
        finally
        {
            await CleanupAsync(factory, projectId, cancellationToken);
        }
    }

    [Fact]
    public async Task ConcurrentClaims_ReturnTheJobToExactlyOneWorker()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (factory, firstQueue) = CreateQueue();
        var secondQueue = new PostgreSqlJobQueue(factory);
        var (projectId, jobId, now) = await SeedJobAsync(factory, maxRetries: 1, cancellationToken);

        try
        {
            var claims = await Task.WhenAll(
                firstQueue.ClaimNextAsync(
                    "worker-a",
                    now.AddSeconds(1),
                    now.AddMinutes(2),
                    cancellationToken),
                secondQueue.ClaimNextAsync(
                    "worker-b",
                    now.AddSeconds(1),
                    now.AddMinutes(2),
                    cancellationToken));

            var claimed = Assert.Single(claims, result => result is not null);
            Assert.Equal(jobId, claimed!.Id);
            Assert.Equal(1, claims.Count(result => result is null));

            await using var context = await factory.CreateDbContextAsync(cancellationToken);
            var persisted = await context.Jobs.AsNoTracking()
                .SingleAsync(job => job.Id == jobId, cancellationToken);
            Assert.Equal(JobStatus.Running, persisted.Status);
            Assert.Contains(persisted.WorkerId, new[] { "worker-a", "worker-b" });
        }
        finally
        {
            await CleanupAsync(factory, projectId, cancellationToken);
        }
    }

    [Fact]
    public async Task Failure_RetriesOnceThenEndsFailed()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (factory, queue) = CreateQueue();
        var (projectId, jobId, now) = await SeedJobAsync(factory, maxRetries: 1, cancellationToken);

        try
        {
            Assert.NotNull(await queue.ClaimNextAsync(
                "worker-retry-1",
                now.AddSeconds(1),
                now.AddMinutes(2),
                cancellationToken));

            var retry = await queue.FailAsync(
                jobId,
                "worker-retry-1",
                "temporary",
                "First attempt failed.",
                now.AddSeconds(2),
                cancellationToken);

            Assert.NotNull(retry);
            Assert.True(retry.RetryScheduled);
            Assert.Equal(JobStatus.Queued, retry.Status);
            Assert.Equal(1, retry.RetryCount);

            Assert.NotNull(await queue.ClaimNextAsync(
                "worker-retry-2",
                now.AddSeconds(3),
                now.AddMinutes(3),
                cancellationToken));

            var exhausted = await queue.FailAsync(
                jobId,
                "worker-retry-2",
                "permanent",
                "Second attempt failed.",
                now.AddSeconds(4),
                cancellationToken);

            Assert.NotNull(exhausted);
            Assert.False(exhausted.RetryScheduled);
            Assert.Equal(JobStatus.Failed, exhausted.Status);
            Assert.Equal(1, exhausted.RetryCount);

            await using var context = await factory.CreateDbContextAsync(cancellationToken);
            var failed = await context.Jobs.AsNoTracking()
                .SingleAsync(job => job.Id == jobId, cancellationToken);
            Assert.Equal(JobStatus.Failed, failed.Status);
            Assert.Equal("permanent", failed.ErrorCode);
            Assert.NotNull(failed.CompletedAt);
        }
        finally
        {
            await CleanupAsync(factory, projectId, cancellationToken);
        }
    }

    [Fact]
    public async Task ExpiredLease_IsRecoveredAndConsumesRetry()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (factory, queue) = CreateQueue();
        var (projectId, jobId, now) = await SeedJobAsync(factory, maxRetries: 1, cancellationToken);

        try
        {
            Assert.NotNull(await queue.ClaimNextAsync(
                "crashed-worker",
                now.AddSeconds(1),
                now.AddSeconds(4),
                cancellationToken));

            var recovered = await queue.ClaimNextAsync(
                "recovery-worker",
                now.AddSeconds(5),
                now.AddMinutes(2),
                cancellationToken);

            Assert.NotNull(recovered);
            Assert.Equal(jobId, recovered.Id);
            Assert.True(recovered.RecoveredFromExpiredLease);
            Assert.Equal(1, recovered.RetryCount);

            await using var context = await factory.CreateDbContextAsync(cancellationToken);
            var persisted = await context.Jobs.AsNoTracking()
                .SingleAsync(job => job.Id == jobId, cancellationToken);
            Assert.Equal(JobStatus.Running, persisted.Status);
            Assert.Equal("recovery-worker", persisted.WorkerId);
            Assert.Equal(1, persisted.RetryCount);
        }
        finally
        {
            await CleanupAsync(factory, projectId, cancellationToken);
        }
    }

    [Fact]
    public async Task ExpiredLease_WithNoRetriesRemaining_BecomesFailed()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (factory, queue) = CreateQueue();
        var (projectId, jobId, now) = await SeedJobAsync(factory, maxRetries: 0, cancellationToken);

        try
        {
            Assert.NotNull(await queue.ClaimNextAsync(
                "crashed-worker",
                now.AddSeconds(1),
                now.AddSeconds(4),
                cancellationToken));

            var recovered = await queue.ClaimNextAsync(
                "other-worker",
                now.AddSeconds(5),
                now.AddMinutes(2),
                cancellationToken);

            Assert.Null(recovered);

            await using var context = await factory.CreateDbContextAsync(cancellationToken);
            var failed = await context.Jobs.AsNoTracking()
                .SingleAsync(job => job.Id == jobId, cancellationToken);
            Assert.Equal(JobStatus.Failed, failed.Status);
            Assert.Equal("lease_expired", failed.ErrorCode);
            Assert.NotNull(failed.CompletedAt);
        }
        finally
        {
            await CleanupAsync(factory, projectId, cancellationToken);
        }
    }

    private static (TestDbContextFactory Factory, PostgreSqlJobQueue Queue) CreateQueue()
    {
        var connectionString =
            Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection");
        Assert.False(
            string.IsNullOrWhiteSpace(connectionString),
            "Set ConnectionStrings__DefaultConnection before running integration tests.");

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(connectionString)
            .Options;
        var factory = new TestDbContextFactory(options);
        return (factory, new PostgreSqlJobQueue(factory));
    }

    private static async Task<(Guid ProjectId, Guid JobId, DateTimeOffset Now)> SeedJobAsync(
        IDbContextFactory<ApplicationDbContext> factory,
        int maxRetries,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var project = ContentProject.Create(
            $"Queue test {Guid.NewGuid():N}",
            "Disposable worker integration-test row.",
            now);
        var job = Job.Create(
            project.Id,
            JobType.GenerateIdea,
            $"queue-{Guid.NewGuid():N}",
            """{"topic":"worker safety"}""",
            now,
            maxRetries);

        await using var context = await factory.CreateDbContextAsync(cancellationToken);
        context.ContentProjects.Add(project);
        context.Jobs.Add(job);
        await context.SaveChangesAsync(cancellationToken);
        return (project.Id, job.Id, now);
    }

    private static async Task CleanupAsync(
        IDbContextFactory<ApplicationDbContext> factory,
        Guid projectId,
        CancellationToken cancellationToken)
    {
        await using var context = await factory.CreateDbContextAsync(cancellationToken);
        await context.Jobs
            .Where(job => job.ContentProjectId == projectId)
            .ExecuteDeleteAsync(cancellationToken);
        await context.ContentProjects
            .Where(project => project.Id == projectId)
            .ExecuteDeleteAsync(cancellationToken);
    }

    private sealed class TestDbContextFactory(
        DbContextOptions<ApplicationDbContext> options)
        : IDbContextFactory<ApplicationDbContext>
    {
        public ApplicationDbContext CreateDbContext() => new(options);
        public Task<ApplicationDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) => Task.FromResult(new ApplicationDbContext(options));
    }
}
