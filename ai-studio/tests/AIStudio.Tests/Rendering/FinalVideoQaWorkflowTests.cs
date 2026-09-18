using AIStudio.Application.Content;
using AIStudio.Application.Jobs;
using AIStudio.Application.Jobs.FinalVideoQa;
using AIStudio.Application.Jobs.RenderVideo;
using AIStudio.Domain.Jobs;
using AIStudio.Tests.Assets;
using AIStudio.Tests.Jobs;
using Xunit;

namespace AIStudio.Tests.Rendering;

public sealed class FinalVideoQaWorkflowTests
{
    [Fact]
    public async Task Enqueue_CreatesFinalVideoQaJobForSucceededRenderJob()
    {
        var projectId = Guid.NewGuid();
        var dbContext = new RecordingDbContext();
        var renderResult = FinalVideoQaTestData.RenderResult(
            "renders/project/video.mp4",
            new string('d', RenderVideoResult.ContentHashLength));
        var renderJob = FinalVideoQaTestData.RenderJob(projectId, renderResult);
        var workflow = CreateWorkflow(dbContext, projectId, renderJob);

        var jobId = await workflow.EnqueueAsync(
            projectId,
            renderJob.Id,
            TestContext.Current.CancellationToken);

        Assert.NotNull(jobId);
        var job = Assert.IsType<Job>(dbContext.AddedJob);
        Assert.Equal(jobId, job.Id);
        Assert.Equal(JobType.FinalVideoQa, job.Type);
        Assert.Equal(JobStatus.Queued, job.Status);
        Assert.Equal(64, job.InputVersionHash.Length);
        Assert.Equal(1, dbContext.SaveCount);

        var payload = FinalVideoQaJobPayload.Deserialize(job.Payload);
        Assert.Equal(projectId, payload.ContentProjectId);
        Assert.Equal(renderJob.Id, payload.RenderJobId);
    }

    [Fact]
    public async Task Enqueue_ReturnsNullWhenProjectMissing()
    {
        var projectId = Guid.NewGuid();
        var dbContext = new RecordingDbContext();
        var renderResult = FinalVideoQaTestData.RenderResult(
            "renders/project/video.mp4",
            new string('d', RenderVideoResult.ContentHashLength));
        var renderJob = FinalVideoQaTestData.RenderJob(projectId, renderResult);
        var workflow = CreateWorkflow(dbContext, projectId: null, renderJob);

        var jobId = await workflow.EnqueueAsync(
            projectId,
            renderJob.Id,
            TestContext.Current.CancellationToken);

        Assert.Null(jobId);
        Assert.Null(dbContext.AddedJob);
    }

    [Fact]
    public async Task Enqueue_RejectsMissingRenderJob()
    {
        var projectId = Guid.NewGuid();
        var dbContext = new RecordingDbContext();
        var workflow = CreateWorkflow(dbContext, projectId, renderJob: null);

        var exception = await Assert.ThrowsAsync<FinalVideoQaException>(
            () => workflow.EnqueueAsync(
                projectId,
                Guid.NewGuid(),
                TestContext.Current.CancellationToken));

        Assert.Equal("qa_render_job_not_found", exception.ErrorCode);
        Assert.Equal(0, dbContext.SaveCount);
    }

    [Fact]
    public async Task Enqueue_RejectsUnfinishedRenderJob()
    {
        var projectId = Guid.NewGuid();
        var dbContext = new RecordingDbContext();
        var renderResult = FinalVideoQaTestData.RenderResult(
            "renders/project/video.mp4",
            new string('d', RenderVideoResult.ContentHashLength));
        var renderJob = FinalVideoQaTestData.RenderJob(
            projectId,
            renderResult,
            status: JobStatus.Running);
        var workflow = CreateWorkflow(dbContext, projectId, renderJob);

        var exception = await Assert.ThrowsAsync<FinalVideoQaException>(
            () => workflow.EnqueueAsync(
                projectId,
                renderJob.Id,
                TestContext.Current.CancellationToken));

        Assert.Equal("qa_render_job_invalid", exception.ErrorCode);
        Assert.Equal(0, dbContext.SaveCount);
    }

    private static FinalVideoQaWorkflow CreateWorkflow(
        RecordingDbContext dbContext,
        Guid? projectId,
        JobSnapshot? renderJob) =>
        new(
            dbContext,
            new StubContentProjectReader(
                projectId is null
                    ? null
                    : new ContentProjectSnapshot(projectId.Value, "Project", "Brief")),
            new StubJobReader(renderJob),
            new AssetStubTimeProvider(FinalVideoQaTestData.Now));
}
