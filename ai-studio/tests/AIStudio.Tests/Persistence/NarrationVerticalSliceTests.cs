using AIStudio.Application.Narration;
using AIStudio.Domain.Assets;
using AIStudio.Domain.Content;
using AIStudio.Domain.Jobs;
using AIStudio.Infrastructure.Assets;
using AIStudio.Infrastructure.Content;
using AIStudio.Infrastructure.Jobs;
using AIStudio.Infrastructure.Narration;
using AIStudio.Infrastructure.Persistence;
using AIStudio.Tests.Jobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;

namespace AIStudio.Tests.Persistence;

public sealed class NarrationVerticalSliceTests
{
    [Fact]
    public async Task Register_PersistsAndReloadsNarrationMetadata()
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
            $"Narration integration {Guid.NewGuid():N}",
            "Disposable narration vertical-slice row.",
            now);
        var job = Job.Create(
            project.Id,
            JobType.GenerateStoryboard,
            $"narration-{Guid.NewGuid():N}",
            GenerateStoryboardTestData.ValidPayload(project.Id),
            now,
            maxRetries: 0);
        job.Start("narration-integration", now.AddMinutes(2), now);
        job.Succeed(GenerateStoryboardTestData.ValidResult, now.AddSeconds(1));

        await using (var setup = new ApplicationDbContext(options))
        {
            setup.ContentProjects.Add(project);
            setup.Jobs.Add(job);
            await setup.SaveChangesAsync(cancellationToken);
        }

        var root = Path.Combine(
            Path.GetTempPath(),
            "aistudio-narration-slice-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        await File.WriteAllBytesAsync(
            Path.Combine(root, "narration.wav"),
            [1, 2, 3],
            cancellationToken);

        try
        {
            await using var context = new ApplicationDbContext(options);
            var workflow = new NarrationWorkflow(
                new NarrationRepository(context),
                new ContentProjectReader(factory),
                new JobReader(factory),
                new LocalAssetFileStore(
                    Options.Create(new AssetStorageOptions { RootPath = root })),
                TimeProvider.System);

            var narration = await workflow.RegisterAsync(
                project.Id,
                job.Id,
                new RegisterNarration(
                    "narration.wav",
                    AssetOrigin.Local,
                    null,
                    null,
                    null,
                    null),
                cancellationToken);

            Assert.NotNull(narration);

            await using var verification = new ApplicationDbContext(options);
            var persisted = await verification.NarrationTracks
                .AsNoTracking()
                .SingleAsync(
                    candidate => candidate.ContentProjectId == project.Id,
                    cancellationToken);
            Assert.Equal("narration.wav", persisted.Path);
            Assert.Equal(3, persisted.ByteSize);
            Assert.Equal(AssetOrigin.Local, persisted.Origin);
            Assert.Null(persisted.License);
        }
        finally
        {
            await using var cleanup = new ApplicationDbContext(options);
            await cleanup.NarrationTracks
                .Where(candidate => candidate.ContentProjectId == project.Id)
                .ExecuteDeleteAsync(cancellationToken);
            await cleanup.Jobs
                .Where(candidate => candidate.Id == job.Id)
                .ExecuteDeleteAsync(cancellationToken);
            await cleanup.ContentProjects
                .Where(candidate => candidate.Id == project.Id)
                .ExecuteDeleteAsync(cancellationToken);
            Directory.Delete(root, recursive: true);
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
