using System.Security.Cryptography;
using AIStudio.Application.Assets;
using AIStudio.Application.Bibles;
using AIStudio.Application.IdentityAssets;
using AIStudio.Infrastructure.Assets;
using Microsoft.Extensions.Options;
using Xunit;

namespace AIStudio.Tests.IdentityAssets;

public sealed class LocalIdentityAssetStoreTests : IDisposable
{
    private readonly string _baseDirectory;
    private readonly string _root;
    private readonly string _outside;

    public LocalIdentityAssetStoreTests()
    {
        _baseDirectory = Path.Combine(
            Path.GetTempPath(),
            "aistudio-identity-asset-tests",
            Guid.NewGuid().ToString("N"));
        _root = Path.Combine(_baseDirectory, "root");
        _outside = Path.Combine(_baseDirectory, "outside");
        Directory.CreateDirectory(_root);
        Directory.CreateDirectory(_outside);
    }

    public void Dispose()
    {
        if (Directory.Exists(_baseDirectory))
        {
            Directory.Delete(_baseDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task ExistingKeyRejectsChangedBytesAndPreservesOriginal()
    {
        var store = Store();
        var id = new AssetReferenceId("student-ref");
        var version = new IdentityAssetVersion(1);
        await store.WriteAsync(id, version, new byte[] { 1 }, TestContext.Current.CancellationToken);

        var exception = await Assert.ThrowsAsync<AssetCollectionException>(
            () => store.WriteAsync(id, version, new byte[] { 2 }, TestContext.Current.CancellationToken));
        var persisted = await store.ReadBytesAsync(id, version, TestContext.Current.CancellationToken);

        Assert.Equal("identity_asset_content_conflict", exception.ErrorCode);
        Assert.Equal(new byte[] { 1 }, persisted.ToArray());
    }

    [Fact]
    public async Task WriteReturnsCorrectByteFactsAndReadReturnsIdenticalBytes()
    {
        var bytes = new byte[] { 1, 2, 3, 4, 5 };
        var store = Store();

        var blob = await store.WriteAsync(
            new AssetReferenceId("student-ref"),
            new IdentityAssetVersion(1),
            bytes,
            TestContext.Current.CancellationToken);
        var read = await store.ReadBytesAsync(
            new AssetReferenceId("student-ref"),
            new IdentityAssetVersion(1),
            TestContext.Current.CancellationToken);

        Assert.Equal(bytes.Length, blob.ByteSize);
        Assert.Equal(
            Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
            blob.ContentHash);
        Assert.Equal(bytes, read.ToArray());
    }

    [Fact]
    public async Task ExistsTracksWrittenKey()
    {
        var store = Store();
        var id = new AssetReferenceId("student-ref");
        var version = new IdentityAssetVersion(1);

        Assert.False(store.Exists(id, version));

        await store.WriteAsync(id, version, new byte[] { 1 }, TestContext.Current.CancellationToken);

        Assert.True(store.Exists(id, version));
    }

    [Fact]
    public async Task VersionsDoNotCollide()
    {
        var store = Store();
        var id = new AssetReferenceId("student-ref");

        await store.WriteAsync(id, new IdentityAssetVersion(1), new byte[] { 1 }, TestContext.Current.CancellationToken);
        await store.WriteAsync(id, new IdentityAssetVersion(2), new byte[] { 2 }, TestContext.Current.CancellationToken);

        var first = await store.ReadBytesAsync(id, new IdentityAssetVersion(1), TestContext.Current.CancellationToken);
        var second = await store.ReadBytesAsync(id, new IdentityAssetVersion(2), TestContext.Current.CancellationToken);

        Assert.Equal(new byte[] { 1 }, first.ToArray());
        Assert.Equal(new byte[] { 2 }, second.ToArray());
    }

    [Fact]
    public async Task AssetIdsDoNotCollide()
    {
        var store = Store();
        var version = new IdentityAssetVersion(1);

        await store.WriteAsync(new AssetReferenceId("student-ref"), version, new byte[] { 1 }, TestContext.Current.CancellationToken);
        await store.WriteAsync(new AssetReferenceId("world-ref"), version, new byte[] { 2 }, TestContext.Current.CancellationToken);

        var first = await store.ReadBytesAsync(new AssetReferenceId("student-ref"), version, TestContext.Current.CancellationToken);
        var second = await store.ReadBytesAsync(new AssetReferenceId("world-ref"), version, TestContext.Current.CancellationToken);

        Assert.Equal(new byte[] { 1 }, first.ToArray());
        Assert.Equal(new byte[] { 2 }, second.ToArray());
    }

    [Fact]
    public async Task InvalidIdsCannotCreateTraversal()
    {
        Assert.Throws<ArgumentException>(() => new AssetReferenceId("../escape"));

        await Assert.ThrowsAsync<ArgumentException>(
            () => Store().WriteAsync(
                default,
                new IdentityAssetVersion(1),
                new byte[] { 1 },
                TestContext.Current.CancellationToken));
        Assert.Empty(Directory.EnumerateFiles(_baseDirectory, "*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task ExistingSymlinkSafetyIsPreserved()
    {
        Directory.CreateSymbolicLink(Path.Combine(_root, "identity-assets"), _outside);

        var exception = await Assert.ThrowsAsync<AssetCollectionException>(
            () => Store().WriteAsync(
                new AssetReferenceId("student-ref"),
                new IdentityAssetVersion(1),
                new byte[] { 1 },
                TestContext.Current.CancellationToken));

        Assert.Equal("asset_path_invalid", exception.ErrorCode);
        Assert.Empty(Directory.EnumerateFiles(_outside));
    }

    [Fact]
    public void PublicContractExposesNoPhysicalPathOrStorageKey()
    {
        Assert.Equal(
            ["ByteSize", "ContentHash"],
            typeof(IdentityAssetBlob).GetProperties().Select(property => property.Name));
        Assert.DoesNotContain(
            typeof(IIdentityAssetStore).GetMethods(),
            method => method.ReturnType.Name.Contains("Path", StringComparison.OrdinalIgnoreCase));
        Assert.Empty(typeof(LocalIdentityAssetStore).GetProperties());
    }

    private LocalIdentityAssetStore Store()
    {
        var fileStore = new LocalAssetFileStore(
            Options.Create(new AssetStorageOptions { RootPath = _root }));
        return new LocalIdentityAssetStore(fileStore);
    }
}
