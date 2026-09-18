using AIStudio.Application.Abstractions.Persistence;
using AIStudio.Application.Content;
using AIStudio.Application.Jobs;
using AIStudio.Application.Jobs.GenerateIdea;
using AIStudio.Application.Jobs.GenerateStoryboard;
using AIStudio.Domain.Content;
using AIStudio.Domain.Jobs;
using Xunit;

namespace AIStudio.Tests.Jobs;

public sealed class GenerateIdeaWorkflowTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 16, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task CreateProject_NormalizesAndPersistsProject()
    {
        var dbContext = new RecordingDbContext();
        var workflow = CreateWorkflow(dbContext);

        var project = await workflow.CreateProjectAsync(
            "  Local AI Studio  ",
            "  Brief  ",
            TestContext.Current.CancellationToken);

        Assert.Equal("Local AI Studio", project.Title);
        Assert.Equal("Brief", project.Brief);
        Assert.Equal(project.Id, dbContext.AddedProject?.Id);
        Assert.Equal(1, dbContext.SaveCount);
    }

    [Fact]
    public async Task Enqueue_CreatesDurableGenerateIdeaJobForExistingProject()
    {
        var projectId = Guid.NewGuid();
        var dbContext = new RecordingDbContext();
        var workflow = CreateWorkflow(
            dbContext,
            new ContentProjectSnapshot(projectId, "Project", "Brief"));

        var jobId = await workflow.EnqueueAsync(
            projectId,
            "  Local coding  ",
            "  Developers  ",
            "  Indonesian  ",
            TestContext.Current.CancellationToken);

        Assert.NotNull(jobId);
        var job = Assert.IsType<Job>(dbContext.AddedJob);
        Assert.Equal(jobId, job.Id);
        Assert.Equal(JobType.GenerateIdea, job.Type);
        Assert.Equal(JobStatus.Queued, job.Status);
        Assert.Equal(64, job.InputVersionHash.Length);
        Assert.Equal(1, dbContext.SaveCount);

        var payload = GenerateIdeaJobPayload.Deserialize(job.Payload);
        Assert.Equal(projectId, payload.ContentProjectId);
        Assert.Equal("Local coding", payload.Topic);
        Assert.Equal("Developers", payload.TargetAudience);
        Assert.Equal("Indonesian", payload.Language);
    }

    [Fact]
    public async Task Enqueue_ReturnsNullWhenProjectDoesNotExist()
    {
        var dbContext = new RecordingDbContext();
        var workflow = CreateWorkflow(dbContext);

        var jobId = await workflow.EnqueueAsync(
            Guid.NewGuid(),
            "Topic",
            null,
            null,
            TestContext.Current.CancellationToken);

        Assert.Null(jobId);
        Assert.Null(dbContext.AddedJob);
        Assert.Equal(0, dbContext.SaveCount);
    }

    [Fact]
    public async Task Enqueue_RejectsInvalidPayloadBeforeProjectLookupResult()
    {
        var workflow = CreateWorkflow(new RecordingDbContext());

        var exception = await Assert.ThrowsAsync<JobExecutionException>(
            () => workflow.EnqueueAsync(
                Guid.NewGuid(),
                "   ",
                null,
                null,
                TestContext.Current.CancellationToken));

        Assert.Equal("generate_idea_invalid_payload", exception.ErrorCode);
    }

    [Fact]
    public async Task FindJob_DeserializesCompletedGenerateIdeaResult()
    {
        var jobId = Guid.NewGuid();
        var projectId = Guid.NewGuid();
        var snapshot = new JobSnapshot(
            jobId,
            projectId,
            JobType.GenerateIdea,
            JobStatus.Succeeded,
            0,
            2,
            GenerateIdeaTestData.ValidResult,
            null,
            null,
            Now,
            Now,
            Now,
            Now);
        var workflow = CreateWorkflow(
            new RecordingDbContext(),
            jobSnapshot: snapshot);

        var job = await workflow.FindJobAsync(
            jobId,
            TestContext.Current.CancellationToken);

        Assert.NotNull(job);
        Assert.Equal(JobStatus.Succeeded, job.Status);
        var result = Assert.IsType<GenerateIdeaResult>(job.Result);
        Assert.Equal("Local AI Content", result.Title);
        Assert.Equal("Tutorial", result.SuggestedFormat);
    }

    [Fact]
    public async Task FindJob_DeserializesCompletedGenerateScriptResult()
    {
        var jobId = Guid.NewGuid();
        var projectId = Guid.NewGuid();
        var snapshot = new JobSnapshot(
            jobId,
            projectId,
            JobType.GenerateScript,
            JobStatus.Succeeded,
            0,
            2,
            GenerateScriptTestData.ValidResult,
            null,
            null,
            Now,
            Now,
            Now,
            Now);
        var workflow = CreateWorkflow(
            new RecordingDbContext(),
            jobSnapshot: snapshot);

        var job = await workflow.FindJobAsync(
            jobId,
            TestContext.Current.CancellationToken);

        Assert.NotNull(job);
        var result = Assert.IsType<AIStudio.Application.Jobs.GenerateScript.GenerateScriptResult>(job.Result);
        Assert.Equal("Local AI Tutorial", result.Title);
        Assert.Equal(2, result.Sections.Count);
    }

    [Fact]
    public async Task FindJob_DeserializesCompletedGenerateStoryboardResult()
    {
        var jobId = Guid.NewGuid();
        var projectId = Guid.NewGuid();
        var snapshot = new JobSnapshot(
            jobId,
            projectId,
            JobType.GenerateStoryboard,
            JobStatus.Succeeded,
            0,
            2,
            GenerateStoryboardTestData.ValidResult,
            null,
            null,
            Now,
            Now,
            Now,
            Now);
        var workflow = CreateWorkflow(
            new RecordingDbContext(),
            jobSnapshot: snapshot);

        var job = await workflow.FindJobAsync(
            jobId,
            TestContext.Current.CancellationToken);

        Assert.NotNull(job);
        var result = Assert.IsType<GenerateStoryboardResult>(job.Result);
        Assert.Equal("Local AI Storyboard", result.Title);
        Assert.Equal(2, result.Scenes.Count);
    }

    [Fact]
    public async Task FindJob_ReturnsNullWhenJobDoesNotExist()
    {
        var workflow = CreateWorkflow(new RecordingDbContext());

        var job = await workflow.FindJobAsync(
            Guid.NewGuid(),
            TestContext.Current.CancellationToken);

        Assert.Null(job);
    }

    private static GenerateIdeaWorkflow CreateWorkflow(
        RecordingDbContext dbContext,
        ContentProjectSnapshot? project = null,
        JobSnapshot? jobSnapshot = null) =>
        new(
            dbContext,
            new StubContentProjectReader(project),
            new StubJobReader(jobSnapshot),
            new StubTimeProvider(Now));

    private sealed class RecordingDbContext : IApplicationDbContext
    {
        public ContentProject? AddedProject { get; private set; }

        public Job? AddedJob { get; private set; }

        public int SaveCount { get; private set; }

        public void Add(ContentProject project) => AddedProject = project;

        public void Add(Job job) => AddedJob = job;

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SaveCount++;
            return Task.FromResult(1);
        }
    }

    private sealed class StubContentProjectReader(ContentProjectSnapshot? project)
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

    private sealed class StubJobReader(JobSnapshot? job) : IJobReader
    {
        public Task<JobSnapshot?> FindByIdAsync(
            Guid id,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(job?.Id == id ? job : null);
        }
    }

    private sealed class StubTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
