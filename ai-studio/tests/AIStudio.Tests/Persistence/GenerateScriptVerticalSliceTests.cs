using AIStudio.Application.Jobs.GenerateScript;
using AIStudio.Domain.Content;
using AIStudio.Domain.Jobs;
using AIStudio.Infrastructure.Content;
using AIStudio.Infrastructure.Jobs;
using AIStudio.Infrastructure.Persistence;
using AIStudio.Tests.Jobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AIStudio.Tests.Persistence;

public sealed class GenerateScriptVerticalSliceTests
{
    [Fact]
    public async Task WorkerPipeline_PersistsStructuredGenerateScriptResult()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var connectionString =
            Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection");
        Assert.False(
            string.IsNullOrWhiteSpace(connectionString),
            "Set ConnectionStrings__DefaultConnection before running integration tests.");

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(connectionString)
            .Options;
        var factory = new TestDbContextFactory(options);
        var now = DateTimeOffset.UtcNow;
        var project = ContentProject.Create(
            $"GenerateScript integration {Guid.NewGuid():N}",
            "Disposable script vertical-slice row.",
            now);
        var job = Job.Create(
            project.Id,
            JobType.GenerateScript,
            $"generate-script-{Guid.NewGuid():N}",
            GenerateScriptTestData.ValidPayload(project.Id),
            now,
            maxRetries: 0);

        await using (var setup = new ApplicationDbContext(options))
        {
            var eligibleJobExists = await setup.Jobs.AnyAsync(
                candidate =>
                    candidate.Status == JobStatus.Queued
                    || (candidate.Status == JobStatus.Running
                        && candidate.LeaseExpiresAt != null
                        && candidate.LeaseExpiresAt <= now),
                cancellationToken);
            Assert.False(
                eligibleJobExists,
                "Integration database contains another claimable job; refusing to process it.");

            setup.ContentProjects.Add(project);
            setup.Jobs.Add(job);
            await setup.SaveChangesAsync(cancellationToken);
        }

        try
        {
            var queue = new PostgreSqlJobQueue(factory);
            var handler = new GenerateScriptJobHandler(
                new ContentProjectReader(factory),
                new RecordingTextGenerator(GenerateScriptTestData.ValidResult));
            var processor = new JobProcessor(
                queue,
                [handler],
                TimeProvider.System,
                NullLogger<JobProcessor>.Instance);

            var processed = await processor.ProcessNextAsync(
                "generate-script-integration",
                TimeSpan.FromMinutes(1),
                cancellationToken);

            Assert.True(processed);

            await using var verification = new ApplicationDbContext(options);
            var persisted = await verification.Jobs
                .AsNoTracking()
                .SingleAsync(candidate => candidate.Id == job.Id, cancellationToken);
            Assert.Equal(JobStatus.Succeeded, persisted.Status);
            Assert.NotNull(persisted.Result);

            var result = GenerateScriptResult.Deserialize(persisted.Result);
            Assert.Equal("Local AI Tutorial", result.Title);
            Assert.Equal(2, result.Sections.Count);
        }
        finally
        {
            await using var cleanup = new ApplicationDbContext(options);
            await cleanup.Jobs
                .Where(candidate => candidate.Id == job.Id)
                .ExecuteDeleteAsync(cancellationToken);
            await cleanup.ContentProjects
                .Where(candidate => candidate.Id == project.Id)
                .ExecuteDeleteAsync(cancellationToken);
        }
    }

    private sealed class TestDbContextFactory(
        DbContextOptions<ApplicationDbContext> options)
        : IDbContextFactory<ApplicationDbContext>
    {
        public ApplicationDbContext CreateDbContext() => new(options);

        public Task<ApplicationDbContext> CreateDbContextAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ApplicationDbContext(options));
    }
}
