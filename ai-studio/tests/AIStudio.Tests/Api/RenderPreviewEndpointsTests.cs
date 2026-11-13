using System.Security.Cryptography;
using AIStudio.Api.Endpoints;
using AIStudio.Application.Abstractions.Persistence;
using AIStudio.Application.Assets;
using AIStudio.Application.Content;
using AIStudio.Application.Jobs;
using AIStudio.Application.Jobs.RenderVideo;
using AIStudio.Application.Narration;
using AIStudio.Application.Rendering;
using AIStudio.Application.Rendering.AudioProduction;
using AIStudio.Domain.Assets;
using AIStudio.Domain.Content;
using AIStudio.Domain.Jobs;
using AIStudio.Domain.Narration;
using AIStudio.Infrastructure.Assets;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;
using Xunit;

namespace AIStudio.Tests.Api;

public sealed class RenderPreviewEndpointsTests : IDisposable
{
    private readonly string rootPath = Path.Combine(
        Path.GetTempPath(),
        $"aistudio-render-preview-{Guid.NewGuid():N}");

    [Fact]
    public async Task GetRenderPreview_ReturnsRangeEnabledMp4ForVerifiedRenderJob()
    {
        var projectId = Guid.NewGuid();
        var renderJobId = Guid.NewGuid();
        var storyboardJobId = Guid.NewGuid();
        var fileStore = CreateFileStore();
        var mp4Bytes = new byte[] { 0x00, 0x00, 0x00, 0x18, 0x66, 0x74, 0x79, 0x70 };
        var stored = await fileStore.WriteAsync(
            $"renders/{renderJobId:N}.mp4",
            mp4Bytes,
            TestContext.Current.CancellationToken);

        var renderResult = new RenderVideoResult(
            stored.RelativePath,
            stored.ContentHash,
            stored.ByteSize,
            DurationSeconds: 12.5,
            Width: 1080,
            Height: 1920,
            StoryboardJobId: storyboardJobId,
            NarrationTrackId: Guid.NewGuid(),
            SceneCount: 2,
            NarrationContentHash: new string('a', 64));

        var workflow = CreateWorkflow(
            new ContentProjectSnapshot(projectId, "Demo", "Brief"),
            CreateJob(renderJobId, projectId, JobType.RenderVideo, JobStatus.Succeeded, renderResult.Serialize()),
            fileStore);

        var result = await RenderEndpoints.GetRenderPreviewAsync(
            projectId.ToString(),
            renderJobId.ToString(),
            workflow,
            fileStore,
            TestContext.Current.CancellationToken);

        var fileResult = Assert.IsType<PhysicalFileHttpResult>(result);
        Assert.Equal("video/mp4", fileResult.ContentType);
        Assert.True(fileResult.EnableRangeProcessing);
        Assert.Equal(stored.AbsolutePath, fileResult.FileName);
    }

    [Theory]
    [InlineData("not-a-guid", "11111111-1111-1111-1111-111111111111")]
    [InlineData("11111111-1111-1111-1111-111111111111", "00000000-0000-0000-0000-000000000000")]
    public async Task GetRenderPreview_RejectsInvalidIdentifiers(
        string projectId,
        string renderJobId)
    {
        var fileStore = CreateFileStore();
        var workflow = CreateWorkflow(project: null, job: null, fileStore);

        var result = await RenderEndpoints.GetRenderPreviewAsync(
            projectId,
            renderJobId,
            workflow,
            fileStore,
            TestContext.Current.CancellationToken);

        var validation = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(400, validation.StatusCode);
    }

    [Fact]
    public async Task GetRenderPreview_ReturnsNotFoundWhenJobBelongsToAnotherProject()
    {
        var projectId = Guid.NewGuid();
        var otherProjectId = Guid.NewGuid();
        var renderJobId = Guid.NewGuid();
        var fileStore = CreateFileStore();
        var workflow = CreateWorkflow(
            new ContentProjectSnapshot(projectId, "Demo", "Brief"),
            CreateJob(renderJobId, otherProjectId, JobType.RenderVideo, JobStatus.Succeeded, "{}"),
            fileStore);

        var result = await RenderEndpoints.GetRenderPreviewAsync(
            projectId.ToString(),
            renderJobId.ToString(),
            workflow,
            fileStore,
            TestContext.Current.CancellationToken);

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(404, problem.StatusCode);
    }

    [Theory]
    [InlineData(JobType.GenerateStoryboard, JobStatus.Succeeded)]
    [InlineData(JobType.RenderVideo, JobStatus.Running)]
    public async Task GetRenderPreview_ReturnsConflictWhenJobTypeOrStatusIsNotReady(
        JobType type,
        JobStatus status)
    {
        var projectId = Guid.NewGuid();
        var jobId = Guid.NewGuid();
        var fileStore = CreateFileStore();
        var workflow = CreateWorkflow(
            new ContentProjectSnapshot(projectId, "Demo", "Brief"),
            CreateJob(jobId, projectId, type, status, null),
            fileStore);

        var result = await RenderEndpoints.GetRenderPreviewAsync(
            projectId.ToString(),
            jobId.ToString(),
            workflow,
            fileStore,
            TestContext.Current.CancellationToken);

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(409, problem.StatusCode);
    }

    [Fact]
    public async Task GetRenderPreview_ReturnsNotFoundWhenRenderedFileIsMissing()
    {
        var projectId = Guid.NewGuid();
        var renderJobId = Guid.NewGuid();
        var fileStore = CreateFileStore();
        var renderResult = new RenderVideoResult(
            "renders/missing.mp4",
            new string('b', 64),
            1024,
            DurationSeconds: 10,
            Width: 1080,
            Height: 1920,
            StoryboardJobId: Guid.NewGuid(),
            NarrationTrackId: Guid.NewGuid(),
            SceneCount: 1,
            NarrationContentHash: new string('a', 64));

        var workflow = CreateWorkflow(
            new ContentProjectSnapshot(projectId, "Demo", "Brief"),
            CreateJob(renderJobId, projectId, JobType.RenderVideo, JobStatus.Succeeded, renderResult.Serialize()),
            fileStore);

        var result = await RenderEndpoints.GetRenderPreviewAsync(
            projectId.ToString(),
            renderJobId.ToString(),
            workflow,
            fileStore,
            TestContext.Current.CancellationToken);

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(404, problem.StatusCode);
    }

    [Fact]
    public async Task GetRenderPreview_ReturnsConflictWhenHashMismatches()
    {
        var projectId = Guid.NewGuid();
        var renderJobId = Guid.NewGuid();
        var fileStore = CreateFileStore();
        var stored = await fileStore.WriteAsync(
            $"renders/{renderJobId:N}.mp4",
            [1, 2, 3, 4],
            TestContext.Current.CancellationToken);

        var renderResult = new RenderVideoResult(
            stored.RelativePath,
            Convert.ToHexString(SHA256.HashData([9, 9, 9, 9])).ToLowerInvariant(),
            stored.ByteSize,
            DurationSeconds: 10,
            Width: 1080,
            Height: 1920,
            StoryboardJobId: Guid.NewGuid(),
            NarrationTrackId: Guid.NewGuid(),
            SceneCount: 1,
            NarrationContentHash: new string('a', 64));

        var workflow = CreateWorkflow(
            new ContentProjectSnapshot(projectId, "Demo", "Brief"),
            CreateJob(renderJobId, projectId, JobType.RenderVideo, JobStatus.Succeeded, renderResult.Serialize()),
            fileStore);

        var result = await RenderEndpoints.GetRenderPreviewAsync(
            projectId.ToString(),
            renderJobId.ToString(),
            workflow,
            fileStore,
            TestContext.Current.CancellationToken);

        var problem = Assert.IsType<ProblemHttpResult>(result);
        Assert.Equal(409, problem.StatusCode);
    }

    public void Dispose()
    {
        if (Directory.Exists(rootPath))
        {
            Directory.Delete(rootPath, recursive: true);
        }
    }

    private LocalAssetFileStore CreateFileStore()
    {
        Directory.CreateDirectory(rootPath);
        return new LocalAssetFileStore(
            Options.Create(new AssetStorageOptions { RootPath = rootPath }));
    }

    private static RenderVideoWorkflow CreateWorkflow(
        ContentProjectSnapshot? project,
        JobSnapshot? job,
        IAssetFileStore fileStore) =>
        new(
            new StubDbContext(),
            new StubContentProjectReader(project),
            new StubJobReader(job),
            new StubAssetRepository(),
            new StubNarrationRepository(),
            new AudioProductionWorkspace(fileStore, new StubAudioInspector()),
            TimeProvider.System);

    private static JobSnapshot CreateJob(
        Guid jobId,
        Guid projectId,
        JobType type,
        JobStatus status,
        string? result)
    {
        var now = DateTimeOffset.UtcNow;
        return new JobSnapshot(
            jobId,
            projectId,
            type,
            status,
            0,
            3,
            result,
            null,
            null,
            now,
            now,
            now,
            status == JobStatus.Succeeded ? now : null);
    }

    private sealed class StubDbContext : IApplicationDbContext
    {
        public void Add(ContentProject project)
        {
        }

        public void Add(Job job)
        {
        }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(0);
    }

    private sealed class StubContentProjectReader(ContentProjectSnapshot? project)
        : IContentProjectReader
    {
        public Task<ContentProjectSnapshot?> FindByIdAsync(
            Guid id,
            CancellationToken cancellationToken) =>
            Task.FromResult(project?.Id == id ? project : null);
    }

    private sealed class StubJobReader(JobSnapshot? job) : IJobReader
    {
        public Task<JobSnapshot?> FindByIdAsync(
            Guid id,
            CancellationToken cancellationToken) =>
            Task.FromResult(job?.Id == id ? job : null);
    }

    private sealed class StubAssetRepository : IAssetRepository
    {
        public Task<SceneAsset?> FindBySceneAsync(
            Guid contentProjectId,
            int sceneIndex,
            CancellationToken cancellationToken) =>
            Task.FromResult<SceneAsset?>(null);

        public Task<IReadOnlyList<SceneAsset>> ListByProjectAsync(
            Guid contentProjectId,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<SceneAsset>>([]);

        public void Add(SceneAsset asset)
        {
        }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken) =>
            Task.FromResult(0);
    }

    private sealed class StubNarrationRepository : INarrationRepository
    {
        public Task<NarrationTrack?> FindByProjectIdAsync(
            Guid contentProjectId,
            CancellationToken cancellationToken) =>
            Task.FromResult<NarrationTrack?>(null);

        public void Add(NarrationTrack track)
        {
        }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken) =>
            Task.FromResult(0);
    }

    private sealed class StubAudioInspector : IMediaInspector
    {
        public Task<MediaInspection> InspectAsync(
            string absolutePath,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
