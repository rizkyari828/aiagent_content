using System.Security.Cryptography;
using AIStudio.Application.Assets;
using AIStudio.Application.Bibles;
using AIStudio.Application.IdentityAssets;
using AIStudio.Application.Stories;

namespace AIStudio.Infrastructure.Assets;

/// <summary>
/// Local byte store that keeps its deterministic relative key and physical path
/// private. All path containment, symlink, and atomic-write guarantees are
/// delegated to the existing <see cref="IAssetFileStore"/>.
/// </summary>
public sealed class LocalIdentityAssetStore(IAssetFileStore fileStore) : IIdentityAssetStore
{
    private readonly IAssetFileStore _fileStore =
        fileStore ?? throw new ArgumentNullException(nameof(fileStore));
    private readonly SemaphoreSlim _writeGate = new(1, 1);

    public async Task<IdentityAssetBlob> WriteAsync(
        AssetReferenceId id,
        IdentityAssetVersion version,
        ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken)
    {
        if (content.IsEmpty)
        {
            throw new AssetCollectionException(
                "asset_file_invalid",
                "Identity asset content is empty.");
        }

        var relativeKey = RelativeKey(id, version);
        var incomingHash = Convert
            .ToHexString(SHA256.HashData(content.Span))
            .ToLowerInvariant();

        await _writeGate.WaitAsync(cancellationToken);
        try
        {
            AssetFileInfo? existing = null;
            try
            {
                existing = _fileStore.Register(relativeKey);
            }
            catch (AssetCollectionException exception)
                when (exception.ErrorCode == "asset_file_not_found")
            {
            }

            if (existing is not null)
            {
                if (string.Equals(existing.ContentHash, incomingHash, StringComparison.Ordinal))
                {
                    return new IdentityAssetBlob(existing.ByteSize, existing.ContentHash);
                }

                throw new AssetCollectionException(
                    "identity_asset_content_conflict",
                    $"Identity asset '{id}' v{version.Value} already contains different bytes; allocate a new version.");
            }

            var info = await _fileStore.WriteAsync(
                relativeKey,
                content.ToArray(),
                cancellationToken);

            return new IdentityAssetBlob(info.ByteSize, info.ContentHash);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    public async Task<ReadOnlyMemory<byte>> ReadBytesAsync(
        AssetReferenceId id,
        IdentityAssetVersion version,
        CancellationToken cancellationToken)
    {
        var info = _fileStore.Register(RelativeKey(id, version));
        return await File.ReadAllBytesAsync(info.AbsolutePath, cancellationToken);
    }

    public bool Exists(AssetReferenceId id, IdentityAssetVersion version)
    {
        try
        {
            _fileStore.Register(RelativeKey(id, version));
            return true;
        }
        catch (AssetCollectionException exception)
            when (exception.ErrorCode == "asset_file_not_found")
        {
            return false;
        }
    }

    private static string RelativeKey(AssetReferenceId id, IdentityAssetVersion version)
    {
        if (!StoryIdentifier.IsValid(id.Value))
        {
            throw new ArgumentException("A valid identity asset id is required.", nameof(id));
        }

        if (!version.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(version), "A positive identity asset version is required.");
        }

        return $"identity-assets/{id.Value}/v{version.Value}.blob";
    }
}
