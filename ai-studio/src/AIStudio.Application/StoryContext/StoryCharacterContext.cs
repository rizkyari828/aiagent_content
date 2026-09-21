using System.Text.Json.Serialization;
using AIStudio.Application.Bibles;

namespace AIStudio.Application.StoryContext;

/// <summary>
/// A compact projection of one referenced <see cref="CharacterBible"/>: stable
/// identity plus the storytelling context a downstream consumer needs. It is not a
/// blind serialization of the bible — registry internals stay out, and relationships
/// to characters outside the current story are omitted. Engine-neutral: no provider,
/// path, or executable content.
/// </summary>
public sealed record StoryCharacterContext
{
    [JsonPropertyName("id")]
    public CharacterBibleId Id { get; init; }

    /// <summary>The deterministically resolved bible version (latest registered in v1).</summary>
    [JsonPropertyName("version")]
    public int Version { get; init; }

    [JsonPropertyName("displayName")]
    public string DisplayName { get; init; } = string.Empty;

    /// <summary>Stable identity, reused from the bible rather than duplicated.</summary>
    [JsonPropertyName("identity")]
    public CharacterIdentity Identity { get; init; } = new();

    [JsonPropertyName("personalityTraits")]
    public IReadOnlyList<string> PersonalityTraits { get; init; } = [];

    [JsonPropertyName("baselineVariant")]
    public string BaselineVariant { get; init; } = CharacterBible.DefaultVariant;

    /// <summary>Approved variant ids in authored order; variant detail is not duplicated.</summary>
    [JsonPropertyName("variantIds")]
    public IReadOnlyList<string> VariantIds { get; init; } = [];

    /// <summary>Only relationships whose target is also present in this context.</summary>
    [JsonPropertyName("relationships")]
    public IReadOnlyList<StoryRelationshipContext> Relationships { get; init; } = [];

    /// <summary>Safe asset id metadata, included only when explicitly requested.</summary>
    [JsonPropertyName("assetReferences")]
    public IReadOnlyList<StoryAssetContext> AssetReferences { get; init; } = [];
}

/// <summary>A filtered, data-driven relationship between two included characters.</summary>
public sealed record StoryRelationshipContext
{
    [JsonPropertyName("target")]
    public CharacterBibleId Target { get; init; }

    [JsonPropertyName("type")]
    public string Type { get; init; } = string.Empty;

    [JsonPropertyName("description")]
    public string? Description { get; init; }
}

/// <summary>
/// Safe, id-only asset metadata. It deliberately exposes no storage path, URL,
/// command, or provider workflow — a future Asset Registry resolves the id. Included
/// only when the caller opts in.
/// </summary>
public sealed record StoryAssetContext
{
    [JsonPropertyName("assetId")]
    public AssetReferenceId AssetId { get; init; }

    [JsonPropertyName("purpose")]
    public string Purpose { get; init; } = string.Empty;

    [JsonPropertyName("variant")]
    public string? Variant { get; init; }
}
