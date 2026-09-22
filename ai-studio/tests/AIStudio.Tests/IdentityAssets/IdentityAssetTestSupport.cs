using AIStudio.Application.Bibles;
using AIStudio.Application.IdentityAssets;

namespace AIStudio.Tests.IdentityAssets;

internal static class IdentityAssetTestSupport
{
    public static readonly DateTimeOffset CreatedAt =
        new(2026, 9, 22, 1, 0, 0, TimeSpan.Zero);

    public static IdentityAsset Asset(
        string id = "student-ref",
        int version = 1,
        string kind = "reference-image",
        string mediaType = "image/png",
        IReadOnlyList<AssetReferenceId>? parents = null) =>
        new()
        {
            Id = new AssetReferenceId(id),
            Version = new IdentityAssetVersion(version),
            Kind = kind,
            MediaType = mediaType,
            ByteSize = 4,
            ContentHash = new string('a', IdentityAsset.ContentHashLength),
            Status = IdentityAssetStatus.Draft,
            Provenance = new IdentityAssetProvenance
            {
                SourceBibleId = "student",
                SourceBibleVersion = 1,
                ProviderId = "comfyui-flux",
                Seed = 42,
                PromptHash = new string('b', IdentityAsset.ContentHashLength),
                ParentAssetIds = parents ?? []
            },
            CreatedAt = CreatedAt
        };

    public static AssetReference Reference(
        int? version = null,
        string id = "student-ref") =>
        new()
        {
            AssetId = new AssetReferenceId(id),
            Version = version is null ? null : new IdentityAssetVersion(version.Value),
            Purpose = "character-primary-reference",
            Variant = "default"
        };
}

internal sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => utcNow;
}
