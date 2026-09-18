using AIStudio.Domain.Assets;
using AIStudio.Domain.Subtitles;
using Xunit;

namespace AIStudio.Tests.Subtitles;

public sealed class SubtitleTrackTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 18, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_RecordsLocalSubtitleMetadata()
    {
        var projectId = Guid.NewGuid();
        var jobId = Guid.NewGuid();

        var track = SubtitleTrack.Create(
            projectId,
            jobId,
            "subtitle.srt",
            256,
            Hash,
            AssetOrigin.Local,
            source: null,
            creator: "Editor",
            license: null,
            retrievedAt: null,
            Now);

        Assert.Equal(projectId, track.ContentProjectId);
        Assert.Equal(jobId, track.SourceJobId);
        Assert.Equal("subtitle.srt", track.Path);
        Assert.Equal(256, track.ByteSize);
        Assert.Equal(Hash, track.ContentHash);
        Assert.Equal(AssetOrigin.Local, track.Origin);
        Assert.Equal("Editor", track.Creator);
        Assert.Null(track.RetrievedAt);
        Assert.Equal(Now, track.CreatedAt);
    }

    [Fact]
    public void Create_RequiresSourceForExternalSubtitle()
    {
        var exception = Assert.Throws<ArgumentException>(() => SubtitleTrack.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "subtitle.srt",
            10,
            Hash,
            AssetOrigin.External,
            source: null,
            creator: null,
            license: "CC0",
            retrievedAt: null,
            Now));

        Assert.Equal("source", exception.ParamName);
    }

    [Fact]
    public void Create_RequiresLicenseForExternalSubtitle()
    {
        var exception = Assert.Throws<ArgumentException>(() => SubtitleTrack.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "subtitle.srt",
            10,
            Hash,
            AssetOrigin.External,
            source: "https://example.test/subtitle",
            creator: null,
            license: null,
            retrievedAt: null,
            Now));

        Assert.Equal("license", exception.ParamName);
    }

    [Theory]
    [InlineData("not-a-hash")]
    [InlineData("abc123")]
    public void Create_RejectsInvalidContentHash(string hash)
    {
        Assert.Throws<ArgumentException>(() => SubtitleTrack.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "subtitle.srt",
            10,
            hash,
            AssetOrigin.Local,
            null,
            null,
            null,
            null,
            Now));
    }

    [Fact]
    public void Create_RejectsNegativeByteSize()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => SubtitleTrack.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "subtitle.srt",
            -1,
            Hash,
            AssetOrigin.Local,
            null,
            null,
            null,
            null,
            Now));
    }

    private static string Hash => new('a', SubtitleTrack.ContentHashLength);
}
