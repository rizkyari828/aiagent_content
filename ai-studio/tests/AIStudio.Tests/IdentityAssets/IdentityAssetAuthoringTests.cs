using System.Reflection;
using System.Security.Cryptography;
using AIStudio.Application.Bibles;
using AIStudio.Application.IdentityAssets;
using AIStudio.Infrastructure.Assets;
using Microsoft.Extensions.Options;
using Xunit;

namespace AIStudio.Tests.IdentityAssets;

/// <summary>
/// Import/approval authoring semantics: import an EXISTING PNG into stored bytes +
/// Draft metadata, and approve one concrete version explicitly. No generation, no
/// transport, no provider.
/// </summary>
public sealed class IdentityAssetAuthoringTests : IDisposable
{
    private static readonly DateTimeOffset CreatedAt =
        new(2026, 9, 23, 1, 0, 0, TimeSpan.Zero);

    private static readonly AssetReferenceId ReferenceId = new("student-01-reference");

    // A real 1x1 PNG (signature + valid chunks) so the fixture is deterministic.
    private static readonly byte[] ValidPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");

    private static readonly string ExpectedHash =
        Convert.ToHexString(SHA256.HashData(ValidPng)).ToLowerInvariant();

    private readonly string root;

    public IdentityAssetAuthoringTests()
    {
        root = Path.Combine(
            Path.GetTempPath(),
            "aistudio-identity-authoring-tests",
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
    public async Task Import_WritesBytesAndDraftMetadata()
    {
        var registry = Registry();
        var store = Store();

        var asset = await Import(registry, store).ImportAsync(
            Request(),
            TestContext.Current.CancellationToken);

        Assert.Equal(IdentityAssetStatus.Draft, asset.Status);
        Assert.Equal(1, asset.Version.Value);
        Assert.Equal(ValidPng.Length, asset.ByteSize);
        Assert.Equal(ExpectedHash, asset.ContentHash);

        // Bytes are the store's authority and match the imported content.
        Assert.True(store.Exists(ReferenceId, asset.Version));
        var persisted = await store.ReadBytesAsync(
            ReferenceId,
            asset.Version,
            TestContext.Current.CancellationToken);
        Assert.Equal(ValidPng, persisted.ToArray());

        // Registry metadata matches the stored bytes exactly.
        var metadata = registry.Get(ReferenceId, asset.Version);
        Assert.Equal(ValidPng.Length, metadata.ByteSize);
        Assert.Equal(ExpectedHash, metadata.ContentHash);
    }

    [Fact]
    public async Task Import_AllocatesNextVersionPerImport()
    {
        var registry = Registry();
        var store = Store();
        var workflow = Import(registry, store);

        var first = await workflow.ImportAsync(Request(), TestContext.Current.CancellationToken);
        var second = await workflow.ImportAsync(
            Request(ReplacementBytes()),
            TestContext.Current.CancellationToken);

        Assert.Equal(1, first.Version.Value);
        Assert.Equal(2, second.Version.Value);
        Assert.Equal(IdentityAssetStatus.Draft, second.Status);
        Assert.Equal(2, registry.GetLatest(ReferenceId).Version.Value);
    }

    [Fact]
    public async Task Import_LeavesExistingApprovedVersionUnchanged()
    {
        var registry = Registry();
        var store = Store();
        var workflow = Import(registry, store);

        var first = await workflow.ImportAsync(Request(), TestContext.Current.CancellationToken);
        Approval(registry).Approve(ReferenceId, first.Version);

        await workflow.ImportAsync(Request(ReplacementBytes()), TestContext.Current.CancellationToken);

        var approved = registry.Get(ReferenceId, new IdentityAssetVersion(1));
        Assert.Equal(IdentityAssetStatus.Approved, approved.Status);
        Assert.Equal(ExpectedHash, approved.ContentHash);
        Assert.Equal(1, registry.GetLatestApproved(ReferenceId).Version.Value);
        Assert.Equal(2, registry.GetLatest(ReferenceId).Version.Value);
    }

    [Fact]
    public async Task Import_EmptyContentRejectedBeforeAnyMutation()
    {
        var registry = Registry();
        var store = Store();

        var exception = await Assert.ThrowsAsync<IdentityAssetAuthoringException>(
            () => Import(registry, store).ImportAsync(
                Request([]),
                TestContext.Current.CancellationToken));

        Assert.Equal("identity_import_content_empty", exception.ErrorCode);
        Assert.Empty(registry.Assets);
        Assert.False(store.Exists(ReferenceId, new IdentityAssetVersion(1)));
    }

    [Fact]
    public async Task Import_InvalidPngRejectedBeforeAnyMutation()
    {
        var registry = Registry();
        var store = Store();

        var exception = await Assert.ThrowsAsync<IdentityAssetAuthoringException>(
            () => Import(registry, store).ImportAsync(
                Request([1, 2, 3]),
                TestContext.Current.CancellationToken));

        Assert.Equal("identity_import_png_invalid", exception.ErrorCode);
        Assert.Empty(registry.Assets);
        Assert.False(store.Exists(ReferenceId, new IdentityAssetVersion(1)));
    }

    [Fact]
    public async Task Import_UnsupportedKindRejected()
    {
        var registry = Registry();

        var exception = await Assert.ThrowsAsync<IdentityAssetAuthoringException>(
            () => Import(registry, Store()).ImportAsync(
                Request(kind: "voice-reference"),
                TestContext.Current.CancellationToken));

        Assert.Equal("identity_import_kind_unsupported", exception.ErrorCode);
    }

    [Fact]
    public async Task Import_UnsupportedMediaTypeRejected()
    {
        var registry = Registry();

        var exception = await Assert.ThrowsAsync<IdentityAssetAuthoringException>(
            () => Import(registry, Store()).ImportAsync(
                Request(mediaType: "image/jpeg"),
                TestContext.Current.CancellationToken));

        Assert.Equal("identity_import_media_type_unsupported", exception.ErrorCode);
    }

    [Fact]
    public async Task Import_PreservesSuppliedProvenance()
    {
        var registry = Registry();
        var provenance = new IdentityAssetProvenance
        {
            ProviderId = "user-upload",
            Seed = 7,
            PromptHash = new string('a', IdentityAsset.ContentHashLength)
        };

        var asset = await Import(registry, Store()).ImportAsync(
            Request(provenance: provenance),
            TestContext.Current.CancellationToken);

        Assert.Equal("user-upload", asset.Provenance.ProviderId);
        Assert.Equal(7, asset.Provenance.Seed);
    }

    [Fact]
    public async Task Approval_ApprovesExplicitDraftVersion()
    {
        var registry = Registry();
        var asset = await Import(registry, Store()).ImportAsync(
            Request(),
            TestContext.Current.CancellationToken);

        var approved = Approval(registry).Approve(ReferenceId, asset.Version);

        Assert.Equal(IdentityAssetStatus.Approved, approved.Status);
        Assert.Equal(CreatedAt, approved.ApprovedAt);
        Assert.Equal(1, registry.GetLatestApproved(ReferenceId).Version.Value);
    }

    [Fact]
    public async Task Approval_MissingVersionFails()
    {
        var registry = Registry();
        await Import(registry, Store()).ImportAsync(Request(), TestContext.Current.CancellationToken);

        Assert.Throws<KeyNotFoundException>(
            () => Approval(registry).Approve(ReferenceId, new IdentityAssetVersion(9)));
    }

    [Fact]
    public void Approval_RejectsFloatingVersion()
    {
        var exception = Assert.Throws<IdentityAssetAuthoringException>(
            () => Approval(Registry()).Approve(ReferenceId, default));

        Assert.Equal("identity_approval_invalid_version", exception.ErrorCode);
    }

    [Fact]
    public void Approval_HasNoApproveLatestOverload()
    {
        var methods = typeof(ApproveIdentityAssetWorkflow)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(method => method.DeclaringType == typeof(ApproveIdentityAssetWorkflow))
            .ToArray();

        var approve = Assert.Single(methods);
        var parameters = approve.GetParameters();
        Assert.Equal(typeof(AssetReferenceId), parameters[0].ParameterType);
        Assert.Equal(typeof(IdentityAssetVersion), parameters[1].ParameterType);
    }

    [Fact]
    public async Task Approval_PersistsAcrossRegistryRecreation()
    {
        var registry = Registry(Persistence());
        var asset = await Import(registry, Store()).ImportAsync(
            Request(),
            TestContext.Current.CancellationToken);
        Approval(registry).Approve(ReferenceId, asset.Version);

        var reopened = Registry(Persistence());
        var stored = reopened.Get(ReferenceId, asset.Version);

        Assert.Equal(IdentityAssetStatus.Approved, stored.Status);
        Assert.Equal(CreatedAt, stored.ApprovedAt);
        Assert.Equal(1, reopened.GetLatestApproved(ReferenceId).Version.Value);
    }

    [Fact]
    public async Task Resolver_FloatingReferenceFollowsLatestApproved()
    {
        var registry = Registry();
        var store = Store();
        var workflow = Import(registry, store);

        var first = await workflow.ImportAsync(Request(), TestContext.Current.CancellationToken);
        Approval(registry).Approve(ReferenceId, first.Version);

        var resolver = new IdentityAssetResolver(registry);
        Assert.Equal(1, ResolveLatest(resolver).Value);

        var second = await workflow.ImportAsync(
            Request(ReplacementBytes()),
            TestContext.Current.CancellationToken);
        Assert.Equal(1, ResolveLatest(resolver).Value);

        Approval(registry).Approve(ReferenceId, second.Version);
        Assert.Equal(2, ResolveLatest(resolver).Value);
    }

    [Fact]
    public async Task RealisticFixture_ImportApproveImportApprove()
    {
        var persistence = Persistence();
        var store = Store();
        var first = Registry(persistence);
        var workflow = Import(first, store);

        var v1 = await workflow.ImportAsync(Request(), TestContext.Current.CancellationToken);
        Assert.Equal(1, v1.Version.Value);
        Assert.Equal(IdentityAssetStatus.Draft, v1.Status);
        Approval(first).Approve(ReferenceId, v1.Version);

        var reopened = Registry(Persistence());
        Assert.Equal(
            IdentityAssetStatus.Approved,
            reopened.Get(ReferenceId, new IdentityAssetVersion(1)).Status);

        var v2 = await Import(reopened, store).ImportAsync(
            Request(ReplacementBytes()),
            TestContext.Current.CancellationToken);
        Assert.Equal(2, v2.Version.Value);
        Assert.Equal(IdentityAssetStatus.Draft, v2.Status);
        Assert.Equal(1, reopened.GetLatestApproved(ReferenceId).Version.Value);

        reopened.Approve(ReferenceId, v2.Version);
        Assert.Equal(2, reopened.GetLatestApproved(ReferenceId).Version.Value);
        Assert.Equal(
            IdentityAssetStatus.Approved,
            reopened.Get(ReferenceId, new IdentityAssetVersion(1)).Status);
    }

    [Fact]
    public void AuthoringContractExposesNoPhysicalStorageOrProviderDetail()
    {
        Assert.Equal(
            ["AssetId", "Kind", "MediaType", "Content", "Provenance"],
            typeof(ImportIdentityAssetRequest).GetProperties().Select(property => property.Name));

        string[] forbidden = ["Path", "StorageKey", "Url", "Uri", "Workflow", "Checkpoint", "Absolute"];
        foreach (var property in typeof(ImportIdentityAssetRequest).GetProperties())
        {
            foreach (var fragment in forbidden)
            {
                Assert.DoesNotContain(fragment, property.Name, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    private static IdentityAssetVersion ResolveLatest(IdentityAssetResolver resolver)
    {
        var resolution = resolver.Resolve(new AssetReference { AssetId = ReferenceId });
        Assert.True(resolution.IsSuccess);
        return resolution.Value!.Version;
    }

    private static byte[] ReplacementBytes() => [.. ValidPng, 0];

    private IdentityAssetRegistry Registry(IIdentityAssetMetadataPersistence? persistence = null) =>
        new(timeProvider: new FixedTimeProvider(CreatedAt), persistence: persistence);

    private LocalIdentityAssetMetadataPersistence Persistence() =>
        new(Options.Create(new AssetStorageOptions { RootPath = root }));

    private LocalIdentityAssetStore Store() =>
        new(new LocalAssetFileStore(Options.Create(new AssetStorageOptions { RootPath = root })));

    private ImportIdentityAssetWorkflow Import(
        IdentityAssetRegistry registry,
        IIdentityAssetStore store) =>
        new(store, registry, new FixedTimeProvider(CreatedAt));

    private static ApproveIdentityAssetWorkflow Approval(IdentityAssetRegistry registry) => new(registry);

    private static ImportIdentityAssetRequest Request(
        byte[]? content = null,
        string kind = ImportIdentityAssetWorkflow.SupportedKind,
        string mediaType = ImportIdentityAssetWorkflow.SupportedMediaType,
        IdentityAssetProvenance? provenance = null) =>
        new(ReferenceId, kind, mediaType, content ?? ValidPng, provenance);
}
