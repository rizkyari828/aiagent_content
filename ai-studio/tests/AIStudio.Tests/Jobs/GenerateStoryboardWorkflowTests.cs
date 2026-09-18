using AIStudio.Application.Abstractions.Persistence;
using AIStudio.Application.Content;
using AIStudio.Application.Jobs.GenerateScript;
using AIStudio.Application.Jobs.GenerateStoryboard;
using AIStudio.Domain.Content;
using AIStudio.Domain.Jobs;
using AIStudio.Domain.Scripts;
using Xunit;

namespace AIStudio.Tests.Jobs;

public sealed class GenerateStoryboardWorkflowTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 18, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Enqueue_CreatesDurableGenerateStoryboardJobForApprovedScript()
    {
        var projectId = Guid.NewGuid();
        var dbContext = new RecordingDbContext();
        var workflow = CreateWorkflow(
            dbContext,
            projectId,
            GenerateStoryboardTestData.Script(projectId, ScriptReviewStatus.Approved));

        var jobId = await workflow.EnqueueAsync(
            projectId,
            TestContext.Current.CancellationToken);

        Assert.NotNull(jobId);
        var job = Assert.IsType<Job>(dbContext.AddedJob);
        Assert.Equal(jobId, job.Id);
        Assert.Equal(JobType.GenerateStoryboard, job.Type);
        Assert.Equal(JobStatus.Queued, job.Status);
        Assert.Equal(64, job.InputVersionHash.Length);
        Assert.Equal(1, dbContext.SaveCount);

        var payload = GenerateStoryboardJobPayload.Deserialize(job.Payload);
        Assert.Equal(projectId, payload.ContentProjectId);
    }

    [Fact]
    public async Task Enqueue_ReturnsNullWhenProjectDoesNotExist()
    {
        var projectId = Guid.NewGuid();
        var dbContext = new RecordingDbContext();
        var workflow = CreateWorkflow(
            dbContext,
            projectId: null,
            GenerateStoryboardTestData.Script(projectId, ScriptReviewStatus.Approved));

        var jobId = await workflow.EnqueueAsync(
            projectId,
            TestContext.Current.CancellationToken);

        Assert.Null(jobId);
        Assert.Null(dbContext.AddedJob);
        Assert.Equal(0, dbContext.SaveCount);
    }

    [Fact]
    public async Task Enqueue_RejectsMissingReviewedScript()
    {
        var projectId = Guid.NewGuid();
        var dbContext = new RecordingDbContext();
        var workflow = CreateWorkflow(dbContext, projectId, script: null);

        var exception = await Assert.ThrowsAsync<StoryboardGenerationException>(
            () => workflow.EnqueueAsync(
                projectId,
                TestContext.Current.CancellationToken));

        Assert.Equal("storyboard_script_not_found", exception.ErrorCode);
        Assert.Null(dbContext.AddedJob);
        Assert.Equal(0, dbContext.SaveCount);
    }

    [Fact]
    public async Task Enqueue_RejectsDraftReviewedScript()
    {
        var projectId = Guid.NewGuid();
        var dbContext = new RecordingDbContext();
        var workflow = CreateWorkflow(
            dbContext,
            projectId,
            GenerateStoryboardTestData.Script(projectId, ScriptReviewStatus.Draft));

        var exception = await Assert.ThrowsAsync<StoryboardGenerationException>(
            () => workflow.EnqueueAsync(
                projectId,
                TestContext.Current.CancellationToken));

        Assert.Equal("storyboard_script_not_approved", exception.ErrorCode);
        Assert.Null(dbContext.AddedJob);
        Assert.Equal(0, dbContext.SaveCount);
    }

    [Fact]
    public async Task Enqueue_InputVersionHashTracksApprovedScriptContent()
    {
        var firstProjectId = Guid.NewGuid();
        var firstContext = new RecordingDbContext();
        var firstWorkflow = CreateWorkflow(
            firstContext,
            firstProjectId,
            GenerateStoryboardTestData.Script(firstProjectId, ScriptReviewStatus.Approved));
        await firstWorkflow.EnqueueAsync(
            firstProjectId,
            TestContext.Current.CancellationToken);

        var editedContent = GenerateScriptResult
            .Deserialize(GenerateStoryboardTestData.ApprovedScriptContent) with
        {
            Title = "A different approved script"
        };
        var secondProjectId = Guid.NewGuid();
        var secondContext = new RecordingDbContext();
        var secondWorkflow = CreateWorkflow(
            secondContext,
            secondProjectId,
            GenerateStoryboardTestData.Script(
                secondProjectId,
                ScriptReviewStatus.Approved,
                editedContent.Serialize()));
        await secondWorkflow.EnqueueAsync(
            secondProjectId,
            TestContext.Current.CancellationToken);

        Assert.NotEqual(
            firstContext.AddedJob!.InputVersionHash,
            secondContext.AddedJob!.InputVersionHash);
    }

    private static GenerateStoryboardWorkflow CreateWorkflow(
        RecordingDbContext dbContext,
        Guid? projectId,
        ReviewedScript? script) =>
        new(
            dbContext,
            new StubProjectReader(
                projectId is null
                    ? null
                    : new ContentProjectSnapshot(projectId.Value, "Project", "Brief")),
            new StubScriptReviewRepository(script),
            new StubTimeProvider(Now));

    private sealed class RecordingDbContext : IApplicationDbContext
    {
        public Job? AddedJob { get; private set; }

        public int SaveCount { get; private set; }

        public void Add(ContentProject project) =>
            throw new InvalidOperationException("Project creation is not used by this workflow.");

        public void Add(Job job) => AddedJob = job;

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SaveCount++;
            return Task.FromResult(1);
        }
    }

    private sealed class StubProjectReader(ContentProjectSnapshot? project)
        : IContentProjectReader
    {
        public Task<ContentProjectSnapshot?> FindByIdAsync(
            Guid id,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(project?.Id == id ? project : null);
        }
    }

    private sealed class StubTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
