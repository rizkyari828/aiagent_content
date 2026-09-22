using System.Diagnostics.CodeAnalysis;
using AIStudio.Application.Bibles;

namespace AIStudio.Application.IdentityAssets;

/// <summary>The single in-memory authority for identity-asset metadata and approval.</summary>
public interface IIdentityAssetRegistry
{
    IReadOnlyList<IdentityAsset> Assets { get; }

    IdentityAssetVersion NextVersion(AssetReferenceId id);

    void Register(IdentityAsset asset);

    IdentityAsset Approve(AssetReferenceId id, IdentityAssetVersion version);

    IdentityAsset Get(AssetReferenceId id, IdentityAssetVersion version);

    IdentityAsset GetLatest(AssetReferenceId id);

    IdentityAsset GetLatestApproved(AssetReferenceId id);

    bool TryGet(
        AssetReferenceId id,
        IdentityAssetVersion version,
        [NotNullWhen(true)] out IdentityAsset? asset);

    bool TryGetLatest(AssetReferenceId id, [NotNullWhen(true)] out IdentityAsset? asset);

    bool TryGetLatestApproved(AssetReferenceId id, [NotNullWhen(true)] out IdentityAsset? asset);
}
