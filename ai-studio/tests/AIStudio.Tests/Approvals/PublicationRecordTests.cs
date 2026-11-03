using AIStudio.Domain.Approvals;
using Xunit;

namespace AIStudio.Tests.Approvals;

public sealed class PublicationRecordTests
{
    private static readonly DateTimeOffset Now = ApprovalTestData.Now;

    [Fact]
    public void Create_NormalizesPlatformAndValidatesVideoMetadata()
    {
        var record = PublicationRecord.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            3,
            ApprovalTestData.RenderHash,
            "  YouTube  ",
            "  https://youtu.be/dQw4w9WgXcQ  ",
            "  dQw4w9WgXcQ  ",
            Now.AddHours(-1),
            null,
            Now);

        Assert.Equal("youtube", record.Platform);
        Assert.Equal("https://youtu.be/dQw4w9WgXcQ", record.Url);
        Assert.Equal("dQw4w9WgXcQ", record.VideoId);
        Assert.Equal(Now.AddHours(-1), record.PublishedAt);
        Assert.Equal(3, record.FinalApprovalRevision);
        Assert.Equal(ApprovalTestData.RenderHash, record.ArtifactContentHash);
    }

    [Theory]
    [InlineData("https://www.youtube.com/watch?v=dQw4w9WgXcQ")]
    [InlineData("dQw4w9WgXcQ/extra")]
    [InlineData("ab")]
    [InlineData("has space")]
    public void Create_RejectsVideoIdThatIsNotAnOpaqueIdentifier(string videoId)
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            PublicationRecord.Create(
                Guid.NewGuid(),
                Guid.NewGuid(),
                1,
                ApprovalTestData.RenderHash,
                "youtube",
                "https://youtu.be/dQw4w9WgXcQ",
                videoId,
                Now,
                null,
                Now));

        Assert.Equal("videoId", exception.ParamName);
    }

    [Theory]
    [InlineData("http://youtu.be/dQw4w9WgXcQ")]
    [InlineData("https://example.com/watch?v=dQw4w9WgXcQ")]
    [InlineData("https://youtu.be@evil.example/watch")]
    [InlineData("not a url")]
    public void Create_RejectsNonHttpsOrForeignYouTubeUrl(string url)
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            PublicationRecord.Create(
                Guid.NewGuid(),
                Guid.NewGuid(),
                1,
                ApprovalTestData.RenderHash,
                "youtube",
                url,
                "dQw4w9WgXcQ",
                Now,
                null,
                Now));

        Assert.Equal("url", exception.ParamName);
    }

    [Fact]
    public void Matches_RequiresTheExactSameRecord()
    {
        var record = PublicationRecord.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            1,
            ApprovalTestData.RenderHash,
            "youtube",
            "https://youtu.be/dQw4w9WgXcQ",
            "dQw4w9WgXcQ",
            Now,
            "  done  ",
            Now);

        Assert.True(record.Matches(
            "youtube",
            "https://youtu.be/dQw4w9WgXcQ",
            "dQw4w9WgXcQ",
            Now,
            "done"));
        Assert.False(record.Matches(
            "youtube",
            "https://youtu.be/dQw4w9WgXcQ",
            "different-id",
            Now,
            "done"));
        Assert.False(record.Matches(
            "youtube",
            "https://youtu.be/dQw4w9WgXcQ",
            "dQw4w9WgXcQ",
            Now.AddMinutes(1),
            "done"));
    }

    [Fact]
    public void Create_RejectsInvalidApprovalRevisionAndHash()
    {
        var revision = Assert.Throws<ArgumentOutOfRangeException>(() =>
            PublicationRecord.Create(
                Guid.NewGuid(),
                Guid.NewGuid(),
                0,
                ApprovalTestData.RenderHash,
                "youtube",
                "https://youtu.be/dQw4w9WgXcQ",
                "dQw4w9WgXcQ",
                Now,
                null,
                Now));
        Assert.Equal("finalApprovalRevision", revision.ParamName);

        var hash = Assert.Throws<ArgumentException>(() =>
            PublicationRecord.Create(
                Guid.NewGuid(),
                Guid.NewGuid(),
                1,
                "bad",
                "youtube",
                "https://youtu.be/dQw4w9WgXcQ",
                "dQw4w9WgXcQ",
                Now,
                null,
                Now));
        Assert.Equal("artifactContentHash", hash.ParamName);
    }
}
