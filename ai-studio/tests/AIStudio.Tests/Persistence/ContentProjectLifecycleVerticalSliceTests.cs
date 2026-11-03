using AIStudio.Application.Approvals;
using AIStudio.Application.Content;
using AIStudio.Application.Jobs;
using AIStudio.Domain.Content;
using AIStudio.Domain.Jobs;
using AIStudio.Infrastructure.Approvals;
using AIStudio.Infrastructure.Assets;
using AIStudio.Infrastructure.Content;
using AIStudio.Infrastructure.Jobs;
using AIStudio.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;

namespace AIStudio.Tests.Persistence;

public sealed class ContentProjectLifecycleVerticalSliceTests
{
    [Fact]
    public async Task ReadUpdateReadinessAndCancel_PersistThroughPostgreSql()
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
        var olderProject = ContentProject.Create(
            "Lifecycle older project",
            "First disposable lifecycle row.",
            now.AddMinutes(-10));
        var newerProject = ContentProject.Create(
            "Lifecycle newer project",
            "Second disposable lifecycle row.",
            now.AddMinutes(-5));
        var ideaJob = Job.Create(
            olderProject.Id,
            JobType.GenerateIdea,
            "lifecycle-idea",
            """{"topic":"lifecycle"}""",
            now.AddMinutes(-9),
            maxRetries: 0);
        ideaJob.Start("lifecycle-slice", now.AddMinutes(-6), now.AddMinutes(-8));
        ideaJob.Succeed("""{"title":"Idea"}""", now.AddMinutes(-7));
        var queuedJob = Job.Create(
            newerProject.Id,
            JobType.GenerateScript,
            "lifecycle-script",
            """{"selectedIdea":{}}""",
            now.AddMinutes(-4),
            maxRetries: 1);

        var artifactRoot = Path.Combine(
            Path.GetTempPath(),
            "aistudio-content-slice-tests",
            Guid.NewGuid().ToString("N"));

        try
        {
            await using (var setup = new ApplicationDbContext(options))
            {
                setup.ContentProjects.Add(olderProject);
                setup.ContentProjects.Add(newerProject);
                setup.Jobs.Add(ideaJob);
                setup.Jobs.Add(queuedJob);
                await setup.SaveChangesAsync(cancellationToken);
            }

            await using (var context = new ApplicationDbContext(options))
            {
                var projectWorkflow = new ContentProjectWorkflow(
                    new ContentProjectRepository(context),
                    new ProjectJobReader(factory),
                    TimeProvider.System);

                var listed = await projectWorkflow.ListAsync(1, cancellationToken);
                Assert.Equal(newerProject.Id, Assert.Single(listed).Id);

                var details = await projectWorkflow.FindAsync(
                    olderProject.Id,
                    cancellationToken);
                Assert.NotNull(details);
                Assert.Equal("GenerateIdea", details.LastJob!.Type.ToString());
                Assert.Equal(JobStatus.Succeeded, details.LastJob.Status);
                Assert.Equal("First disposable lifecycle row.", details.Brief);

                var updated = await projectWorkflow.UpdateAsync(
                    olderProject.Id,
                    "  Lifecycle renamed  ",
                    "  Lifecycle brief updated  ",
                    cancellationToken);
                Assert.NotNull(updated);
                Assert.Equal("Lifecycle renamed", updated.Title);
                Assert.Equal("Lifecycle brief updated", updated.Brief);
                Assert.True(updated.UpdatedAt > olderProject.CreatedAt);
            }

            await using (var context = new ApplicationDbContext(options))
            {
                var readinessWorkflow = new ContentReadinessWorkflow(
                    new ContentProjectRepository(context),
                    new ProjectJobReader(factory),
                    new FinalApprovalWorkflow(
                        new FinalApprovalRepository(context),
                        new ContentProjectReader(factory),
                        new JobReader(factory),
                        new LocalAssetFileStore(
                            Options.Create(new AssetStorageOptions
                            {
                                RootPath = artifactRoot
                            })),
                        TimeProvider.System),
                    new PublicationRepository(context),
                    TimeProvider.System);

                var readiness = await readinessWorkflow.EvaluateAsync(
                    olderProject.Id,
                    cancellationToken);
                Assert.NotNull(readiness);
                Assert.Equal(ContentReadinessStatus.InProgress, readiness.Status);
                Assert.Equal(
                    ContentReadinessCodes.RenderMissing,
                    Assert.Single(readiness.Reasons).Code);

                var started = await readinessWorkflow.EvaluateAsync(
                    newerProject.Id,
                    cancellationToken);
                Assert.NotNull(started);
                Assert.Equal(ContentReadinessStatus.InProgress, started.Status);
            }

            var cancelResult = await new PostgreSqlJobQueue(factory).CancelAsync(
                queuedJob.Id,
                now.AddMinutes(-3),
                cancellationToken);
            Assert.Equal(JobCancelOutcome.Cancelled, cancelResult.Outcome);

            await using (var verification = new ApplicationDbContext(options))
            {
                var persisted = await verification.ContentProjects
                    .AsNoTracking()
                    .SingleAsync(project => project.Id == olderProject.Id, cancellationToken);
                Assert.Equal("Lifecycle renamed", persisted.Title);
                Assert.Equal("Lifecycle brief updated", persisted.Brief);
                Assert.Equal(ContentStatus.Draft, persisted.Status);
                Assert.True(persisted.UpdatedAt > now.AddMinutes(-10));

                var cancelled = await verification.Jobs
                    .AsNoTracking()
                    .SingleAsync(job => job.Id == queuedJob.Id, cancellationToken);
                Assert.Equal(JobStatus.Cancelled, cancelled.Status);
                Assert.NotNull(cancelled.CompletedAt);
                Assert.Null(cancelled.LeaseExpiresAt);
            }
        }
        finally
        {
            await using var cleanup = new ApplicationDbContext(options);
            await cleanup.Jobs
                .Where(job => job.ContentProjectId == olderProject.Id
                    || job.ContentProjectId == newerProject.Id)
                .ExecuteDeleteAsync(cancellationToken);
            await cleanup.ContentProjects
                .Where(project => project.Id == olderProject.Id
                    || project.Id == newerProject.Id)
                .ExecuteDeleteAsync(cancellationToken);

            if (Directory.Exists(artifactRoot))
            {
                Directory.Delete(artifactRoot, recursive: true);
            }
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
