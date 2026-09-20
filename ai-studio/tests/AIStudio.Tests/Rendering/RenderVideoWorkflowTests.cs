using System.IO;
using AIStudio.Application.Content;
using AIStudio.Application.Jobs;
using AIStudio.Application.Jobs.RenderVideo;
using AIStudio.Application.Rendering;
using AIStudio.Application.Rendering.AudioProduction;
using AIStudio.Domain.Assets;
using AIStudio.Domain.Jobs;
using AIStudio.Domain.Narration;
using AIStudio.Infrastructure.Assets;
using AIStudio.Tests.Assets;
using AIStudio.Tests.Jobs;
using Microsoft.Extensions.Options;
using Xunit;

namespace AIStudio.Tests.Rendering;

public sealed class RenderVideoWorkflowTests
{
    [Fact]
    public async Task Enqueue_CreatesRenderVideoJobForCompleteInputs()
    {
        var projectId = Guid.NewGuid();
        var storyboardJob = AssetTestData.StoryboardJob(
            projectId,
            GenerateStoryboardTestData.ValidResult);
        var dbContext = new RecordingDbContext();
        var workflow = CreateWorkflow(
            dbContext,
            projectId,
            storyboardJob,
            [
                RenderVideoTestData.SceneAsset(projectId, storyboardJob.Id, 0, "scene-0.png", [1]),
                RenderVideoTestData.SceneAsset(projectId, storyboardJob.Id, 1, "scene-1.png", [2])
            ],
            RenderVideoTestData.Narration(projectId, storyboardJob.Id, "narration.wav", [3]));

        var jobId = await workflow.EnqueueAsync(
            projectId,
            storyboardJob.Id,
            TestContext.Current.CancellationToken);

        Assert.NotNull(jobId);
        var job = Assert.IsType<Job>(dbContext.AddedJob);
        Assert.Equal(jobId, job.Id);
        Assert.Equal(JobType.RenderVideo, job.Type);
        Assert.Equal(JobStatus.Queued, job.Status);
        Assert.Equal(64, job.InputVersionHash.Length);
        Assert.Equal(1, dbContext.SaveCount);

        var payload = RenderVideoJobPayload.Deserialize(job.Payload);
        Assert.Equal(projectId, payload.ContentProjectId);
        Assert.Equal(storyboardJob.Id, payload.StoryboardJobId);
    }

    [Fact]
    public async Task Enqueue_ReturnsNullWhenProjectMissing()
    {
        var projectId = Guid.NewGuid();
        var storyboardJob = AssetTestData.StoryboardJob(
            projectId,
            GenerateStoryboardTestData.ValidResult);
        var dbContext = new RecordingDbContext();
        var workflow = CreateWorkflow(
            dbContext,
            projectId: null,
            storyboardJob,
            [
                RenderVideoTestData.SceneAsset(projectId, storyboardJob.Id, 0, "scene-0.png", [1]),
                RenderVideoTestData.SceneAsset(projectId, storyboardJob.Id, 1, "scene-1.png", [2])
            ],
            RenderVideoTestData.Narration(projectId, storyboardJob.Id, "narration.wav", [3]));

        var jobId = await workflow.EnqueueAsync(
            projectId,
            storyboardJob.Id,
            TestContext.Current.CancellationToken);

        Assert.Null(jobId);
        Assert.Null(dbContext.AddedJob);
    }

    [Fact]
    public async Task Enqueue_RejectsMissingStoryboard()
    {
        var projectId = Guid.NewGuid();
        var dbContext = new RecordingDbContext();
        var workflow = CreateWorkflow(
            dbContext,
            projectId,
            storyboard: null,
            [],
            narration: null);

        var exception = await Assert.ThrowsAsync<RenderVideoException>(
            () => workflow.EnqueueAsync(
                projectId,
                Guid.NewGuid(),
                TestContext.Current.CancellationToken));

        Assert.Equal("render_storyboard_not_found", exception.ErrorCode);
        Assert.Equal(0, dbContext.SaveCount);
    }

    [Fact]
    public async Task Enqueue_RejectsIncompleteStoryboard()
    {
        var projectId = Guid.NewGuid();
        var storyboardJob = AssetTestData.StoryboardJob(
            projectId,
            GenerateStoryboardTestData.ValidResult,
            JobStatus.Running);
        var dbContext = new RecordingDbContext();
        var workflow = CreateWorkflow(dbContext, projectId, storyboardJob, [], null);

        var exception = await Assert.ThrowsAsync<RenderVideoException>(
            () => workflow.EnqueueAsync(
                projectId,
                storyboardJob.Id,
                TestContext.Current.CancellationToken));

        Assert.Equal("render_storyboard_invalid", exception.ErrorCode);
    }

    [Fact]
    public async Task Enqueue_RejectsIncompleteSceneAssets()
    {
        var projectId = Guid.NewGuid();
        var storyboardJob = AssetTestData.StoryboardJob(
            projectId,
            GenerateStoryboardTestData.ValidResult);
        var dbContext = new RecordingDbContext();
        var workflow = CreateWorkflow(
            dbContext,
            projectId,
            storyboardJob,
            [RenderVideoTestData.SceneAsset(projectId, storyboardJob.Id, 0, "scene-0.png", [1])],
            RenderVideoTestData.Narration(projectId, storyboardJob.Id, "narration.wav", [3]));

        var exception = await Assert.ThrowsAsync<RenderVideoException>(
            () => workflow.EnqueueAsync(
                projectId,
                storyboardJob.Id,
                TestContext.Current.CancellationToken));

        Assert.Equal("render_assets_incomplete", exception.ErrorCode);
    }

    [Fact]
    public async Task Enqueue_RejectsAssetFromAnotherStoryboard()
    {
        var projectId = Guid.NewGuid();
        var storyboardJob = AssetTestData.StoryboardJob(
            projectId,
            GenerateStoryboardTestData.ValidResult);
        var dbContext = new RecordingDbContext();
        var workflow = CreateWorkflow(
            dbContext,
            projectId,
            storyboardJob,
            [
                RenderVideoTestData.SceneAsset(projectId, Guid.NewGuid(), 0, "scene-0.png", [1]),
                RenderVideoTestData.SceneAsset(projectId, storyboardJob.Id, 1, "scene-1.png", [2])
            ],
            RenderVideoTestData.Narration(projectId, storyboardJob.Id, "narration.wav", [3]));

        var exception = await Assert.ThrowsAsync<RenderVideoException>(
            () => workflow.EnqueueAsync(
                projectId,
                storyboardJob.Id,
                TestContext.Current.CancellationToken));

        Assert.Equal("render_asset_storyboard_mismatch", exception.ErrorCode);
    }

    [Fact]
    public async Task Enqueue_RejectsMissingNarration()
    {
        var projectId = Guid.NewGuid();
        var storyboardJob = AssetTestData.StoryboardJob(
            projectId,
            GenerateStoryboardTestData.ValidResult);
        var dbContext = new RecordingDbContext();
        var workflow = CreateWorkflow(
            dbContext,
            projectId,
            storyboardJob,
            [
                RenderVideoTestData.SceneAsset(projectId, storyboardJob.Id, 0, "scene-0.png", [1]),
                RenderVideoTestData.SceneAsset(projectId, storyboardJob.Id, 1, "scene-1.png", [2])
            ],
            narration: null);

        var exception = await Assert.ThrowsAsync<RenderVideoException>(
            () => workflow.EnqueueAsync(
                projectId,
                storyboardJob.Id,
                TestContext.Current.CancellationToken));

        Assert.Equal("render_audio_missing", exception.ErrorCode);
    }

    [Fact]
    public async Task Enqueue_AcceptsMasteredAudioWithoutNarrationTrack()
    {
        var projectId = Guid.NewGuid();
        var storyboardJob = AssetTestData.StoryboardJob(
            projectId,
            GenerateStoryboardTestData.ValidResult);
        var dbContext = new RecordingDbContext();
        var masterBytes = new byte[] { 1, 2, 3 };
        var workspace = await SeedMasterAsync(projectId, storyboardJob.Id, masterBytes);
        var workflow = CreateWorkflow(
            dbContext,
            projectId,
            storyboardJob,
            [
                RenderVideoTestData.SceneAsset(projectId, storyboardJob.Id, 0, "scene-0.png", [1]),
                RenderVideoTestData.SceneAsset(projectId, storyboardJob.Id, 1, "scene-1.png", [2])
            ],
            narration: null,
            workspace);

        var jobId = await workflow.EnqueueAsync(
            projectId,
            storyboardJob.Id,
            TestContext.Current.CancellationToken);

        Assert.NotNull(jobId);
        var job = Assert.IsType<Job>(dbContext.AddedJob);
        Assert.Equal(JobType.RenderVideo, job.Type);
    }

    private async Task<AudioProductionWorkspace> SeedMasterAsync(
        Guid projectId,
        Guid storyboardJobId,
        byte[] masterBytes)
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "aistudio-render-workflow-tests",
            Guid.NewGuid().ToString("N"));
        var storage = Options.Create(new AssetStorageOptions { RootPath = root });
        var masterRelative = AudioProductionWorkspace.FilePath(
            projectId,
            storyboardJobId,
            AudioProductionWorkspace.MasterFileName);
        var masterPath = Path.Combine(root, masterRelative);
        Directory.CreateDirectory(Path.GetDirectoryName(masterPath)!);
        File.WriteAllBytes(masterPath, masterBytes);

        var workspace = new AudioProductionWorkspace(
            new LocalAssetFileStore(storage),
            FakeMediaInspector.Returning(new MediaInspection(12, false, true, false, 0, 0, 0, 0)));

        await workspace.SaveManifestAsync(
            projectId,
            storyboardJobId,
            new AudioProductionManifest(
                AudioProductionWorkspace.Version,
                new Dictionary<string, AudioProductionStage>
                {
                    [AudioProductionWorkspace.MasterStage] = new AudioProductionStage(
                        "master-fingerprint",
                        masterRelative,
                        RenderVideoTestData.Hash(masterBytes),
                        masterBytes.Length,
                        12,
                        0,
                        0)
                }),
            TestContext.Current.CancellationToken);

        return workspace;
    }

    private static RenderVideoWorkflow CreateWorkflow(
        RecordingDbContext dbContext,
        Guid? projectId,
        JobSnapshot? storyboard,
        IReadOnlyList<SceneAsset> assets,
        NarrationTrack? narration,
        AudioProductionWorkspace? workspace = null) =>
        new(
            dbContext,
            new StubContentProjectReader(
                projectId is null
                    ? null
                    : new ContentProjectSnapshot(projectId.Value, "Project", "Brief")),
            new StubJobReader(storyboard),
            new StubAssetRepository([.. assets]),
            new StubNarrationRepository(narration),
            workspace ?? new AudioProductionWorkspace(
                new LocalAssetFileStore(Options.Create(new AssetStorageOptions
                {
                    RootPath = Path.Combine(
                        Path.GetTempPath(),
                        "aistudio-render-workflow-tests",
                        Guid.NewGuid().ToString("N"))
                })),
                FakeMediaInspector.Returning(new MediaInspection(12, false, true, false, 0, 0, 0, 0))),
            new AssetStubTimeProvider(AssetTestData.Now));
}
