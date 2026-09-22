using AIStudio.Application.Stories;

namespace AIStudio.Application.IdentityAssets;

public static class IdentityAssetValidator
{
    public const int MaximumMediaTypeLength = 128;

    public static IReadOnlyList<IdentityAssetIssue> Validate(IdentityAsset asset)
    {
        ArgumentNullException.ThrowIfNull(asset);

        var issues = new List<IdentityAssetIssue>();

        if (!StoryIdentifier.IsValid(asset.Id.Value))
        {
            issues.Add(Issue(IdentityAssetIssueCodes.IdInvalid, "A valid identity asset id is required."));
        }

        if (!asset.Version.IsValid)
        {
            issues.Add(Issue(
                IdentityAssetIssueCodes.VersionInvalid,
                $"An identity asset version must be at least {IdentityAssetVersion.Minimum}."));
        }

        if (!StoryIdentifier.IsValid(asset.Kind))
        {
            issues.Add(Issue(
                IdentityAssetIssueCodes.KindInvalid,
                "Identity asset kind must be a lowercase token such as 'reference-image'."));
        }

        if (!IsValidMediaType(asset.MediaType))
        {
            issues.Add(Issue(
                IdentityAssetIssueCodes.MediaTypeInvalid,
                "Identity asset media type must be a bounded type/subtype value."));
        }

        if (asset.ByteSize <= 0)
        {
            issues.Add(Issue(IdentityAssetIssueCodes.ByteSizeInvalid, "Identity asset byte size must be positive."));
        }

        if (!IsSha256(asset.ContentHash))
        {
            issues.Add(Issue(
                IdentityAssetIssueCodes.ContentHashInvalid,
                "Identity asset content hash must be a lowercase 64-character SHA-256 value."));
        }

        if (!Enum.IsDefined(asset.Status))
        {
            issues.Add(Issue(IdentityAssetIssueCodes.StatusInvalid, "Identity asset status is invalid."));
        }

        if ((asset.Status == IdentityAssetStatus.Draft && asset.ApprovedAt is not null)
            || (asset.Status == IdentityAssetStatus.Approved && asset.ApprovedAt is null))
        {
            issues.Add(Issue(
                IdentityAssetIssueCodes.ApprovedAtInvalid,
                "ApprovedAt must be absent for Draft and present for Approved metadata."));
        }

        if (asset.CreatedAt == default)
        {
            issues.Add(Issue(IdentityAssetIssueCodes.CreatedAtInvalid, "Identity asset creation time is required."));
        }

        ValidateProvenance(asset.Provenance, issues);

        return issues;
    }

    internal static bool IsSha256(string? value) =>
        value is { Length: IdentityAsset.ContentHashLength }
        && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static bool IsValidMediaType(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value.Length > MaximumMediaTypeLength
            || value.Any(char.IsWhiteSpace))
        {
            return false;
        }

        var slash = value.IndexOf('/');
        return slash > 0
            && slash == value.LastIndexOf('/')
            && slash < value.Length - 1
            && value.All(character => char.IsAsciiLetterOrDigit(character)
                || character is '/' or '-' or '+' or '.' or '_');
    }

    private static void ValidateProvenance(
        IdentityAssetProvenance? provenance,
        List<IdentityAssetIssue> issues)
    {
        if (provenance is null)
        {
            issues.Add(Issue(IdentityAssetIssueCodes.ProvenanceInvalid, "Identity asset provenance is required."));
            return;
        }

        var hasBibleId = provenance.SourceBibleId is not null;
        var hasBibleVersion = provenance.SourceBibleVersion is not null;
        if (hasBibleId != hasBibleVersion
            || (hasBibleId && !StoryIdentifier.IsValid(provenance.SourceBibleId))
            || (hasBibleVersion && provenance.SourceBibleVersion < 1)
            || (provenance.CapabilityId is { } capability && string.IsNullOrEmpty(capability.Value))
            || (provenance.ProviderId is not null && !StoryIdentifier.IsValid(provenance.ProviderId))
            || provenance.Seed < 0
            || (provenance.PromptHash is not null && !IsSha256(provenance.PromptHash)))
        {
            issues.Add(Issue(
                IdentityAssetIssueCodes.ProvenanceInvalid,
                "Identity asset provenance identifiers, version, seed, or prompt hash are invalid."));
        }

        var parents = new HashSet<string>(StringComparer.Ordinal);
        foreach (var parent in provenance.ParentAssetIds ?? [])
        {
            if (!StoryIdentifier.IsValid(parent.Value))
            {
                issues.Add(Issue(IdentityAssetIssueCodes.ParentAssetInvalid, "Every parent asset id must be valid."));
            }
            else if (!parents.Add(parent.Value))
            {
                issues.Add(Issue(
                    IdentityAssetIssueCodes.ParentAssetDuplicate,
                    $"Parent asset '{parent}' is declared more than once."));
            }
        }
    }

    private static IdentityAssetIssue Issue(string code, string message) =>
        new() { Code = code, Message = message };
}
