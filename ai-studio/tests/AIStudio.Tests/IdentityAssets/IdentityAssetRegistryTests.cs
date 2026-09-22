using AIStudio.Application.Bibles;
using AIStudio.Application.IdentityAssets;
using Xunit;

namespace AIStudio.Tests.IdentityAssets;

public sealed class IdentityAssetRegistryTests
{
    [Fact]
    public void FirstVersionIsOne()
    {
        var registry = new IdentityAssetRegistry();

        Assert.Equal(1, registry.NextVersion(new AssetReferenceId("student-ref")).Value);
    }

    [Fact]
    public void RegisteredVersionAdvancesNextVersion()
    {
        var registry = Registry(IdentityAssetTestSupport.Asset(version: 1));

        Assert.Equal(2, registry.NextVersion(new AssetReferenceId("student-ref")).Value);
    }

    [Fact]
    public void VersionsAreIndependentPerAssetId()
    {
        var registry = Registry(IdentityAssetTestSupport.Asset("student-ref", version: 3));

        Assert.Equal(4, registry.NextVersion(new AssetReferenceId("student-ref")).Value);
        Assert.Equal(1, registry.NextVersion(new AssetReferenceId("world-ref")).Value);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void InvalidVersionIsRejected(int value)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new IdentityAssetVersion(value));
    }

    [Fact]
    public void DraftRegistrationAndExactLookupSucceed()
    {
        var asset = IdentityAssetTestSupport.Asset();
        var registry = Registry(asset);

        var stored = registry.Get(asset.Id, asset.Version);

        Assert.Equal(IdentityAssetStatus.Draft, stored.Status);
        Assert.Equal(asset.Id, stored.Id);
        Assert.Equal(asset.Version, stored.Version);
        Assert.Equal(asset.ContentHash, stored.ContentHash);
    }

    [Fact]
    public void DuplicateKeyIsRejected()
    {
        var asset = IdentityAssetTestSupport.Asset();
        var registry = Registry(asset);

        Assert.Throws<InvalidOperationException>(() => registry.Register(asset));
    }

    [Fact]
    public void LatestReturnsHighestVersion()
    {
        var registry = Registry(
            IdentityAssetTestSupport.Asset(version: 3),
            IdentityAssetTestSupport.Asset(version: 1),
            IdentityAssetTestSupport.Asset(version: 2));

        Assert.Equal(3, registry.GetLatest(new AssetReferenceId("student-ref")).Version.Value);
    }

    [Fact]
    public void DraftIsNotLatestApproved()
    {
        var registry = Registry(IdentityAssetTestSupport.Asset());

        Assert.False(registry.TryGetLatestApproved(new AssetReferenceId("student-ref"), out _));
        Assert.Throws<KeyNotFoundException>(
            () => registry.GetLatestApproved(new AssetReferenceId("student-ref")));
    }

    [Fact]
    public void ApprovalSucceeds()
    {
        var registry = Registry(IdentityAssetTestSupport.Asset());

        var approved = registry.Approve(
            new AssetReferenceId("student-ref"),
            new IdentityAssetVersion(1));

        Assert.Equal(IdentityAssetStatus.Approved, approved.Status);
        Assert.Equal(ApprovedAt, approved.ApprovedAt);
    }

    [Fact]
    public void RepeatedApprovalIsIdempotentAndKeepsApprovedAtStable()
    {
        var registry = Registry(IdentityAssetTestSupport.Asset());

        var first = registry.Approve(
            new AssetReferenceId("student-ref"),
            new IdentityAssetVersion(1));
        var second = registry.Approve(
            new AssetReferenceId("student-ref"),
            new IdentityAssetVersion(1));

        Assert.Same(first, second);
        Assert.Equal(first.ApprovedAt, second.ApprovedAt);
    }

    [Fact]
    public void LatestApprovedIgnoresHigherDraft()
    {
        var registry = Registry(
            IdentityAssetTestSupport.Asset(version: 1),
            IdentityAssetTestSupport.Asset(version: 2));
        registry.Approve(new AssetReferenceId("student-ref"), new IdentityAssetVersion(1));

        var approved = registry.GetLatestApproved(new AssetReferenceId("student-ref"));

        Assert.Equal(1, approved.Version.Value);
        Assert.Equal(2, registry.GetLatest(new AssetReferenceId("student-ref")).Version.Value);
    }

    [Fact]
    public void ApprovedMetadataCannotBeMutatedThroughRegistry()
    {
        var parents = new List<AssetReferenceId> { new("source-ref") };
        var registry = Registry(IdentityAssetTestSupport.Asset(parents: parents));
        var approved = registry.Approve(
            new AssetReferenceId("student-ref"),
            new IdentityAssetVersion(1));

        parents.Add(new AssetReferenceId("later-ref"));
        var changedCopy = approved with { Kind = "rig" };

        Assert.Equal("reference-image", registry.Get(approved.Id, approved.Version).Kind);
        Assert.Equal(["source-ref"], registry.Get(approved.Id, approved.Version)
            .Provenance.ParentAssetIds.Select(parent => parent.Value));
        Assert.Equal("rig", changedCopy.Kind);
    }

    [Fact]
    public void ApprovedAssetCannotBeRegisteredAsNewMetadata()
    {
        var approved = IdentityAssetTestSupport.Asset() with
        {
            Status = IdentityAssetStatus.Approved,
            ApprovedAt = ApprovedAt
        };

        Assert.Throws<InvalidOperationException>(() => new IdentityAssetRegistry().Register(approved));
    }

    [Fact]
    public void StructurallyInvalidMetadataIsRejected()
    {
        var invalid = IdentityAssetTestSupport.Asset() with
        {
            MediaType = "not-a-media-type"
        };

        var exception = Assert.Throws<InvalidOperationException>(
            () => new IdentityAssetRegistry().Register(invalid));

        Assert.Contains(IdentityAssetIssueCodes.MediaTypeInvalid, exception.Message);
    }

    private static readonly DateTimeOffset ApprovedAt =
        new(2026, 9, 22, 2, 0, 0, TimeSpan.Zero);

    private static IdentityAssetRegistry Registry(params IdentityAsset[] assets) =>
        new(assets, new FixedTimeProvider(ApprovedAt));
}
