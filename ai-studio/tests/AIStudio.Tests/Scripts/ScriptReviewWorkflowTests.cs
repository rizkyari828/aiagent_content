using AIStudio.Application.Jobs;
using AIStudio.Application.Jobs.GenerateScript;
using AIStudio.Application.Scripts;
using AIStudio.Domain.Jobs;
using AIStudio.Domain.Scripts;
using AIStudio.Tests.Jobs;
using Xunit;

namespace AIStudio.Tests.Scripts;

public sealed class ScriptReviewWorkflowTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 17, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Start_CopiesCompletedGenerateScriptResultAndIsIdempotent()
    {
        var source = SourceJob();
        var repository = new RecordingScriptReviewRepository();
        var workflow = CreateWorkflow(repository, source);

        var started = await workflow.StartAsync(
            source.ContentProjectId,
            source.Id,
            TestContext.Current.CancellationToken);
        var repeated = await workflow.StartAsync(
            source.ContentProjectId,
            source.Id,
            TestContext.Current.CancellationToken);

        Assert.True(started.Created);
        Assert.False(repeated.Created);
        Assert.Equal(ScriptReviewStatus.Draft, started.Script.Status);
        Assert.Equal(source.Id, started.Script.SourceJobId);
        Assert.Equal("Local AI Tutorial", started.Script.Script.Title);
        Assert.Equal(1, repository.SaveCount);
    }

    [Fact]
    public async Task Start_RejectsDifferentSourceAndInvalidJobState()
    {
        var source = SourceJob();
        var existing = ReviewedScript.Create(
            source.ContentProjectId,
            Guid.NewGuid(),
            GenerateScriptResult.Deserialize(GenerateScriptTestData.ValidResult).Serialize(),
            Now);
        var repository = new RecordingScriptReviewRepository(existing);
        var workflow = CreateWorkflow(repository, source);

        var conflict = await Assert.ThrowsAsync<ScriptReviewException>(() =>
            workflow.StartAsync(
                source.ContentProjectId,
                source.Id,
                TestContext.Current.CancellationToken));
        Assert.Equal("script_review_conflict", conflict.ErrorCode);

        var failedSource = source with { Status = JobStatus.Failed, Result = null };
        var invalidWorkflow = CreateWorkflow(
            new RecordingScriptReviewRepository(),
            failedSource);
        var invalid = await Assert.ThrowsAsync<ScriptReviewException>(() =>
            invalidWorkflow.StartAsync(
                source.ContentProjectId,
                source.Id,
                TestContext.Current.CancellationToken));
        Assert.Equal("script_review_invalid_source", invalid.ErrorCode);
    }

    [Fact]
    public async Task Edit_PersistsCanonicalHumanVersionAndRejectsBlankContent()
    {
        var source = SourceJob();
        var repository = new RecordingScriptReviewRepository();
        var time = new SettableTimeProvider(Now);
        var workflow = new ScriptReviewWorkflow(repository, new StubJobReader(source), time);
        await workflow.StartAsync(
            source.ContentProjectId,
            source.Id,
            TestContext.Current.CancellationToken);
        time.UtcNow = Now.AddMinutes(1);

        var editedContent = new GenerateScriptResult(
            "  Human Reviewed Title  ",
            " Reviewed hook ",
            [new GenerateScriptSection(" Section ", " Human narration ")],
            " Reviewed closing ");
        var edited = await workflow.EditAsync(
            source.ContentProjectId,
            editedContent,
            TestContext.Current.CancellationToken);

        Assert.NotNull(edited);
        Assert.Equal(2, edited.Revision);
        Assert.Equal("Human Reviewed Title", edited.Script.Title);
        Assert.Equal("Human narration", edited.Script.Sections[0].Narration);
        Assert.Equal(2, repository.SaveCount);
        Assert.Equal(edited.Script.Serialize(), repository.Script!.Content);

        var invalid = await Assert.ThrowsAsync<ScriptReviewException>(() =>
            workflow.EditAsync(
                source.ContentProjectId,
                editedContent with { Closing = " " },
                TestContext.Current.CancellationToken));
        Assert.Equal("script_review_invalid_content", invalid.ErrorCode);
        Assert.Equal(2, repository.SaveCount);
    }

    [Fact]
    public async Task Approve_IsIdempotentAndApprovedHumanVersionIsCanonical()
    {
        var source = SourceJob();
        var repository = new RecordingScriptReviewRepository();
        var time = new SettableTimeProvider(Now);
        var workflow = new ScriptReviewWorkflow(repository, new StubJobReader(source), time);
        await workflow.StartAsync(
            source.ContentProjectId,
            source.Id,
            TestContext.Current.CancellationToken);
        time.UtcNow = Now.AddMinutes(1);
        await workflow.EditAsync(
            source.ContentProjectId,
            GenerateScriptResult.Deserialize(GenerateScriptTestData.ValidResult) with
            {
                Closing = "Approved human closing."
            },
            TestContext.Current.CancellationToken);
        time.UtcNow = Now.AddMinutes(2);

        var approved = await workflow.ApproveAsync(
            source.ContentProjectId,
            TestContext.Current.CancellationToken);
        var repeated = await workflow.ApproveAsync(
            source.ContentProjectId,
            TestContext.Current.CancellationToken);
        var retrieved = await workflow.FindAsync(
            source.ContentProjectId,
            TestContext.Current.CancellationToken);

        Assert.NotNull(approved);
        Assert.NotNull(repeated);
        Assert.NotNull(retrieved);
        Assert.Equal(ScriptReviewStatus.Approved, approved.Status);
        Assert.Equal("Approved human closing.", retrieved.Script.Closing);
        Assert.Equal(3, repository.SaveCount);

        var edit = await Assert.ThrowsAsync<ScriptReviewException>(() =>
            workflow.EditAsync(
                source.ContentProjectId,
                retrieved.Script with { Closing = "Changed later." },
                TestContext.Current.CancellationToken));
        Assert.Equal("script_review_invalid_state", edit.ErrorCode);
    }

    [Fact]
    public async Task MissingReviewAndSourceAreReportedDeterministically()
    {
        var source = SourceJob();
        var repository = new RecordingScriptReviewRepository();
        var workflow = new ScriptReviewWorkflow(
            repository,
            new StubJobReader(null),
            new SettableTimeProvider(Now));

        var sourceMissing = await Assert.ThrowsAsync<ScriptReviewException>(() =>
            workflow.StartAsync(
                source.ContentProjectId,
                source.Id,
                TestContext.Current.CancellationToken));
        Assert.Equal("script_review_source_not_found", sourceMissing.ErrorCode);

        Assert.Null(await workflow.FindAsync(
            source.ContentProjectId,
            TestContext.Current.CancellationToken));
        Assert.Null(await workflow.EditAsync(
            source.ContentProjectId,
            GenerateScriptResult.Deserialize(GenerateScriptTestData.ValidResult),
            TestContext.Current.CancellationToken));
        Assert.Null(await workflow.ApproveAsync(
            source.ContentProjectId,
            TestContext.Current.CancellationToken));
        Assert.Equal(0, repository.SaveCount);
    }

    private static ScriptReviewWorkflow CreateWorkflow(
        RecordingScriptReviewRepository repository,
        JobSnapshot source) =>
        new(repository, new StubJobReader(source), new SettableTimeProvider(Now));

    private static JobSnapshot SourceJob() =>
        new(
            Guid.NewGuid(),
            Guid.NewGuid(),
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

    private sealed class RecordingScriptReviewRepository(ReviewedScript? script = null)
        : IScriptReviewRepository
    {
        public ReviewedScript? Script { get; private set; } = script;

        public int SaveCount { get; private set; }

        public Task<ReviewedScript?> FindByProjectIdAsync(
            Guid contentProjectId,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(
                Script?.ContentProjectId == contentProjectId ? Script : null);
        }

        public void Add(ReviewedScript reviewedScript) => Script = reviewedScript;

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SaveCount++;
            return Task.FromResult(1);
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

    private sealed class SettableTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public DateTimeOffset UtcNow { get; set; } = utcNow;

        public override DateTimeOffset GetUtcNow() => UtcNow;
    }
}
