using AIStudio.Domain.Assets;
using Xunit;

namespace AIStudio.Tests.Assets;

public sealed class SceneAssetTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 18, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_RecordsLocalAssetMetadata()
    {
        var projectId = Guid.NewGuid();
        var jobId = Guid.NewGuid();

        var asset = SceneAsset.Create(
            projectId,
            jobId,
            2,
            AssetType.Image,
            "scene-2.png",
            128,
            Hash,
            AssetOrigin.Local,
            source: null,
            creator: "Editor",
            license: null,
            retrievedAt: null,
            Now);

        Assert.Equal(projectId, asset.ContentProjectId);
        Assert.Equal(jobId, asset.SourceJobId);
        Assert.Equal(2, asset.SceneIndex);
        Assert.Equal("scene-2.png", asset.Path);
        Assert.Equal(Hash, asset.ContentHash);
        Assert.Equal(AssetOrigin.Local, asset.Origin);
        Assert.Equal("Editor", asset.Creator);
        Assert.Null(asset.RetrievedAt);
        Assert.Equal(Now, asset.CreatedAt);
    }

    [Fact]
    public void Create_RequiresSourceForExternalAsset()
    {
        var exception = Assert.Throws<ArgumentException>(() => SceneAsset.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            0,
            AssetType.Image,
            "scene-0.png",
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
    public void Create_RequiresLicenseForExternalAsset()
    {
        var exception = Assert.Throws<ArgumentException>(() => SceneAsset.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            0,
            AssetType.Image,
            "scene-0.png",
            10,
            Hash,
            AssetOrigin.External,
            source: "https://example.test/asset",
            creator: null,
            license: null,
            retrievedAt: null,
            Now));

        Assert.Equal("license", exception.ParamName);
    }

    [Fact]
    public void Create_DefaultsExternalRetrievalTime()
    {
        var asset = SceneAsset.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            0,
            AssetType.Video,
            "scene-0.mp4",
            10,
            Hash,
            AssetOrigin.External,
            source: "https://example.test/asset",
            creator: null,
            license: "CC0",
            retrievedAt: null,
            Now);

        Assert.Equal(Now, asset.RetrievedAt);
    }

    [Theory]
    [InlineData("not-a-hash")]
    [InlineData("abc123")]
    public void Create_RejectsInvalidContentHash(string hash)
    {
        Assert.Throws<ArgumentException>(() => SceneAsset.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            0,
            AssetType.Image,
            "scene-0.png",
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
    public void Create_RejectsNegativeSceneIndex()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => SceneAsset.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            -1,
            AssetType.Image,
            "scene-0.png",
            10,
            Hash,
            AssetOrigin.Local,
            null,
            null,
            null,
            null,
            Now));
    }

    private static string Hash => new('a', SceneAsset.ContentHashLength);
}
