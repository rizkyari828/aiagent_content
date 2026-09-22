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
/// A production-safe result: the authored reference is copied with a concrete
/// version, so later execution does not re-resolve latest.
/// </summary>
public sealed record PinnedIdentityAsset
{
    public AssetReference Reference { get; init; } = new();

    public IdentityAsset Metadata { get; init; } = new();
}

public sealed record IdentityAssetResolution
{
    public PinnedIdentityAsset? Value { get; init; }

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
            Value = new PinnedIdentityAsset
            {
                Reference = reference with { Version = asset.Version },
                Metadata = asset
            }
        };
    }

    private static IdentityAssetResolution Failure(string code, string message) =>
        new()
        {
            Issues = [new IdentityAssetResolutionIssue { Code = code, Message = message }]
        };
}
