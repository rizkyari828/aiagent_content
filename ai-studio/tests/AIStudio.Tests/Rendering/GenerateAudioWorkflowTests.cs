using AIStudio.Application.Content;
using AIStudio.Application.Jobs;
using AIStudio.Application.Jobs.GenerateAudio;
using AIStudio.Application.Rendering.AudioProduction;
using AIStudio.Domain.Jobs;
using AIStudio.Infrastructure.Assets;
using AIStudio.Tests.Assets;
using AIStudio.Tests.Jobs;
using Microsoft.Extensions.Options;
using Xunit;

namespace AIStudio.Tests.Rendering;

public sealed class GenerateAudioWorkflowTests
{
    [Fact]
    public async Task Enqueue_CreatesGenerateAudioJobForCompletedStoryboard()
    {
        var projectId = Guid.NewGuid();
        var storyboard = AssetTestData.StoryboardJob(
            projectId,
            GenerateStoryboardTestData.ValidResult);
        var dbContext = new RecordingDbContext();
        var workflow = CreateWorkflow(dbContext, projectId, storyboard);

        var jobId = await workflow.EnqueueAsync(
            projectId,
            storyboard.Id,
            TestContext.Current.CancellationToken);

        Assert.NotNull(jobId);
        var job = Assert.IsType<Job>(dbContext.AddedJob);
        Assert.Equal(jobId, job.Id);
        Assert.Equal(JobType.GenerateAudio, job.Type);
        Assert.Equal(JobStatus.Queued, job.Status);
        Assert.Equal(64, job.InputVersionHash.Length);
        Assert.Equal(1, dbContext.SaveCount);

        var payload = GenerateAudioJobPayload.Deserialize(job.Payload);
        Assert.Equal(projectId, payload.ContentProjectId);
        Assert.Equal(storyboard.Id, payload.StoryboardJobId);
    }

    [Fact]
    public async Task Enqueue_ReturnsNullWhenProjectMissing()
    {
        var projectId = Guid.NewGuid();
        var storyboard = AssetTestData.StoryboardJob(
            projectId,
            GenerateStoryboardTestData.ValidResult);
        var dbContext = new RecordingDbContext();
        var workflow = CreateWorkflow(dbContext, projectId: null, storyboard);

        var jobId = await workflow.EnqueueAsync(
            projectId,
            storyboard.Id,
            TestContext.Current.CancellationToken);

        Assert.Null(jobId);
        Assert.Null(dbContext.AddedJob);
    }

    [Fact]
    public async Task Enqueue_RejectsMissingStoryboard()
    {
        var projectId = Guid.NewGuid();
        var dbContext = new RecordingDbContext();
        var workflow = CreateWorkflow(dbContext, projectId, storyboard: null);

        var exception = await Assert.ThrowsAsync<AudioProductionException>(
            () => workflow.EnqueueAsync(
                projectId,
                Guid.NewGuid(),
                TestContext.Current.CancellationToken));

        Assert.Equal("audio_storyboard_not_found", exception.ErrorCode);
        Assert.Equal(0, dbContext.SaveCount);
    }

    [Fact]
    public async Task Enqueue_RejectsIncompleteStoryboard()
    {
        var projectId = Guid.NewGuid();
        var storyboard = AssetTestData.StoryboardJob(
            projectId,
            GenerateStoryboardTestData.ValidResult,
            JobStatus.Running);
        var dbContext = new RecordingDbContext();
        var workflow = CreateWorkflow(dbContext, projectId, storyboard);

        var exception = await Assert.ThrowsAsync<AudioProductionException>(
            () => workflow.EnqueueAsync(
                projectId,
                storyboard.Id,
                TestContext.Current.CancellationToken));

        Assert.Equal("audio_storyboard_invalid", exception.ErrorCode);
    }

    private static GenerateAudioWorkflow CreateWorkflow(
        RecordingDbContext dbContext,
        Guid? projectId,
        JobSnapshot? storyboard) =>
        new(
            dbContext,
            new StubContentProjectReader(
                projectId is null
                    ? null
                    : new ContentProjectSnapshot(projectId.Value, "Project", "Brief")),
            new StubJobReader(storyboard),
            new AssetStubTimeProvider(AssetTestData.Now));
}
