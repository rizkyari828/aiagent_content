using AIStudio.Application.Approvals;
using AIStudio.Application.Assets;
using AIStudio.Application.Content;
using AIStudio.Domain.Approvals;
using AIStudio.Domain.Jobs;
using Xunit;

namespace AIStudio.Tests.Approvals;

public sealed class PublicationWorkflowTests
{
    private const string VideoUrl = "https://youtu.be/dQw4w9WgXcQ";
    private const string VideoId = "dQw4w9WgXcQ";

    [Fact]
    public async Task Create_RecordsManualPublicationWhenApprovalIsValid()
    {
        var fixture = new Fixture();
        var publishedAt = ApprovalTestData.Now.AddHours(-2);

        var result = await fixture.Workflow.CreateAsync(
            fixture.ProjectId,
            "youtube",
            $"  {VideoUrl}  ",
            $"  {VideoId}  ",
            publishedAt,
            "  first manual upload  ",
            TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.True(result.Created);
        Assert.Equal("youtube", result.Publication.Platform);
        Assert.Equal(VideoUrl, result.Publication.Url);
        Assert.Equal(VideoId, result.Publication.VideoId);
        Assert.Equal(publishedAt, result.Publication.PublishedAt);
        Assert.Equal("first manual upload", result.Publication.Notes);
        Assert.Equal(fixture.Approval!.Id, result.Publication.FinalApprovalId);
        Assert.Equal(fixture.Approval.Revision, result.Publication.FinalApprovalRevision);
        Assert.Equal(ApprovalTestData.RenderHash, result.Publication.ArtifactContentHash);
        Assert.Equal(1, fixture.Publications.SaveCount);
    }

    [Fact]
    public async Task Create_IsIdempotentForTheSameRecord()
    {
        var fixture = new Fixture();
        var cancellationToken = TestContext.Current.CancellationToken;
        var first = await fixture.Workflow.CreateAsync(
            fixture.ProjectId,
            "youtube",
            VideoUrl,
            VideoId,
            ApprovalTestData.Now.AddHours(-2),
            null,
            cancellationToken);

        var repeated = await fixture.Workflow.CreateAsync(
            fixture.ProjectId,
            "youtube",
            VideoUrl,
            VideoId,
            ApprovalTestData.Now.AddHours(-2),
            null,
            cancellationToken);

        Assert.NotNull(first);
        Assert.NotNull(repeated);
        Assert.False(repeated.Created);
        Assert.Equal(first.Publication.Id, repeated.Publication.Id);
        Assert.Equal(1, fixture.Publications.SaveCount);
    }

    [Fact]
    public async Task Create_RejectsADifferentExistingRecord()
    {
        var fixture = new Fixture();
        var cancellationToken = TestContext.Current.CancellationToken;
        await fixture.Workflow.CreateAsync(
            fixture.ProjectId,
            "youtube",
            VideoUrl,
            VideoId,
            ApprovalTestData.Now.AddHours(-2),
            null,
            cancellationToken);

        var conflict = await Assert.ThrowsAsync<PublicationException>(() =>
            fixture.Workflow.CreateAsync(
                fixture.ProjectId,
                "youtube",
                VideoUrl,
                "different-id",
                ApprovalTestData.Now.AddHours(-2),
                null,
                cancellationToken));

        Assert.Equal("publication_conflict", conflict.ErrorCode);
        Assert.Equal(1, fixture.Publications.SaveCount);
    }

    [Fact]
    public async Task Create_RequiresACurrentlyValidApproval()
    {
        var fixture = new Fixture();
        fixture.Approvals.Approval = null;

        var missing = await Assert.ThrowsAsync<PublicationException>(() =>
            fixture.Workflow.CreateAsync(
                fixture.ProjectId,
                "youtube",
                VideoUrl,
                VideoId,
                ApprovalTestData.Now.AddHours(-2),
                null,
                TestContext.Current.CancellationToken));
        Assert.Equal("publication_approval_required", missing.ErrorCode);

        fixture = new Fixture();
        fixture.Store.Files[ApprovalTestData.RenderPath] = new AssetFileInfo(
            ApprovalTestData.RenderPath,
            "/tmp/video.mp4",
            ApprovalTestData.RenderByteSize,
            ApprovalTestData.ChangedHash);

        var changed = await Assert.ThrowsAsync<PublicationException>(() =>
            fixture.Workflow.CreateAsync(
                fixture.ProjectId,
                "youtube",
                VideoUrl,
                VideoId,
                ApprovalTestData.Now.AddHours(-2),
                null,
                TestContext.Current.CancellationToken));
        Assert.Equal("publication_approval_required", changed.ErrorCode);
    }

    [Fact]
    public async Task Create_RejectsDestinationNotCoveredByApproval()
    {
        var fixture = new Fixture();

        var mismatch = await Assert.ThrowsAsync<PublicationException>(() =>
            fixture.Workflow.CreateAsync(
                fixture.ProjectId,
                "tiktok",
                "https://www.tiktok.com/@studio/video/123456789",
                VideoId,
                ApprovalTestData.Now.AddHours(-2),
                null,
                TestContext.Current.CancellationToken));

        Assert.Equal("publication_destination_mismatch", mismatch.ErrorCode);
    }

    [Fact]
    public async Task Create_RejectsInvalidVideoMetadata()
    {
        var fixture = new Fixture();
        var cancellationToken = TestContext.Current.CancellationToken;

        var videoId = await Assert.ThrowsAsync<PublicationException>(() =>
            fixture.Workflow.CreateAsync(
                fixture.ProjectId,
                "youtube",
                VideoUrl,
                "https://youtu.be/dQw4w9WgXcQ",
                ApprovalTestData.Now,
                null,
                cancellationToken));
        Assert.Equal("publication_video_id_invalid", videoId.ErrorCode);

        var url = await Assert.ThrowsAsync<PublicationException>(() =>
            fixture.Workflow.CreateAsync(
                fixture.ProjectId,
                "youtube",
                "http://youtu.be/dQw4w9WgXcQ",
                VideoId,
                ApprovalTestData.Now,
                null,
                cancellationToken));
        Assert.Equal("publication_url_invalid", url.ErrorCode);

        var platform = await Assert.ThrowsAsync<PublicationException>(() =>
            fixture.Workflow.CreateAsync(
                fixture.ProjectId,
                "YouTube!",
                VideoUrl,
                VideoId,
                ApprovalTestData.Now,
                null,
                cancellationToken));
        Assert.Equal("publication_platform_invalid", platform.ErrorCode);

        var timestamp = await Assert.ThrowsAsync<PublicationException>(() =>
            fixture.Workflow.CreateAsync(
                fixture.ProjectId,
                "youtube",
                VideoUrl,
                VideoId,
                default,
                null,
                cancellationToken));
        Assert.Equal("publication_invalid_request", timestamp.ErrorCode);
    }

    [Fact]
    public async Task Create_ReturnsNullWhenProjectDoesNotExist()
    {
        var fixture = new Fixture(projectExists: false);

        var result = await fixture.Workflow.CreateAsync(
            fixture.ProjectId,
            "youtube",
            VideoUrl,
            VideoId,
            ApprovalTestData.Now,
            null,
            TestContext.Current.CancellationToken);

        Assert.Null(result);
    }

    [Fact]
    public async Task Create_RemainsIdempotentAfterApprovalBecomesInvalid()
    {
        var fixture = new Fixture();
        var cancellationToken = TestContext.Current.CancellationToken;
        await fixture.Workflow.CreateAsync(
            fixture.ProjectId,
            "youtube",
            VideoUrl,
            VideoId,
            ApprovalTestData.Now.AddHours(-2),
            null,
            cancellationToken);

        fixture.Approvals.Approval = null;

        var repeated = await fixture.Workflow.CreateAsync(
            fixture.ProjectId,
            "youtube",
            VideoUrl,
            VideoId,
            ApprovalTestData.Now.AddHours(-2),
            null,
            cancellationToken);

        Assert.NotNull(repeated);
        Assert.False(repeated.Created);
        Assert.Equal(1, fixture.Publications.SaveCount);
    }

    [Fact]
    public async Task Find_ReturnsTheRecordOrNull()
    {
        var fixture = new Fixture();
        var cancellationToken = TestContext.Current.CancellationToken;

        Assert.Null(await fixture.Workflow.FindAsync(
            fixture.ProjectId,
            cancellationToken));

        await fixture.Workflow.CreateAsync(
            fixture.ProjectId,
            "youtube",
            VideoUrl,
            VideoId,
            ApprovalTestData.Now.AddHours(-2),
            null,
            cancellationToken);

        var record = await fixture.Workflow.FindAsync(
            fixture.ProjectId,
            cancellationToken);
        Assert.NotNull(record);
        Assert.Equal(VideoId, record.VideoId);
    }

    [Fact]
    public async Task Create_RejectsWhenTheReviewRequestedChanges()
    {
        var fixture = new Fixture();
        fixture.Approvals.Approval = ApprovalTestData.CreateApproval(
            fixture.ProjectId,
            fixture.RenderJobId,
            decision: FinalApprovalDecision.ChangesRequested,
            decisionNote: "Fix the ending");

        var exception = await Assert.ThrowsAsync<PublicationException>(
            () => fixture.Workflow.CreateAsync(
                fixture.ProjectId,
                "youtube",
                VideoUrl,
                VideoId,
                ApprovalTestData.Now.AddHours(-2),
                null,
                TestContext.Current.CancellationToken));

        Assert.Equal("publication_approval_required", exception.ErrorCode);
        Assert.Contains("changes", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Null(fixture.Publications.Record);
        Assert.Equal(0, fixture.Publications.SaveCount);
    }

    private sealed class Fixture
    {
        public Fixture(bool projectExists = true)
        {
            Approvals.Approval = ApprovalTestData.CreateApproval(
                ProjectId,
                RenderJobId);
            Approvals.QaEvidence = ApprovalTestData.QaEvidence(QaJobId);
            Jobs.Jobs[RenderJobId] = ApprovalTestData.RenderJob(ProjectId, RenderJobId);

            var projectReader = new ProjectReaderStub(
                projectExists
                    ? new ContentProjectSnapshot(ProjectId, "Publication fixture", null)
                    : null);
            var approvalWorkflow = new FinalApprovalWorkflow(
                Approvals,
                projectReader,
                Jobs,
                Store,
                Time);

            Workflow = new PublicationWorkflow(
                Publications,
                projectReader,
                approvalWorkflow,
                Time);
        }

        public Guid ProjectId { get; } = Guid.NewGuid();

        public Guid RenderJobId { get; } = Guid.NewGuid();

        public Guid QaJobId { get; } = Guid.NewGuid();

        public FakeFinalApprovalRepository Approvals { get; } = new();

        public FakePublicationRepository Publications { get; } = new();

        public JobReaderStub Jobs { get; } = new();

        public FakeAssetFileStore Store { get; } = new();

        public SettableTimeProvider Time { get; } = new(ApprovalTestData.Now);

        public FinalApproval? Approval => Approvals.Approval;

        public PublicationWorkflow Workflow { get; }
    }
}
