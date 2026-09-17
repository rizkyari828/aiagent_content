using AIStudio.Application.Abstractions.Persistence;
using AIStudio.Application.Content;
using AIStudio.Application.Jobs.GenerateIdea;
using AIStudio.Application.Jobs.GenerateScript;
using AIStudio.Domain.Content;
using AIStudio.Domain.Jobs;
using Xunit;

namespace AIStudio.Tests.Jobs;

public sealed class GenerateScriptWorkflowTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 17, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Enqueue_CreatesDurableGenerateScriptJobForExistingProject()
    {
        var projectId = Guid.NewGuid();
        var dbContext = new RecordingDbContext();
        var workflow = CreateWorkflow(
            dbContext,
            new ContentProjectSnapshot(projectId, "Project", "Brief"));

        var jobId = await workflow.EnqueueAsync(
            projectId,
            GenerateScriptTestData.SelectedIdea,
            " Indonesian ",
            TestContext.Current.CancellationToken);

        Assert.NotNull(jobId);
        var job = Assert.IsType<Job>(dbContext.AddedJob);
        Assert.Equal(JobType.GenerateScript, job.Type);
        Assert.Equal(JobStatus.Queued, job.Status);
        Assert.Equal(64, job.InputVersionHash.Length);
        Assert.Equal(1, dbContext.SaveCount);

        var payload = GenerateScriptJobPayload.Deserialize(job.Payload);
        Assert.Equal(projectId, payload.ContentProjectId);
        Assert.Equal("Local AI Content", payload.SelectedIdea.Title);
        Assert.Equal("Indonesian", payload.Language);
    }

    [Fact]
    public async Task Enqueue_ReturnsNullWhenProjectDoesNotExist()
    {
        var dbContext = new RecordingDbContext();
        var workflow = CreateWorkflow(dbContext, null);

        var jobId = await workflow.EnqueueAsync(
            Guid.NewGuid(),
            GenerateScriptTestData.SelectedIdea,
            null,
            TestContext.Current.CancellationToken);

        Assert.Null(jobId);
        Assert.Null(dbContext.AddedJob);
        Assert.Equal(0, dbContext.SaveCount);
    }

    [Fact]
    public async Task Enqueue_RejectsInvalidSelectedIdeaBeforeProjectLookup()
    {
        var workflow = CreateWorkflow(new RecordingDbContext(), null);
        var invalidIdea = new GenerateIdeaResult(
            "", "Hook", "Summary", "Angle", "Audience", "Tutorial");

        var exception = await Assert.ThrowsAsync<AIStudio.Application.Jobs.JobExecutionException>(
            () => workflow.EnqueueAsync(
                Guid.NewGuid(),
                invalidIdea,
                null,
                TestContext.Current.CancellationToken));

        Assert.Equal("generate_script_invalid_payload", exception.ErrorCode);
    }

    private static GenerateScriptWorkflow CreateWorkflow(
        RecordingDbContext dbContext,
        ContentProjectSnapshot? project) =>
        new(dbContext, new StubContentProjectReader(project), new StubTimeProvider(Now));

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

    private sealed class StubTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
