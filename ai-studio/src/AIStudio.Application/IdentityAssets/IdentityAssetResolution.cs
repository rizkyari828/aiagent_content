using AIStudio.Application.Bibles;

namespace AIStudio.Application.IdentityAssets;

public sealed record IdentityAssetResolutionIssue
{
    public string Code { get; init; } = string.Empty;

    public string Message { get; init; } = string.Empty;
}

public static class IdentityAssetResolutionIssueCodes
{
    public const string NotFound = "identity_asset_not_found";
    public const string NotApproved = "identity_asset_not_approved";
}

/// <summary>
/// The provider-safe, concrete identity-asset key produced at materialization.
/// It deliberately excludes authoring purpose, metadata, paths, and provider data.
/// </summary>
public sealed record PinnedIdentityAsset
{
    public PinnedIdentityAsset(AssetReferenceId assetId, IdentityAssetVersion version)
    {
        if (string.IsNullOrEmpty(assetId.Value))
        {
            throw new ArgumentException("A valid identity asset id is required.", nameof(assetId));
        }

        if (!version.IsValid)
        {
            throw new ArgumentOutOfRangeException(
                nameof(version),
                "A positive identity asset version is required.");
        }

        AssetId = assetId;
        Version = version;
    }

    public AssetReferenceId AssetId { get; }

    public IdentityAssetVersion Version { get; }
}

public sealed record IdentityAssetResolution
{
    public PinnedIdentityAsset? Value { get; init; }

    public AssetReference? MaterializedReference { get; init; }

    public IdentityAsset? Metadata { get; init; }

    public IReadOnlyList<IdentityAssetResolutionIssue> Issues { get; init; } = [];

    public bool IsSuccess => Value is not null && Issues.Count == 0;
}

public interface IIdentityAssetResolver
{
    IdentityAssetResolution Resolve(AssetReference reference);
}

public sealed class IdentityAssetResolver(IIdentityAssetRegistry registry) : IIdentityAssetResolver
{
    private readonly IIdentityAssetRegistry _registry =
        registry ?? throw new ArgumentNullException(nameof(registry));

    public IdentityAssetResolution Resolve(AssetReference reference)
    {
        ArgumentNullException.ThrowIfNull(reference);

        IdentityAsset? asset;

        if (reference.Version is { } explicitVersion)
        {
            if (!_registry.TryGet(reference.AssetId, explicitVersion, out asset))
            {
                return Failure(
                    IdentityAssetResolutionIssueCodes.NotFound,
                    $"Identity asset '{reference.AssetId}' v{explicitVersion.Value} was not found.");
            }

            if (asset.Status != IdentityAssetStatus.Approved)
            {
                return Failure(
                    IdentityAssetResolutionIssueCodes.NotApproved,
                    $"Identity asset '{reference.AssetId}' v{explicitVersion.Value} is not approved.");
            }
        }
        else if (!_registry.TryGetLatestApproved(reference.AssetId, out asset))
        {
            return _registry.TryGetLatest(reference.AssetId, out _)
                ? Failure(
                    IdentityAssetResolutionIssueCodes.NotApproved,
                    $"Identity asset '{reference.AssetId}' has no approved version.")
                : Failure(
                    IdentityAssetResolutionIssueCodes.NotFound,
                    $"Identity asset '{reference.AssetId}' was not found.");
        }

        return new IdentityAssetResolution
        {
            Value = new PinnedIdentityAsset(asset.Id, asset.Version),
            MaterializedReference = reference with { Version = asset.Version },
            Metadata = asset
        };
    }

    private static IdentityAssetResolution Failure(string code, string message) =>
        new()
        {
            Issues = [new IdentityAssetResolutionIssue { Code = code, Message = message }]
        };
}
