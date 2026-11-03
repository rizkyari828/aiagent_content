using AIStudio.Domain.Approvals;
using Xunit;

namespace AIStudio.Tests.Approvals;

public sealed class FinalApprovalTests
{
    private static readonly DateTimeOffset Now = ApprovalTestData.Now;

    [Fact]
    public void Create_BindsArtifactQaAndDestinationAtRevisionOne()
    {
        var approval = ApprovalTestData.CreateApproval(
            Guid.NewGuid(),
            Guid.NewGuid(),
            destination: "  YouTube ",
            notes: "  manual upload ready  ",
            approver: "  operator-1  ");

        Assert.Equal("youtube", approval.Destination);
        Assert.Equal("manual upload ready", approval.Notes);
        Assert.Equal("operator-1", approval.ApproverLabel);
        Assert.Equal(ApprovalTestData.RenderHash, approval.ArtifactContentHash);
        Assert.Equal(ApprovalTestData.RenderByteSize, approval.ArtifactByteSize);
        Assert.Equal(1, approval.Revision);
        Assert.Equal(Now, approval.ApprovedAt);
        Assert.Equal(Now, approval.CreatedAt);
        Assert.Equal(Now, approval.UpdatedAt);
    }

    [Fact]
    public void Apply_IsIdempotentForTheSameBinding()
    {
        var approval = ApprovalTestData.CreateApproval(
            Guid.NewGuid(),
            Guid.NewGuid());

        var changed = approval.Apply(
            approval.SourceRenderJobId,
            approval.ArtifactPath,
            approval.ArtifactContentHash,
            approval.ArtifactByteSize,
            "youtube",
            null,
            null,
            Now.AddMinutes(5));

        Assert.False(changed);
        Assert.Equal(1, approval.Revision);
        Assert.Equal(Now, approval.ApprovedAt);
    }

    [Fact]
    public void Apply_IncrementsRevisionWhenDestinationOrNotesChange()
    {
        var approval = ApprovalTestData.CreateApproval(
            Guid.NewGuid(),
            Guid.NewGuid());

        Assert.True(approval.Apply(
            approval.SourceRenderJobId,
            approval.ArtifactPath,
            approval.ArtifactContentHash,
            approval.ArtifactByteSize,
            "tiktok",
            null,
            null,
            Now.AddMinutes(1)));
        Assert.Equal(2, approval.Revision);
        Assert.Equal("tiktok", approval.Destination);
        Assert.Equal(Now.AddMinutes(1), approval.ApprovedAt);

        Assert.True(approval.Apply(
            approval.SourceRenderJobId,
            approval.ArtifactPath,
            approval.ArtifactContentHash,
            approval.ArtifactByteSize,
            "tiktok",
            "  publish with pinned comment  ",
            "operator-2",
            Now.AddMinutes(2)));
        Assert.Equal(3, approval.Revision);
        Assert.Equal("publish with pinned comment", approval.Notes);
        Assert.Equal("operator-2", approval.ApproverLabel);
    }

    [Fact]
    public void Apply_IncrementsRevisionWhenTheArtifactBindingChanges()
    {
        var approval = ApprovalTestData.CreateApproval(
            Guid.NewGuid(),
            Guid.NewGuid());
        var newRenderJobId = Guid.NewGuid();
        Assert.True(approval.Apply(
            newRenderJobId,
            "renders/project/video-v2.mp4",
            ApprovalTestData.ChangedHash,
            8192,
            "youtube",
            null,
            null,
            Now.AddMinutes(1)));

        Assert.Equal(2, approval.Revision);
        Assert.Equal(newRenderJobId, approval.SourceRenderJobId);
        Assert.Equal("renders/project/video-v2.mp4", approval.ArtifactPath);
        Assert.Equal(ApprovalTestData.ChangedHash, approval.ArtifactContentHash);
        Assert.Equal(8192, approval.ArtifactByteSize);
    }

    [Theory]
    [InlineData("")]
    [InlineData("1youtube")]
    [InlineData("you tube")]
    public void Create_RejectsInvalidDestination(string destination)
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            ApprovalTestData.CreateApproval(
                Guid.NewGuid(),
                Guid.NewGuid(),
                destination: destination));

        Assert.Equal("platform", exception.ParamName);
    }

    [Fact]
    public void Create_RejectsNonSha256HashAndNonPositiveSize()
    {
        var projectId = Guid.NewGuid();

        var hash = Assert.Throws<ArgumentException>(() => FinalApproval.Create(
            projectId,
            Guid.NewGuid(),
            ApprovalTestData.RenderPath,
            "not-a-hash",
            ApprovalTestData.RenderByteSize,
            "youtube",
            null,
            null,
            Now));
        Assert.Equal("artifactContentHash", hash.ParamName);

        var size = Assert.Throws<ArgumentOutOfRangeException>(() => FinalApproval.Create(
            projectId,
            Guid.NewGuid(),
            ApprovalTestData.RenderPath,
            ApprovalTestData.RenderHash,
            0,
            "youtube",
            null,
            null,
            Now));
        Assert.Equal("value", size.ParamName);
    }

    [Theory]
    [InlineData("/absolute/video.mp4")]
    [InlineData("../escape.mp4")]
    [InlineData("renders/./video.mp4")]
    [InlineData("renders\\video.mp4")]
    public void Create_RejectsUnsafeArtifactPaths(string path)
    {
        var exception = Assert.Throws<ArgumentException>(() => FinalApproval.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            path,
            ApprovalTestData.RenderHash,
            ApprovalTestData.RenderByteSize,
            "youtube",
            null,
            null,
            Now));

        Assert.Equal("value", exception.ParamName);
    }
}
