using AIStudio.Application.Bibles;
using AIStudio.Application.Capabilities;

namespace AIStudio.Application.IdentityAssets;

public enum IdentityAssetStatus
{
    Draft = 0,
    Approved = 1
}

/// <summary>
/// Small, identifier-only production provenance. It deliberately carries no full
/// prompt, path, provider configuration, workflow, checkpoint, or lineage graph.
/// </summary>
public sealed record IdentityAssetProvenance
{
    public string? SourceBibleId { get; init; }

    public int? SourceBibleVersion { get; init; }

    public CapabilityId? CapabilityId { get; init; }

    public string? ProviderId { get; init; }

    public long? Seed { get; init; }

    public string? PromptHash { get; init; }

    public IReadOnlyList<AssetReferenceId> ParentAssetIds { get; init; } = [];
}

/// <summary>
/// Application-owned identity-asset metadata. Bytes and physical storage details
/// belong exclusively to <see cref="IIdentityAssetStore"/>.
/// </summary>
public sealed record IdentityAsset
{
    public const int ContentHashLength = 64;

    public AssetReferenceId Id { get; init; }

    public IdentityAssetVersion Version { get; init; }

    public string Kind { get; init; } = string.Empty;

    public string MediaType { get; init; } = string.Empty;

    public long ByteSize { get; init; }

    public string ContentHash { get; init; } = string.Empty;

    public IdentityAssetStatus Status { get; init; }

    public DateTimeOffset? ApprovedAt { get; init; }

    public IdentityAssetProvenance Provenance { get; init; } = new();

    public DateTimeOffset CreatedAt { get; init; }
}
