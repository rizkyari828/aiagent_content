using AIStudio.Application.Content;
using AIStudio.Application.IdentityAssets;
using AIStudio.Application.Jobs;
using AIStudio.Application.Jobs.GenerateSceneVisuals;
using AIStudio.Domain.Jobs;
using AIStudio.Tests.Assets;
using AIStudio.Tests.Jobs;
using Xunit;

namespace AIStudio.Tests.Rendering;

public sealed class GenerateSceneVisualsWorkflowTests
{
    [Fact]
    public async Task Enqueue_CreatesJobForCompletedStoryboard()
    {
        var projectId = Guid.NewGuid();
        var storyboard = AssetTestData.StoryboardJob(
            projectId,
            GenerateStoryboardTestData.ValidResult);
        var dbContext = new RecordingDbContext();
        var workflow = CreateWorkflow(projectId, storyboard, dbContext);

        var jobId = await workflow.EnqueueAsync(
            projectId,
            storyboard.Id,
            TestContext.Current.CancellationToken);

        Assert.NotNull(jobId);
        Assert.NotNull(dbContext.AddedJob);
        Assert.Equal(JobType.GenerateSceneVisuals, dbContext.AddedJob!.Type);
        Assert.Equal(projectId, dbContext.AddedJob.ContentProjectId);
        Assert.Equal(1, dbContext.SaveCount);
    }

    [Fact]
    public async Task Enqueue_ReturnsNullWhenProjectMissing()
    {
        var projectId = Guid.NewGuid();
        var workflow = new GenerateSceneVisualsWorkflow(
            new RecordingDbContext(),
            new StubContentProjectReader(null),
            new StubJobReader(null),
            new IdentityAssetResolver(new IdentityAssetRegistry()),
            new AssetStubTimeProvider(AssetTestData.Now));

        var jobId = await workflow.EnqueueAsync(
            projectId,
            Guid.NewGuid(),
            TestContext.Current.CancellationToken);

        Assert.Null(jobId);
    }

    [Fact]
    public async Task Enqueue_RejectsMissingStoryboard()
    {
        var projectId = Guid.NewGuid();
        var workflow = CreateWorkflow(projectId, storyboard: null, new RecordingDbContext());

        var exception = await Assert.ThrowsAsync<SceneVisualGenerationException>(
            () => workflow.EnqueueAsync(
                projectId,
                Guid.NewGuid(),
                TestContext.Current.CancellationToken));

        Assert.Equal("visual_storyboard_not_found", exception.ErrorCode);
    }

    [Fact]
    public async Task Enqueue_RejectsIncompleteStoryboard()
    {
        var projectId = Guid.NewGuid();
        var storyboard = AssetTestData.StoryboardJob(
            projectId,
            GenerateStoryboardTestData.ValidResult,
            status: JobStatus.Running);
        var workflow = CreateWorkflow(projectId, storyboard, new RecordingDbContext());

        var exception = await Assert.ThrowsAsync<SceneVisualGenerationException>(
            () => workflow.EnqueueAsync(
                projectId,
                storyboard.Id,
                TestContext.Current.CancellationToken));

        Assert.Equal("visual_storyboard_invalid", exception.ErrorCode);
    }

    private static GenerateSceneVisualsWorkflow CreateWorkflow(
        Guid projectId,
        JobSnapshot? storyboard,
        RecordingDbContext dbContext) =>
        new(
            dbContext,
            new StubContentProjectReader(
                new ContentProjectSnapshot(projectId, "Project", "Brief")),
            new StubJobReader(storyboard),
            new IdentityAssetResolver(new IdentityAssetRegistry()),
            new AssetStubTimeProvider(AssetTestData.Now));
}
