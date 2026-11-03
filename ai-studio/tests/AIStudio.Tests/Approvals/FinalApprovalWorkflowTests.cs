using AIStudio.Application.Approvals;
using AIStudio.Application.Assets;
using AIStudio.Application.Content;
using AIStudio.Domain.Jobs;
using Xunit;

namespace AIStudio.Tests.Approvals;

public sealed class FinalApprovalWorkflowTests
{
    private static readonly DateTimeOffset Now = ApprovalTestData.Now;

    [Fact]
    public async Task Approve_CreatesApprovalWhenRenderQaAndArtifactAgree()
    {
        var fixture = new Fixture();

        var result = await fixture.Workflow.ApproveAsync(
            fixture.ProjectId,
            fixture.RenderJobId,
            "youtube",
            "  first upload  ",
            "  operator-1  ",
            TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.True(result.Created);
        Assert.Equal(1, result.Approval.Revision);
        Assert.Equal(fixture.RenderJobId, result.Approval.SourceRenderJobId);
        Assert.Equal(ApprovalTestData.RenderPath, result.Approval.ArtifactPath);
        Assert.Equal(ApprovalTestData.RenderHash, result.Approval.ArtifactContentHash);
        Assert.Equal("youtube", result.Approval.Destination);
        Assert.Equal("first upload", result.Approval.Notes);
        Assert.Equal("operator-1", result.Approval.ApproverLabel);
        Assert.Equal(1, fixture.Repository.SaveCount);
    }

    [Fact]
    public async Task Approve_IsIdempotentForTheSameArtifactAndDestination()
    {
        var fixture = new Fixture();
        var cancellationToken = TestContext.Current.CancellationToken;
        var first = await fixture.Workflow.ApproveAsync(
            fixture.ProjectId,
            fixture.RenderJobId,
            "youtube",
            null,
            null,
            cancellationToken);
        fixture.Time.UtcNow = Now.AddMinutes(5);

        var repeated = await fixture.Workflow.ApproveAsync(
            fixture.ProjectId,
            fixture.RenderJobId,
            "youtube",
            null,
            null,
            cancellationToken);

        Assert.NotNull(first);
        Assert.NotNull(repeated);
        Assert.False(repeated.Created);
        Assert.Equal(first.Approval.Id, repeated.Approval.Id);
        Assert.Equal(1, repeated.Approval.Revision);
        Assert.Equal(1, fixture.Repository.SaveCount);
    }

    [Fact]
    public async Task Approve_ChangesRevisionWhenDestinationOrNotesChange()
    {
        var fixture = new Fixture();
        var cancellationToken = TestContext.Current.CancellationToken;
        var first = await fixture.Workflow.ApproveAsync(
            fixture.ProjectId,
            fixture.RenderJobId,
            "youtube",
            null,
            null,
            cancellationToken);
        fixture.Time.UtcNow = Now.AddMinutes(1);

        var destination = await fixture.Workflow.ApproveAsync(
            fixture.ProjectId,
            fixture.RenderJobId,
            "tiktok",
            null,
            null,
            cancellationToken);
        fixture.Time.UtcNow = Now.AddMinutes(2);
        var notes = await fixture.Workflow.ApproveAsync(
            fixture.ProjectId,
            fixture.RenderJobId,
            "tiktok",
            "pin a comment",
            null,
            cancellationToken);

        Assert.NotNull(first);
        Assert.NotNull(destination);
        Assert.NotNull(notes);
        Assert.Equal(first.Approval.Id, notes.Approval.Id);
        Assert.Equal(2, destination.Approval.Revision);
        Assert.Equal(3, notes.Approval.Revision);
        Assert.Equal("pin a comment", notes.Approval.Notes);
        Assert.Equal(3, fixture.Repository.SaveCount);
    }

    [Fact]
    public async Task Approve_RequiresSucceededQaEvidence()
    {
        var fixture = new Fixture();
        fixture.Repository.QaEvidence = null;

        var missing = await Assert.ThrowsAsync<FinalApprovalException>(() =>
            fixture.Workflow.ApproveAsync(
                fixture.ProjectId,
                fixture.RenderJobId,
                "youtube",
                null,
                null,
                TestContext.Current.CancellationToken));
        Assert.Equal("final_approval_qa_required", missing.ErrorCode);

        fixture.Repository.QaEvidence = ApprovalTestData.QaEvidence(
            fixture.QaJobId,
            ApprovalTestData.QaResultJson().Replace("\"hasAudio\":true", "\"hasAudio\":false", StringComparison.Ordinal));

        var failing = await Assert.ThrowsAsync<FinalApprovalException>(() =>
            fixture.Workflow.ApproveAsync(
                fixture.ProjectId,
                fixture.RenderJobId,
                "youtube",
                null,
                null,
                TestContext.Current.CancellationToken));
        Assert.Equal("final_approval_qa_required", failing.ErrorCode);

        fixture.Repository.QaEvidence = ApprovalTestData.QaEvidence(
            fixture.QaJobId,
            succeeded: false);

        var failedJob = await Assert.ThrowsAsync<FinalApprovalException>(() =>
            fixture.Workflow.ApproveAsync(
                fixture.ProjectId,
                fixture.RenderJobId,
                "youtube",
                null,
                null,
                TestContext.Current.CancellationToken));
        Assert.Equal("final_approval_qa_required", failedJob.ErrorCode);
    }

    [Fact]
    public async Task Approve_RequiresCompletedRender()
    {
        var fixture = new Fixture();
        fixture.Jobs.Jobs[fixture.RenderJobId] = ApprovalTestData.RenderJob(
            fixture.ProjectId,
            fixture.RenderJobId,
            status: JobStatus.Failed,
            result: null);

        var notSucceeded = await Assert.ThrowsAsync<FinalApprovalException>(() =>
            fixture.Workflow.ApproveAsync(
                fixture.ProjectId,
                fixture.RenderJobId,
                "youtube",
                null,
                null,
                TestContext.Current.CancellationToken));
        Assert.Equal("final_approval_render_not_succeeded", notSucceeded.ErrorCode);

        fixture.Jobs.Jobs.Remove(fixture.RenderJobId);
        var missing = await Assert.ThrowsAsync<FinalApprovalException>(() =>
            fixture.Workflow.ApproveAsync(
                fixture.ProjectId,
                fixture.RenderJobId,
                "youtube",
                null,
                null,
                TestContext.Current.CancellationToken));
        Assert.Equal("final_approval_render_missing", missing.ErrorCode);
    }

    [Fact]
    public async Task Approve_RequiresArtifactToMatchRecordedRender()
    {
        var fixture = new Fixture();
        fixture.Store.Files[ApprovalTestData.RenderPath] = new AssetFileInfo(
            ApprovalTestData.RenderPath,
            "/tmp/video.mp4",
            ApprovalTestData.RenderByteSize,
            ApprovalTestData.ChangedHash);

        var mismatch = await Assert.ThrowsAsync<FinalApprovalException>(() =>
            fixture.Workflow.ApproveAsync(
                fixture.ProjectId,
                fixture.RenderJobId,
                "youtube",
                null,
                null,
                TestContext.Current.CancellationToken));
        Assert.Equal("final_approval_artifact_mismatch", mismatch.ErrorCode);

        fixture.Store.Files.Remove(ApprovalTestData.RenderPath);
        var missing = await Assert.ThrowsAsync<FinalApprovalException>(() =>
            fixture.Workflow.ApproveAsync(
                fixture.ProjectId,
                fixture.RenderJobId,
                "youtube",
                null,
                null,
                TestContext.Current.CancellationToken));
        Assert.Equal("final_approval_artifact_missing", missing.ErrorCode);
    }

    [Fact]
    public async Task Approve_RejectsSupersededRenderAndInvalidDestination()
    {
        var fixture = new Fixture();
        fixture.Repository.HasNewerRender = true;

        var superseded = await Assert.ThrowsAsync<FinalApprovalException>(() =>
            fixture.Workflow.ApproveAsync(
                fixture.ProjectId,
                fixture.RenderJobId,
                "youtube",
                null,
                null,
                TestContext.Current.CancellationToken));
        Assert.Equal("final_approval_render_superseded", superseded.ErrorCode);

        fixture.Repository.HasNewerRender = false;
        var destination = await Assert.ThrowsAsync<ArgumentException>(() =>
            fixture.Workflow.ApproveAsync(
                fixture.ProjectId,
                fixture.RenderJobId,
                "not a platform",
                null,
                null,
                TestContext.Current.CancellationToken));
        Assert.Equal("platform", destination.ParamName);
    }

    [Fact]
    public async Task Approve_ReturnsNullWhenProjectDoesNotExist()
    {
        var fixture = new Fixture(projectExists: false);

        var result = await fixture.Workflow.ApproveAsync(
            fixture.ProjectId,
            fixture.RenderJobId,
            "youtube",
            null,
            null,
            TestContext.Current.CancellationToken);

        Assert.Null(result);
    }

    [Fact]
    public async Task Evaluate_ReportsMissingApprovedAndInvalidStates()
    {
        var fixture = new Fixture();
        var cancellationToken = TestContext.Current.CancellationToken;

        var missing = await fixture.Workflow.EvaluateAsync(
            fixture.ProjectId,
            cancellationToken);
        Assert.NotNull(missing);
        Assert.Equal(FinalApprovalState.Missing, missing.State);
        Assert.Null(missing.Reason);
        Assert.Null(missing.Approval);

        await fixture.Workflow.ApproveAsync(
            fixture.ProjectId,
            fixture.RenderJobId,
            "youtube",
            null,
            null,
            cancellationToken);

        var approved = await fixture.Workflow.EvaluateAsync(
            fixture.ProjectId,
            cancellationToken);
        Assert.NotNull(approved);
        Assert.Equal(FinalApprovalState.Approved, approved.State);
        Assert.Null(approved.Reason);
        Assert.NotNull(approved.Approval);
        Assert.Equal(1, approved.Approval.Revision);
    }

    [Fact]
    public async Task Evaluate_InvalidatesWhenArtifactChangesOrDisappears()
    {
        var fixture = new Fixture();
        var cancellationToken = TestContext.Current.CancellationToken;
        await fixture.Workflow.ApproveAsync(
            fixture.ProjectId,
            fixture.RenderJobId,
            "youtube",
            null,
            null,
            cancellationToken);

        fixture.Store.Files[ApprovalTestData.RenderPath] = new AssetFileInfo(
            ApprovalTestData.RenderPath,
            "/tmp/video.mp4",
            ApprovalTestData.RenderByteSize,
            ApprovalTestData.ChangedHash);
        var mismatch = await fixture.Workflow.EvaluateAsync(
            fixture.ProjectId,
            cancellationToken);
        Assert.NotNull(mismatch);
        Assert.Equal(FinalApprovalState.Invalid, mismatch.State);
        Assert.Equal("final_approval_artifact_mismatch", mismatch.Reason);
        Assert.NotNull(mismatch.Approval);

        fixture.Store.Files.Remove(ApprovalTestData.RenderPath);
        var missing = await fixture.Workflow.EvaluateAsync(
            fixture.ProjectId,
            cancellationToken);
        Assert.NotNull(missing);
        Assert.Equal(FinalApprovalState.Invalid, missing.State);
        Assert.Equal("final_approval_artifact_missing", missing.Reason);
    }

    [Fact]
    public async Task Evaluate_InvalidatesAfterNewRenderOrLostQaEvidence()
    {
        var fixture = new Fixture();
        var cancellationToken = TestContext.Current.CancellationToken;
        await fixture.Workflow.ApproveAsync(
            fixture.ProjectId,
            fixture.RenderJobId,
            "youtube",
            null,
            null,
            cancellationToken);

        fixture.Repository.HasNewerRender = true;
        var superseded = await fixture.Workflow.EvaluateAsync(
            fixture.ProjectId,
            cancellationToken);
        Assert.NotNull(superseded);
        Assert.Equal(FinalApprovalState.Invalid, superseded.State);
        Assert.Equal("final_approval_render_superseded", superseded.Reason);

        fixture.Repository.HasNewerRender = false;
        fixture.Repository.QaEvidence = ApprovalTestData.QaEvidence(
            fixture.QaJobId,
            succeeded: false);
        var failedQa = await fixture.Workflow.EvaluateAsync(
            fixture.ProjectId,
            cancellationToken);
        Assert.NotNull(failedQa);
        Assert.Equal(FinalApprovalState.Invalid, failedQa.State);
        Assert.Equal("final_approval_qa_invalidated", failedQa.Reason);

        fixture.Repository.QaEvidence = null;
        var qa = await fixture.Workflow.EvaluateAsync(
            fixture.ProjectId,
            cancellationToken);
        Assert.NotNull(qa);
        Assert.Equal(FinalApprovalState.Invalid, qa.State);
        Assert.Equal("final_approval_qa_invalidated", qa.Reason);
    }

    [Fact]
    public async Task Evaluate_ReturnsNullWhenProjectDoesNotExist()
    {
        var fixture = new Fixture(projectExists: false);

        Assert.Null(await fixture.Workflow.EvaluateAsync(
            fixture.ProjectId,
            TestContext.Current.CancellationToken));
    }

    private sealed class Fixture
    {
        public Fixture(bool projectExists = true)
        {
            Repository.QaEvidence = ApprovalTestData.QaEvidence(QaJobId);
            Jobs.Jobs[RenderJobId] = ApprovalTestData.RenderJob(ProjectId, RenderJobId);
            Workflow = new FinalApprovalWorkflow(
                Repository,
                new ProjectReaderStub(
                    projectExists
                        ? new ContentProjectSnapshot(ProjectId, "Approval fixture", null)
                        : null),
                Jobs,
                Store,
                Time);
        }

        public Guid ProjectId { get; } = Guid.NewGuid();

        public Guid RenderJobId { get; } = Guid.NewGuid();

        public Guid QaJobId { get; } = Guid.NewGuid();

        public FakeFinalApprovalRepository Repository { get; } = new();

        public JobReaderStub Jobs { get; } = new();

        public FakeAssetFileStore Store { get; } = new();

        public SettableTimeProvider Time { get; } = new(Now);

        public FinalApprovalWorkflow Workflow { get; }
    }
}
