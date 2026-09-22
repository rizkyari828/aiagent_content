using AIStudio.Application.Bibles;

namespace AIStudio.Application.IdentityAssets;

/// <summary>
/// Explicit, human/operator approval of one already-imported Draft version. It
/// requires a concrete <see cref="IdentityAssetVersion"/>: there is deliberately no
/// "approve latest" path, so approval can never silently advance to a newer Draft.
/// </summary>
public sealed class ApproveIdentityAssetWorkflow(IIdentityAssetRegistry registry)
{
    public IdentityAsset Approve(AssetReferenceId assetId, IdentityAssetVersion version)
    {
        if (string.IsNullOrEmpty(assetId.Value))
        {
            throw new IdentityAssetAuthoringException(
                "identity_approval_invalid_id",
                "A valid asset reference id is required.");
        }

        if (!version.IsValid)
        {
            throw new IdentityAssetAuthoringException(
                "identity_approval_invalid_version",
                "A concrete positive identity asset version is required.");
        }

        return registry.Approve(assetId, version);
    }
}
