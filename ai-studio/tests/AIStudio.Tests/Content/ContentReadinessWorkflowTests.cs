using AIStudio.Application.Approvals;
using AIStudio.Application.Assets;
using AIStudio.Application.Content;
using AIStudio.Domain.Approvals;
using AIStudio.Domain.Content;
using AIStudio.Domain.Jobs;
using AIStudio.Tests.Approvals;
using Xunit;

namespace AIStudio.Tests.Content;

public sealed class ContentReadinessWorkflowTests
{
    [Fact]
    public async Task Evaluate_NotStarted_WhenNoJobsExist()
    {
        var fixture = new ContentProjectFixture();

        var readiness = await fixture.ReadinessWorkflow.EvaluateAsync(
            fixture.ProjectId,
            TestContext.Current.CancellationToken);

        Assert.NotNull(readiness);
        Assert.Equal(ContentReadinessStatus.NotStarted, readiness.Status);
        Assert.False(readiness.IsReadyToPublish);
        var reason = Assert.Single(readiness.Reasons);
        Assert.Equal(ContentReadinessCodes.RenderMissing, reason.Code);
        Assert.True(reason.Blocking);
    }

    [Fact]
    public async Task Evaluate_InProgress_WhenNoRenderHasSucceeded()
    {
        var fixture = new ContentProjectFixture();
        fixture.SeedJob(JobType.GenerateIdea, JobStatus.Succeeded, Now());

        var readiness = await fixture.ReadinessWorkflow.EvaluateAsync(
            fixture.ProjectId,
            TestContext.Current.CancellationToken);

        Assert.NotNull(readiness);
        Assert.Equal(ContentReadinessStatus.InProgress, readiness.Status);
        Assert.Equal(
            ContentReadinessCodes.RenderMissing,
            Assert.Single(readiness.Reasons).Code);
    }

    [Fact]
    public async Task Evaluate_InProgress_WhenRenderIsStillRunning()
    {
        var fixture = new ContentProjectFixture();
        fixture.SeedJob(JobType.RenderVideo, JobStatus.Running, Now());

        var readiness = await fixture.ReadinessWorkflow.EvaluateAsync(
            fixture.ProjectId,
            TestContext.Current.CancellationToken);

        Assert.NotNull(readiness);
        Assert.Equal(ContentReadinessStatus.InProgress, readiness.Status);
        Assert.True(Assert.Single(readiness.Reasons).Blocking);
    }

    [Fact]
    public async Task Evaluate_Blocked_WhenNewestRenderFailed()
    {
        var fixture = new ContentProjectFixture();
        fixture.SeedJob(JobType.RenderVideo, JobStatus.Failed, Now());

        var readiness = await fixture.ReadinessWorkflow.EvaluateAsync(
            fixture.ProjectId,
            TestContext.Current.CancellationToken);

        Assert.NotNull(readiness);
        Assert.Equal(ContentReadinessStatus.Blocked, readiness.Status);
        Assert.Equal(
            ContentReadinessCodes.RenderMissing,
            Assert.Single(readiness.Reasons).Code);
    }

    [Fact]
    public async Task Evaluate_InProgress_WithQaFailed_WhenNoQaRan()
    {
        var fixture = new ContentProjectFixture().SeedSucceededRender();
        fixture.Approvals.QaEvidence = null;

        var readiness = await fixture.ReadinessWorkflow.EvaluateAsync(
            fixture.ProjectId,
            TestContext.Current.CancellationToken);

        Assert.NotNull(readiness);
        Assert.Equal(ContentReadinessStatus.InProgress, readiness.Status);
        Assert.Equal(
            ContentReadinessCodes.QaFailed,
            Assert.Single(readiness.Reasons).Code);
    }

    [Fact]
    public async Task Evaluate_InProgress_WithQaFailed_WhenNewestQaFailed()
    {
        var fixture = new ContentProjectFixture().SeedSucceededRender().SeedQaFailure();

        var readiness = await fixture.ReadinessWorkflow.EvaluateAsync(
            fixture.ProjectId,
            TestContext.Current.CancellationToken);

        Assert.NotNull(readiness);
        Assert.Equal(ContentReadinessStatus.InProgress, readiness.Status);
        Assert.Equal(
            ContentReadinessCodes.QaFailed,
            Assert.Single(readiness.Reasons).Code);
    }

    [Fact]
    public async Task Evaluate_QaPassed_WhenApprovalIsMissing()
    {
        var fixture = new ContentProjectFixture().SeedSucceededRender().SeedPassingQa();

        var readiness = await fixture.ReadinessWorkflow.EvaluateAsync(
            fixture.ProjectId,
            TestContext.Current.CancellationToken);

        Assert.NotNull(readiness);
        Assert.Equal(ContentReadinessStatus.QaPassed, readiness.Status);
        Assert.Equal(
            ContentReadinessCodes.FinalApprovalMissing,
            Assert.Single(readiness.Reasons).Code);
    }

    [Fact]
    public async Task Evaluate_QaPassed_WhenApprovalIsInvalid()
    {
        var fixture = new ContentProjectFixture()
            .SeedSucceededRender()
            .SeedPassingQa()
            .SeedApproval();
        fixture.Store.Files[ApprovalTestData.RenderPath] = new AssetFileInfo(
            ApprovalTestData.RenderPath,
            "/tmp/video.mp4",
            ApprovalTestData.RenderByteSize,
            ApprovalTestData.ChangedHash);

        var readiness = await fixture.ReadinessWorkflow.EvaluateAsync(
            fixture.ProjectId,
            TestContext.Current.CancellationToken);

        Assert.NotNull(readiness);
        Assert.Equal(ContentReadinessStatus.QaPassed, readiness.Status);
        var reason = Assert.Single(readiness.Reasons);
        Assert.Equal(ContentReadinessCodes.FinalApprovalInvalid, reason.Code);
        Assert.Contains("final_approval_artifact_mismatch", reason.Message);
    }

    [Fact]
    public async Task Evaluate_ReadyToPublish_WithNonBlockingPublicationReason()
    {
        var fixture = new ContentProjectFixture()
            .SeedSucceededRender()
            .SeedPassingQa()
            .SeedApproval();

        var readiness = await fixture.ReadinessWorkflow.EvaluateAsync(
            fixture.ProjectId,
            TestContext.Current.CancellationToken);

        Assert.NotNull(readiness);
        Assert.Equal(ContentReadinessStatus.ReadyToPublish, readiness.Status);
        Assert.True(readiness.IsReadyToPublish);
        var reason = Assert.Single(readiness.Reasons);
        Assert.Equal(ContentReadinessCodes.PublicationMissing, reason.Code);
        Assert.False(reason.Blocking);
        Assert.Empty(readiness.BlockingReasonCodes);
    }

    [Fact]
    public async Task Evaluate_ReadyToPublish_WithoutReasons_WhenPublicationExists()
    {
        var fixture = new ContentProjectFixture()
            .SeedSucceededRender()
            .SeedPassingQa()
            .SeedApproval();
        fixture.Publications.Record = PublicationRecord.Create(
            fixture.ProjectId,
            fixture.Approvals.Approval!.Id,
            finalApprovalRevision: 1,
            artifactContentHash: ApprovalTestData.RenderHash,
            platform: "youtube",
            url: "https://youtu.be/dQw4w9WgXcQ",
            videoId: "dQw4w9WgXcQ",
            publishedAt: Now().AddHours(-1),
            notes: null,
            utcNow: Now());

        var readiness = await fixture.ReadinessWorkflow.EvaluateAsync(
            fixture.ProjectId,
            TestContext.Current.CancellationToken);

        Assert.NotNull(readiness);
        Assert.Equal(ContentReadinessStatus.ReadyToPublish, readiness.Status);
        Assert.Empty(readiness.Reasons);
    }

    [Fact]
    public async Task Evaluate_ReturnsNullWhenProjectDoesNotExist()
    {
        var fixture = new ContentProjectFixture(projectExists: false);

        var readiness = await fixture.ReadinessWorkflow.EvaluateAsync(
            fixture.ProjectId,
            TestContext.Current.CancellationToken);

        Assert.Null(readiness);
    }

    [Fact]
    public async Task MarkReadyToPublish_TransitionsOnceAndIsIdempotent()
    {
        var fixture = new ContentProjectFixture()
            .SeedSucceededRender()
            .SeedPassingQa()
            .SeedApproval();
        fixture.Time.UtcNow = Now().AddMinutes(7);

        var first = await fixture.ReadinessWorkflow.MarkReadyToPublishAsync(
            fixture.ProjectId,
            TestContext.Current.CancellationToken);
        var second = await fixture.ReadinessWorkflow.MarkReadyToPublishAsync(
            fixture.ProjectId,
            TestContext.Current.CancellationToken);

        Assert.NotNull(first);
        Assert.True(first.Transitioned);
        Assert.Equal(ContentStatus.ReadyToPublish, first.Readiness.ProjectStatus);
        Assert.Equal(ContentReadinessStatus.ReadyToPublish, first.Readiness.Status);
        Assert.Equal(ContentStatus.ReadyToPublish, fixture.Project.Status);
        Assert.Equal(Now().AddMinutes(7), fixture.Project.UpdatedAt);

        Assert.NotNull(second);
        Assert.False(second.Transitioned);
        Assert.Equal(1, fixture.Projects.SaveCount);
    }

    [Fact]
    public async Task MarkReadyToPublish_RejectsNotReadyProjectWithReasonCodes()
    {
        var fixture = new ContentProjectFixture().SeedSucceededRender().SeedPassingQa();

        var exception = await Assert.ThrowsAsync<ContentProjectException>(
            () => fixture.ReadinessWorkflow.MarkReadyToPublishAsync(
                fixture.ProjectId,
                TestContext.Current.CancellationToken));

        Assert.Equal("content_project_not_ready", exception.ErrorCode);
        Assert.Equal(
            new[] { ContentReadinessCodes.FinalApprovalMissing },
            exception.ReasonCodes);
        Assert.Equal(ContentStatus.Draft, fixture.Project.Status);
        Assert.Equal(0, fixture.Projects.SaveCount);
    }

    [Fact]
    public async Task MarkReadyToPublish_RejectsPublishedProject()
    {
        var fixture = new ContentProjectFixture()
            .SeedSucceededRender()
            .SeedPassingQa()
            .SeedApproval();
        fixture.Project.MarkReadyToPublish(Now());
        fixture.Project.TransitionTo(ContentStatus.Published, Now().AddMinutes(1));

        var exception = await Assert.ThrowsAsync<ContentProjectException>(
            () => fixture.ReadinessWorkflow.MarkReadyToPublishAsync(
                fixture.ProjectId,
                TestContext.Current.CancellationToken));

        Assert.Equal("content_project_transition_not_allowed", exception.ErrorCode);
        Assert.Equal(ContentStatus.Published, fixture.Project.Status);
    }

    [Fact]
    public async Task MarkReadyToPublish_ReturnsNullWhenProjectDoesNotExist()
    {
        var fixture = new ContentProjectFixture(projectExists: false);

        var result = await fixture.ReadinessWorkflow.MarkReadyToPublishAsync(
            fixture.ProjectId,
            TestContext.Current.CancellationToken);

        Assert.Null(result);
    }

    [Fact]
    public async Task Evaluate_Blocked_WhenFinalApprovalRequestedChanges()
    {
        var fixture = new ContentProjectFixture()
            .SeedSucceededRender()
            .SeedQaFailure()
            .SeedChangesRequested();

        var readiness = await fixture.ReadinessWorkflow.EvaluateAsync(
            fixture.ProjectId,
            TestContext.Current.CancellationToken);

        Assert.NotNull(readiness);
        Assert.Equal(ContentReadinessStatus.Blocked, readiness.Status);
        Assert.False(readiness.IsReadyToPublish);
        var reason = Assert.Single(readiness.Reasons);
        Assert.Equal(
            ContentReadinessCodes.FinalApprovalChangesRequested,
            reason.Code);
        Assert.True(reason.Blocking);
        Assert.Equal(
            new[] { ContentReadinessCodes.FinalApprovalChangesRequested },
            readiness.BlockingReasonCodes);
    }

    [Fact]
    public async Task MarkReadyToPublish_RejectsChangesRequestedProject()
    {
        var fixture = new ContentProjectFixture()
            .SeedSucceededRender()
            .SeedPassingQa()
            .SeedChangesRequested();

        var exception = await Assert.ThrowsAsync<ContentProjectException>(
            () => fixture.ReadinessWorkflow.MarkReadyToPublishAsync(
                fixture.ProjectId,
                TestContext.Current.CancellationToken));

        Assert.Equal("content_project_not_ready", exception.ErrorCode);
        Assert.Equal(
            new[] { ContentReadinessCodes.FinalApprovalChangesRequested },
            exception.ReasonCodes);
        Assert.Equal(ContentStatus.Draft, fixture.Project.Status);
        Assert.Equal(0, fixture.Projects.SaveCount);
    }

    [Fact]
    public async Task Evaluate_ReadyToPublish_AfterChangesAreApproved()
    {
        var fixture = new ContentProjectFixture()
            .SeedSucceededRender()
            .SeedPassingQa()
            .SeedChangesRequested();
        var cancellationToken = TestContext.Current.CancellationToken;
        fixture.Time.UtcNow = Now().AddMinutes(4);

        var approved = await fixture.ApprovalWorkflow.ApproveAsync(
            fixture.ProjectId,
            fixture.RenderJobId,
            "youtube",
            null,
            "operator-1",
            cancellationToken);
        var readiness = await fixture.ReadinessWorkflow.EvaluateAsync(
            fixture.ProjectId,
            cancellationToken);

        Assert.NotNull(approved);
        Assert.Equal(2, approved.Approval.Revision);
        Assert.Equal(
            FinalApprovalDecision.Approved,
            approved.Approval.Decision);
        Assert.NotNull(readiness);
        Assert.Equal(ContentReadinessStatus.ReadyToPublish, readiness.Status);
        Assert.DoesNotContain(readiness.Reasons, reason => reason.Blocking);
    }

    [Fact]
    public async Task MarkPublished_TransitionsFromReadyToPublish_WhenPublicationExists()
    {
        var fixture = new ContentProjectFixture()
            .SeedSucceededRender()
            .SeedPassingQa()
            .SeedApproval()
            .SeedPublication();
        fixture.Project.MarkReadyToPublish(Now());
        fixture.Time.UtcNow = Now().AddMinutes(10);

        var result = await fixture.ReadinessWorkflow.MarkPublishedAsync(
            fixture.ProjectId,
            TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.True(result.Transitioned);
        Assert.Equal(ContentStatus.Published, result.Readiness.ProjectStatus);
        Assert.Equal(ContentStatus.Published, fixture.Project.Status);
        Assert.Equal(Now().AddMinutes(10), fixture.Project.UpdatedAt);
        Assert.Equal(1, fixture.Projects.SaveCount);
    }

    [Fact]
    public async Task MarkPublished_IsIdempotent()
    {
        var fixture = new ContentProjectFixture()
            .SeedSucceededRender()
            .SeedPassingQa()
            .SeedApproval()
            .SeedPublication();
        fixture.Project.MarkReadyToPublish(Now());
        fixture.Time.UtcNow = Now().AddMinutes(10);

        var first = await fixture.ReadinessWorkflow.MarkPublishedAsync(
            fixture.ProjectId,
            TestContext.Current.CancellationToken);
        var second = await fixture.ReadinessWorkflow.MarkPublishedAsync(
            fixture.ProjectId,
            TestContext.Current.CancellationToken);

        Assert.NotNull(first);
        Assert.True(first.Transitioned);
        Assert.NotNull(second);
        Assert.False(second.Transitioned);
        Assert.Equal(ContentStatus.Published, second.Readiness.ProjectStatus);
        Assert.Equal(1, fixture.Projects.SaveCount);
    }

    [Fact]
    public async Task MarkPublished_RejectsWhenPublicationMissing()
    {
        var fixture = new ContentProjectFixture()
            .SeedSucceededRender()
            .SeedPassingQa()
            .SeedApproval();
        fixture.Project.MarkReadyToPublish(Now());

        var exception = await Assert.ThrowsAsync<ContentProjectException>(
            () => fixture.ReadinessWorkflow.MarkPublishedAsync(
                fixture.ProjectId,
                TestContext.Current.CancellationToken));

        Assert.Equal("content_project_not_ready", exception.ErrorCode);
        Assert.Equal(
            new[] { ContentReadinessCodes.PublicationMissing },
            exception.ReasonCodes);
        Assert.Equal(ContentStatus.ReadyToPublish, fixture.Project.Status);
        Assert.Equal(0, fixture.Projects.SaveCount);
    }

    [Fact]
    public async Task MarkPublished_RejectsWhenApprovalAndPublicationMissing()
    {
        var fixture = new ContentProjectFixture()
            .SeedSucceededRender()
            .SeedPassingQa();
        fixture.Project.MarkReadyToPublish(Now());

        var exception = await Assert.ThrowsAsync<ContentProjectException>(
            () => fixture.ReadinessWorkflow.MarkPublishedAsync(
                fixture.ProjectId,
                TestContext.Current.CancellationToken));

        Assert.Equal("content_project_not_ready", exception.ErrorCode);
        Assert.Contains(ContentReadinessCodes.FinalApprovalMissing, exception.ReasonCodes);
        Assert.Contains(ContentReadinessCodes.PublicationMissing, exception.ReasonCodes);
        Assert.Equal(ContentStatus.ReadyToPublish, fixture.Project.Status);
        Assert.Equal(0, fixture.Projects.SaveCount);
    }

    [Fact]
    public async Task MarkPublished_RejectsArchivedProject()
    {
        var fixture = new ContentProjectFixture()
            .SeedSucceededRender()
            .SeedPassingQa()
            .SeedApproval()
            .SeedPublication();
        fixture.Project.MarkReadyToPublish(Now());
        fixture.Project.TransitionTo(ContentStatus.Archived, Now().AddMinutes(1));

        var exception = await Assert.ThrowsAsync<ContentProjectException>(
            () => fixture.ReadinessWorkflow.MarkPublishedAsync(
                fixture.ProjectId,
                TestContext.Current.CancellationToken));

        Assert.Equal("content_project_transition_not_allowed", exception.ErrorCode);
        Assert.Equal(ContentStatus.Archived, fixture.Project.Status);
    }

    [Fact]
    public async Task MarkPublished_RejectsDraftProject()
    {
        var fixture = new ContentProjectFixture()
            .SeedSucceededRender()
            .SeedPassingQa()
            .SeedApproval()
            .SeedPublication();

        var exception = await Assert.ThrowsAsync<ContentProjectException>(
            () => fixture.ReadinessWorkflow.MarkPublishedAsync(
                fixture.ProjectId,
                TestContext.Current.CancellationToken));

        Assert.Equal("content_project_transition_not_allowed", exception.ErrorCode);
        Assert.Equal(ContentStatus.Draft, fixture.Project.Status);
    }

    [Fact]
    public async Task MarkPublished_ReturnsNullWhenProjectDoesNotExist()
    {
        var fixture = new ContentProjectFixture(projectExists: false);

        var result = await fixture.ReadinessWorkflow.MarkPublishedAsync(
            fixture.ProjectId,
            TestContext.Current.CancellationToken);

        Assert.Null(result);
    }

    private static DateTimeOffset Now() => ContentProjectFixture.Now;
}
