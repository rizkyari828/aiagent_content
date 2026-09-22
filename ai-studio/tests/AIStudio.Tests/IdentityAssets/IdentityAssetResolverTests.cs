using AIStudio.Application.Bibles;
using AIStudio.Application.IdentityAssets;
using Xunit;

namespace AIStudio.Tests.IdentityAssets;

public sealed class IdentityAssetResolverTests
{
    [Fact]
    public void ExplicitApprovedVersionResolvesAndPins()
    {
        var registry = Registry(
            IdentityAssetTestSupport.Asset(version: 1),
            IdentityAssetTestSupport.Asset(version: 2));
        registry.Approve(Id, new IdentityAssetVersion(1));
        registry.Approve(Id, new IdentityAssetVersion(2));

        var result = new IdentityAssetResolver(registry)
            .Resolve(IdentityAssetTestSupport.Reference(version: 1));

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value!.Version.Value);
        Assert.Equal(1, result.Metadata!.Version.Value);
        Assert.Equal("character-primary-reference", result.MaterializedReference!.Purpose);
        Assert.Equal("default", result.MaterializedReference!.Variant);
    }

    [Fact]
    public void ExplicitDraftVersionFailsNotApproved()
    {
        var registry = Registry(IdentityAssetTestSupport.Asset());

        var result = new IdentityAssetResolver(registry)
            .Resolve(IdentityAssetTestSupport.Reference(version: 1));

        Assert.False(result.IsSuccess);
        Assert.Equal(IdentityAssetResolutionIssueCodes.NotApproved, result.Issues.Single().Code);
    }

    [Fact]
    public void ExplicitMissingVersionFailsNotFoundWithoutFallback()
    {
        var registry = Registry(IdentityAssetTestSupport.Asset(version: 1));
        registry.Approve(Id, new IdentityAssetVersion(1));

        var result = new IdentityAssetResolver(registry)
            .Resolve(IdentityAssetTestSupport.Reference(version: 2));

        Assert.Equal(IdentityAssetResolutionIssueCodes.NotFound, result.Issues.Single().Code);
    }

    [Fact]
    public void FloatingReferenceResolvesLatestApproved()
    {
        var registry = Registry(
            IdentityAssetTestSupport.Asset(version: 1),
            IdentityAssetTestSupport.Asset(version: 2));
        registry.Approve(Id, new IdentityAssetVersion(1));
        registry.Approve(Id, new IdentityAssetVersion(2));

        var result = new IdentityAssetResolver(registry)
            .Resolve(IdentityAssetTestSupport.Reference());

        Assert.Equal(2, result.Value!.Version.Value);
        Assert.Equal(2, result.Metadata!.Version.Value);
    }

    [Fact]
    public void FloatingReferenceWithOnlyDraftFailsNotApproved()
    {
        var result = new IdentityAssetResolver(
                Registry(IdentityAssetTestSupport.Asset()))
            .Resolve(IdentityAssetTestSupport.Reference());

        Assert.Equal(IdentityAssetResolutionIssueCodes.NotApproved, result.Issues.Single().Code);
    }

    [Fact]
    public void FloatingReferenceWithNoRecordsFailsNotFound()
    {
        var result = new IdentityAssetResolver(new IdentityAssetRegistry())
            .Resolve(IdentityAssetTestSupport.Reference());

        Assert.Equal(IdentityAssetResolutionIssueCodes.NotFound, result.Issues.Single().Code);
    }

    [Fact]
    public void HigherDraftDoesNotOverrideOlderApproved()
    {
        var registry = Registry(
            IdentityAssetTestSupport.Asset(version: 1),
            IdentityAssetTestSupport.Asset(version: 2));
        registry.Approve(Id, new IdentityAssetVersion(1));

        var result = new IdentityAssetResolver(registry)
            .Resolve(IdentityAssetTestSupport.Reference());

        Assert.Equal(1, result.Value!.Version.Value);
        Assert.Equal(IdentityAssetStatus.Approved, result.Metadata!.Status);
    }

    [Fact]
    public void MaterializedResolutionDoesNotReresolveAfterNewApproval()
    {
        var registry = Registry(
            IdentityAssetTestSupport.Asset(version: 1),
            IdentityAssetTestSupport.Asset(version: 2));
        registry.Approve(Id, new IdentityAssetVersion(1));
        var resolution = new IdentityAssetResolver(registry)
            .Resolve(IdentityAssetTestSupport.Reference());
        var materialized = resolution.Value!;

        registry.Approve(Id, new IdentityAssetVersion(2));

        Assert.Equal(1, materialized.Version.Value);
        Assert.Equal(1, resolution.Metadata!.Version.Value);
        Assert.Equal(2, registry.GetLatestApproved(Id).Version.Value);
    }

    private static readonly AssetReferenceId Id = new("student-ref");

    private static IdentityAssetRegistry Registry(params IdentityAsset[] assets) =>
        new(assets, new FixedTimeProvider(
            new DateTimeOffset(2026, 9, 22, 2, 0, 0, TimeSpan.Zero)));
}
