using AIStudio.Application.Bibles;
using AIStudio.Application.Capabilities;
using AIStudio.Application.IdentityAssets;
using AIStudio.Infrastructure.Assets;
using Microsoft.Extensions.Options;
using Xunit;

namespace AIStudio.Tests.IdentityAssets;

/// <summary>
/// Restart durability proof for registry-owned identity-asset metadata. Each
/// "process" is a freshly constructed registry over the same local metadata root.
/// </summary>
public sealed class IdentityAssetPersistenceTests : IDisposable
{
    private static readonly DateTimeOffset ApprovedAt =
        new(2026, 9, 22, 3, 0, 0, TimeSpan.Zero);

    private readonly string root;

    public IdentityAssetPersistenceTests()
    {
        root = Path.Combine(
            Path.GetTempPath(),
            "aistudio-identity-persistence-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
    }

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void DraftRegisterSurvivesRestart()
    {
        var asset = IdentityAssetTestSupport.Asset();
        Open().Register(asset);

        var reopened = Open().Get(asset.Id, asset.Version);

        Assert.Equal(IdentityAssetStatus.Draft, reopened.Status);
        Assert.Equal(asset.Id, reopened.Id);
        Assert.Equal(asset.Version, reopened.Version);
        Assert.Equal(asset.Kind, reopened.Kind);
        Assert.Equal(asset.MediaType, reopened.MediaType);
        Assert.Equal(asset.ByteSize, reopened.ByteSize);
        Assert.Equal(asset.ContentHash, reopened.ContentHash);
        Assert.Equal(asset.CreatedAt, reopened.CreatedAt);
    }

    [Fact]
    public void ApprovalAndApprovedAtSurviveRestart()
    {
        var registry = Open();
        registry.Register(IdentityAssetTestSupport.Asset());
        registry.Approve(new AssetReferenceId("student-ref"), new IdentityAssetVersion(1));

        var reopened = Open();
        var asset = reopened.Get(new AssetReferenceId("student-ref"), new IdentityAssetVersion(1));

        Assert.Equal(IdentityAssetStatus.Approved, asset.Status);
        Assert.Equal(ApprovedAt, asset.ApprovedAt);
        Assert.Equal(1, reopened.GetLatestApproved(new AssetReferenceId("student-ref")).Version.Value);
    }

    [Fact]
    public void LatestAndLatestApprovedSurviveRestart()
    {
        var registry = Open();
        registry.Register(IdentityAssetTestSupport.Asset(version: 1));
        registry.Register(IdentityAssetTestSupport.Asset(version: 2));
        registry.Approve(new AssetReferenceId("student-ref"), new IdentityAssetVersion(1));

        var reopened = Open();

        Assert.Equal(2, reopened.GetLatest(new AssetReferenceId("student-ref")).Version.Value);
        Assert.Equal(1, reopened.GetLatestApproved(new AssetReferenceId("student-ref")).Version.Value);
    }

    [Fact]
    public void NextVersionSurvivesRestart()
    {
        Open().Register(IdentityAssetTestSupport.Asset(version: 1));

        Assert.Equal(2, Open().NextVersion(new AssetReferenceId("student-ref")).Value);
    }

    [Fact]
    public void DuplicateVersionIsRejectedAfterRestart()
    {
        var asset = IdentityAssetTestSupport.Asset();
        Open().Register(asset);

        Assert.Throws<InvalidOperationException>(() => Open().Register(asset));
    }

    [Fact]
    public void DurableCreateCollisionIsRejected()
    {
        var asset = IdentityAssetTestSupport.Asset();
        var persistence = Persistence();
        persistence.Create(asset);

        var exception = Assert.Throws<InvalidOperationException>(() => persistence.Create(asset));

        Assert.Contains("already exists durably", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AllProvenanceFieldsRoundTrip()
    {
        var asset = IdentityAssetTestSupport.Asset() with
        {
            Provenance = new IdentityAssetProvenance
            {
                SourceBibleId = "student",
                SourceBibleVersion = 2,
                CapabilityId = new CapabilityId("visual.ai_image"),
                ProviderId = "comfyui-flux",
                Seed = 4242,
                PromptHash = new string('c', IdentityAsset.ContentHashLength),
                ParentAssetIds =
                [
                    new AssetReferenceId("parent-one"),
                    new AssetReferenceId("parent-two")
                ]
            }
        };
        Open().Register(asset);

        var provenance = Open().Get(asset.Id, asset.Version).Provenance;

        Assert.Equal("student", provenance.SourceBibleId);
        Assert.Equal(2, provenance.SourceBibleVersion);
        Assert.Equal("visual.ai_image", provenance.CapabilityId!.Value.Value);
        Assert.Equal("comfyui-flux", provenance.ProviderId);
        Assert.Equal(4242, provenance.Seed);
        Assert.Equal(new string('c', IdentityAsset.ContentHashLength), provenance.PromptHash);
        Assert.Equal(
            ["parent-one", "parent-two"],
            provenance.ParentAssetIds.Select(parent => parent.Value));
    }

    [Fact]
    public void RepeatedApprovalAfterRestartKeepsApprovedAtStable()
    {
        var registry = Open();
        registry.Register(IdentityAssetTestSupport.Asset());
        var approved = registry.Approve(
            new AssetReferenceId("student-ref"),
            new IdentityAssetVersion(1));

        var reopened = Open();
        var repeated = reopened.Approve(
            new AssetReferenceId("student-ref"),
            new IdentityAssetVersion(1));

        Assert.Equal(approved.ApprovedAt, repeated.ApprovedAt);
        Assert.Equal(IdentityAssetStatus.Approved, repeated.Status);
    }

    [Fact]
    public void ResolverUsesDurableStateAfterRestart()
    {
        var registry = Open();
        registry.Register(IdentityAssetTestSupport.Asset(version: 1));
        registry.Register(IdentityAssetTestSupport.Asset(version: 2));
        registry.Approve(new AssetReferenceId("student-ref"), new IdentityAssetVersion(1));
        var resolver = new IdentityAssetResolver(Open());

        var floating = resolver.Resolve(Reference(version: null));
        Assert.True(floating.IsSuccess);
        Assert.Equal(1, floating.Value!.Version.Value);

        var pinned = resolver.Resolve(Reference(version: 1));
        Assert.True(pinned.IsSuccess);
        Assert.Equal(1, pinned.Value!.Version.Value);

        var draft = resolver.Resolve(Reference(version: 2));
        Assert.False(draft.IsSuccess);
        Assert.Equal(IdentityAssetResolutionIssueCodes.NotApproved, draft.Issues.Single().Code);
    }

    [Fact]
    public void PinnedReferenceResolvesAfterRestart()
    {
        Open().Register(IdentityAssetTestSupport.Asset(version: 1));
        var registry = Open();
        registry.Approve(new AssetReferenceId("student-ref"), new IdentityAssetVersion(1));

        // A fresh resolver over reopened durable state resolves the previously
        // pinned concrete version, the same lookup execution performs.
        var resolution = new IdentityAssetResolver(Open()).Resolve(Reference(version: 1));

        Assert.True(resolution.IsSuccess);
        Assert.Equal("student-ref", resolution.Value!.AssetId.Value);
        Assert.Equal(1, resolution.Value.Version.Value);
    }

    [Fact]
    public void CorruptMetadataFailsClearly()
    {
        var directory = Path.Combine(root, "identity-asset-metadata", "student-ref");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "v1.json"), "{not json");

        var exception = Assert.Throws<InvalidOperationException>(() => Open());

        Assert.Contains("corrupt", exception.Message, StringComparison.Ordinal);
    }

    private IdentityAssetRegistry Open() =>
        new(timeProvider: new FixedTimeProvider(ApprovedAt), persistence: Persistence());

    private LocalIdentityAssetMetadataPersistence Persistence() =>
        new(Options.Create(new AssetStorageOptions { RootPath = root }));

    private static AssetReference Reference(int? version) => new()
    {
        AssetId = new AssetReferenceId("student-ref"),
        Version = version is null ? null : new IdentityAssetVersion(version.Value),
        Purpose = "character-primary-reference"
    };
}
