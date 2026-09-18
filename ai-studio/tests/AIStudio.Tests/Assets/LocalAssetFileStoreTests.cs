using System.Security.Cryptography;
using AIStudio.Application.Assets;
using AIStudio.Infrastructure.Assets;
using Microsoft.Extensions.Options;
using Xunit;

namespace AIStudio.Tests.Assets;

public sealed class LocalAssetFileStoreTests : IDisposable
{
    private readonly string baseDirectory;
    private readonly string root;
    private readonly string outside;

    public LocalAssetFileStoreTests()
    {
        baseDirectory = Path.Combine(
            Path.GetTempPath(),
            "aistudio-asset-store-tests",
            Guid.NewGuid().ToString("N"));
        root = Path.Combine(baseDirectory, "root");
        outside = Path.Combine(baseDirectory, "outside");
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(outside);
    }

    public void Dispose()
    {
        if (Directory.Exists(baseDirectory))
        {
            Directory.Delete(baseDirectory, recursive: true);
        }
    }

    [Fact]
    public void Register_ReturnsRelativePathSizeAndIntegrityHash()
    {
        var bytes = new byte[] { 1, 2, 3, 4, 5 };
        WriteAsset("scene-0.png", bytes);

        var info = Store().Register("scene-0.png");

        Assert.Equal("scene-0.png", info.RelativePath);
        Assert.Equal(bytes.Length, info.ByteSize);
        Assert.Equal(
            Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
            info.ContentHash);
    }

    [Fact]
    public void Register_AcceptsNestedRelativePath()
    {
        WriteAsset(Path.Combine("images", "scene-1.png"), [9, 9]);

        var info = Store().Register("images/scene-1.png");

        Assert.Equal("images/scene-1.png", info.RelativePath);
    }

    [Theory]
    [InlineData("../outside/secret.png")]
    [InlineData("images/../../outside/secret.png")]
    [InlineData("..\\outside\\secret.png")]
    [InlineData("/etc/passwd")]
    [InlineData("C:\\Windows\\system32\\config\\sam")]
    public void Register_RejectsPathTraversalAndAbsolutePaths(string requestedPath)
    {
        var exception = Assert.Throws<AssetCollectionException>(
            () => Store().Register(requestedPath));

        Assert.Equal("asset_path_invalid", exception.ErrorCode);
    }

    [Fact]
    public void Register_RejectsMissingFile()
    {
        var exception = Assert.Throws<AssetCollectionException>(
            () => Store().Register("missing.png"));

        Assert.Equal("asset_file_not_found", exception.ErrorCode);
    }

    [Fact]
    public void Register_RejectsEmptyFile()
    {
        WriteAsset("empty.png", []);

        var exception = Assert.Throws<AssetCollectionException>(
            () => Store().Register("empty.png"));

        Assert.Equal("asset_file_invalid", exception.ErrorCode);
    }

    [Fact]
    public void Register_RejectsDirectory()
    {
        Directory.CreateDirectory(Path.Combine(root, "folder"));

        var exception = Assert.Throws<AssetCollectionException>(
            () => Store().Register("folder"));

        Assert.Equal("asset_file_not_found", exception.ErrorCode);
    }

    [Fact]
    public void Register_RejectsSymbolicLinkEscapingRoot()
    {
        var secret = Path.Combine(outside, "secret.png");
        File.WriteAllBytes(secret, [7, 7, 7]);
        File.CreateSymbolicLink(Path.Combine(root, "link.png"), secret);

        var exception = Assert.Throws<AssetCollectionException>(
            () => Store().Register("link.png"));

        Assert.Equal("asset_path_invalid", exception.ErrorCode);
    }

    [Fact]
    public void Register_RejectsSymbolicDirectoryEscapingRoot()
    {
        var secret = Path.Combine(outside, "secret.png");
        File.WriteAllBytes(secret, [7, 7, 7]);
        Directory.CreateSymbolicLink(Path.Combine(root, "link-dir"), outside);

        var exception = Assert.Throws<AssetCollectionException>(
            () => Store().Register("link-dir/secret.png"));

        Assert.Equal("asset_path_invalid", exception.ErrorCode);
    }

    [Fact]
    public async Task WriteAsync_WritesGeneratedBytesUnderRoot()
    {
        var bytes = new byte[] { 10, 20, 30, 40 };
        var store = Store();

        var info = await store.WriteAsync(
            "visuals/project/scene_0.png",
            bytes,
            TestContext.Current.CancellationToken);

        Assert.Equal("visuals/project/scene_0.png", info.RelativePath);
        Assert.Equal(bytes.Length, info.ByteSize);
        Assert.Equal(
            Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
            info.ContentHash);
        Assert.Equal(bytes, File.ReadAllBytes(Path.Combine(root, "visuals/project/scene_0.png")));
    }

    [Fact]
    public async Task WriteAsync_IsIdempotentForIdenticalContent()
    {
        var bytes = new byte[] { 1, 2, 3 };
        var store = Store();
        var first = await store.WriteAsync(
            "visuals/scene.png",
            bytes,
            TestContext.Current.CancellationToken);
        var lastWrite = File.GetLastWriteTimeUtc(first.AbsolutePath);

        var second = await store.WriteAsync(
            "visuals/scene.png",
            bytes,
            TestContext.Current.CancellationToken);

        Assert.Equal(first.ContentHash, second.ContentHash);
        Assert.Equal(lastWrite, File.GetLastWriteTimeUtc(second.AbsolutePath));
    }

    [Fact]
    public async Task WriteAsync_OverwritesChangedContent()
    {
        var store = Store();
        await store.WriteAsync("visuals/scene.png", [1, 2, 3], TestContext.Current.CancellationToken);

        var updated = await store.WriteAsync(
            "visuals/scene.png",
            [4, 5, 6, 7],
            TestContext.Current.CancellationToken);

        Assert.Equal(4, updated.ByteSize);
        Assert.Equal([4, 5, 6, 7], File.ReadAllBytes(updated.AbsolutePath));
    }

    [Theory]
    [InlineData("../outside/secret.png")]
    [InlineData("/etc/passwd")]
    [InlineData("C:\\Windows\\system32\\config\\sam")]
    public async Task WriteAsync_RejectsPathTraversalAndAbsolutePaths(string requestedPath)
    {
        var exception = await Assert.ThrowsAsync<AssetCollectionException>(
            () => Store().WriteAsync(
                requestedPath,
                [1],
                TestContext.Current.CancellationToken));

        Assert.Equal("asset_path_invalid", exception.ErrorCode);
    }

    [Fact]
    public async Task WriteAsync_RejectsEmptyContent()
    {
        var exception = await Assert.ThrowsAsync<AssetCollectionException>(
            () => Store().WriteAsync("visuals/scene.png", [], TestContext.Current.CancellationToken));

        Assert.Equal("asset_file_invalid", exception.ErrorCode);
    }

    private LocalAssetFileStore Store() =>
        new(Options.Create(new AssetStorageOptions { RootPath = root }));

    private void WriteAsset(string relativePath, byte[] bytes)
    {
        var fullPath = Path.Combine(root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllBytes(fullPath, bytes);
    }
}
