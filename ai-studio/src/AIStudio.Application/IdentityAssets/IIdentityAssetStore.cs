using AIStudio.Application.Bibles;

namespace AIStudio.Application.IdentityAssets;

/// <summary>Byte facts returned by the store; no path or storage key is exposed.</summary>
public sealed record IdentityAssetBlob(long ByteSize, string ContentHash);

/// <summary>
/// Byte authority keyed only by the stable application identity. Implementations
/// own physical mapping and never return it through this contract.
/// </summary>
public interface IIdentityAssetStore
{
    Task<IdentityAssetBlob> WriteAsync(
        AssetReferenceId id,
        IdentityAssetVersion version,
        ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken);

    Task<ReadOnlyMemory<byte>> ReadBytesAsync(
        AssetReferenceId id,
        IdentityAssetVersion version,
        CancellationToken cancellationToken);

    bool Exists(AssetReferenceId id, IdentityAssetVersion version);
}
