using System.Text.Json.Serialization;
using AIStudio.Application.Bibles;

namespace AIStudio.Application.StoryContext;

/// <summary>
/// A compact projection of one referenced <see cref="WorldBible"/>: stable
/// environment identity plus recurring props, continuity rules, and named locations.
/// It is not a blind serialization of the bible, is engine-neutral, and exposes no
/// provider, path, or executable content.
/// </summary>
public sealed record StoryWorldContext
{
    [JsonPropertyName("id")]
    public WorldBibleId Id { get; init; }

    /// <summary>The deterministically resolved bible version (latest registered in v1).</summary>
    [JsonPropertyName("version")]
    public int Version { get; init; }

    [JsonPropertyName("displayName")]
    public string DisplayName { get; init; } = string.Empty;

    /// <summary>Stable environment identity, reused from the bible rather than duplicated.</summary>
    [JsonPropertyName("identity")]
    public WorldIdentity Identity { get; init; } = new();

    [JsonPropertyName("recurringProps")]
    public IReadOnlyList<string> RecurringProps { get; init; } = [];

    [JsonPropertyName("continuityRules")]
    public IReadOnlyList<string> ContinuityRules { get; init; } = [];

    [JsonPropertyName("locations")]
    public IReadOnlyList<WorldLocation> Locations { get; init; } = [];

    /// <summary>Safe asset id metadata, included only when explicitly requested.</summary>
    [JsonPropertyName("assetReferences")]
    public IReadOnlyList<StoryAssetContext> AssetReferences { get; init; } = [];
}
