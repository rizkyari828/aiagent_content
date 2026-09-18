using AIStudio.Domain.Assets;
using AIStudio.Domain.Narration;
using Xunit;

namespace AIStudio.Tests.Narration;

public sealed class NarrationTrackTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 18, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_RecordsLocalNarrationMetadata()
    {
        var projectId = Guid.NewGuid();
        var jobId = Guid.NewGuid();

        var track = NarrationTrack.Create(
            projectId,
            jobId,
            "narration.wav",
            512,
            Hash,
            AssetOrigin.Local,
            source: null,
            creator: "Voiceover artist",
            license: null,
            retrievedAt: null,
            Now);

        Assert.Equal(projectId, track.ContentProjectId);
        Assert.Equal(jobId, track.SourceJobId);
        Assert.Equal("narration.wav", track.Path);
        Assert.Equal(512, track.ByteSize);
        Assert.Equal(Hash, track.ContentHash);
        Assert.Equal(AssetOrigin.Local, track.Origin);
        Assert.Equal("Voiceover artist", track.Creator);
        Assert.Null(track.RetrievedAt);
        Assert.Equal(Now, track.CreatedAt);
    }

    [Fact]
    public void Create_RequiresSourceForExternalNarration()
    {
        var exception = Assert.Throws<ArgumentException>(() => NarrationTrack.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "narration.mp3",
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
    public void Create_RequiresLicenseForExternalNarration()
    {
        var exception = Assert.Throws<ArgumentException>(() => NarrationTrack.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "narration.mp3",
            10,
            Hash,
            AssetOrigin.External,
            source: "https://example.test/voice",
            creator: null,
            license: null,
            retrievedAt: null,
            Now));

        Assert.Equal("license", exception.ParamName);
    }

    [Fact]
    public void Create_DefaultsExternalRetrievalTime()
    {
        var track = NarrationTrack.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "narration.mp3",
            10,
            Hash,
            AssetOrigin.External,
            source: "https://example.test/voice",
            creator: null,
            license: "CC0",
            retrievedAt: null,
            Now);

        Assert.Equal(Now, track.RetrievedAt);
    }

    [Theory]
    [InlineData("not-a-hash")]
    [InlineData("abc123")]
    public void Create_RejectsInvalidContentHash(string hash)
    {
        Assert.Throws<ArgumentException>(() => NarrationTrack.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "narration.wav",
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
        Assert.Throws<ArgumentOutOfRangeException>(() => NarrationTrack.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "narration.wav",
            -1,
            Hash,
            AssetOrigin.Local,
            null,
            null,
            null,
            null,
            Now));
    }

    private static string Hash => new('a', NarrationTrack.ContentHashLength);
}
