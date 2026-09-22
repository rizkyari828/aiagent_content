namespace AIStudio.Application.IdentityAssets;

public sealed record IdentityAssetIssue
{
    public string Code { get; init; } = string.Empty;

    public string Message { get; init; } = string.Empty;
}

public static class IdentityAssetIssueCodes
{
    public const string IdInvalid = "identity_asset_id_invalid";
    public const string VersionInvalid = "identity_asset_version_invalid";
    public const string KindInvalid = "identity_asset_kind_invalid";
    public const string MediaTypeInvalid = "identity_asset_media_type_invalid";
    public const string ByteSizeInvalid = "identity_asset_byte_size_invalid";
    public const string ContentHashInvalid = "identity_asset_content_hash_invalid";
    public const string StatusInvalid = "identity_asset_status_invalid";
    public const string ApprovedAtInvalid = "identity_asset_approved_at_invalid";
    public const string CreatedAtInvalid = "identity_asset_created_at_invalid";
    public const string ProvenanceInvalid = "identity_asset_provenance_invalid";
    public const string ParentAssetInvalid = "identity_asset_parent_invalid";
    public const string ParentAssetDuplicate = "identity_asset_parent_duplicate";
}
