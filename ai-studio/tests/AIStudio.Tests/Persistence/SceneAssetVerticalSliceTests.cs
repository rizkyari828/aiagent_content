using AIStudio.Application.Assets;
using AIStudio.Domain.Assets;
using AIStudio.Domain.Content;
using AIStudio.Domain.Jobs;
using AIStudio.Infrastructure.Assets;
using AIStudio.Infrastructure.Content;
using AIStudio.Infrastructure.Jobs;
using AIStudio.Infrastructure.Persistence;
using AIStudio.Tests.Jobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;

namespace AIStudio.Tests.Persistence;

public sealed class SceneAssetVerticalSliceTests
{
    [Fact]
    public async Task Register_PersistsAndReloadsSceneAssetMetadata()
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
            $"Scene asset integration {Guid.NewGuid():N}",
            "Disposable asset vertical-slice row.",
            now);
        var job = Job.Create(
            project.Id,
            JobType.GenerateStoryboard,
            $"scene-asset-{Guid.NewGuid():N}",
            GenerateStoryboardTestData.ValidPayload(project.Id),
            now,
            maxRetries: 0);
        job.Start("scene-asset-integration", now.AddMinutes(2), now);
        job.Succeed(GenerateStoryboardTestData.ValidResult, now.AddSeconds(1));

        await using (var setup = new ApplicationDbContext(options))
        {
            setup.ContentProjects.Add(project);
            setup.Jobs.Add(job);
            await setup.SaveChangesAsync(cancellationToken);
        }

        var root = Path.Combine(
            Path.GetTempPath(),
            "aistudio-asset-slice-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        await File.WriteAllBytesAsync(
            Path.Combine(root, "scene-0.png"),
            [1, 2, 3],
            cancellationToken);

        try
        {
            await using var context = new ApplicationDbContext(options);
            var workflow = new AssetCollectionWorkflow(
                new AssetRepository(context),
                new ContentProjectReader(factory),
                new JobReader(factory),
                new LocalAssetFileStore(
                    Options.Create(new AssetStorageOptions { RootPath = root })),
                TimeProvider.System);

            var asset = await workflow.RegisterAsync(
                project.Id,
                job.Id,
                new RegisterSceneAsset(
                    0,
                    "scene-0.png",
                    AssetType.Image,
                    AssetOrigin.Local,
                    null,
                    null,
                    null,
                    null),
                cancellationToken);

            Assert.NotNull(asset);

            await using var verification = new ApplicationDbContext(options);
            var persisted = await verification.SceneAssets
                .AsNoTracking()
                .SingleAsync(
                    candidate => candidate.ContentProjectId == project.Id,
                    cancellationToken);
            Assert.Equal(0, persisted.SceneIndex);
            Assert.Equal(3, persisted.ByteSize);
            Assert.Equal(AssetOrigin.Local, persisted.Origin);
            Assert.Null(persisted.License);
        }
        finally
        {
            await using var cleanup = new ApplicationDbContext(options);
            await cleanup.SceneAssets
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
