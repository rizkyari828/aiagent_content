using System.Text.Json.Serialization;
using AIStudio.Application.IdentityAssets;

namespace AIStudio.Application.Bibles;

/// <summary>
/// An engine-neutral pointer to a reusable asset the bible may use. It carries an
/// <see cref="AssetReferenceId"/> plus a data-driven purpose and optional variant —
/// never a filesystem path, URL, command, provider workflow, or secret. A future
/// Asset Registry resolves the id to actual storage; nothing here is loaded.
/// </summary>
public sealed record AssetReference
{
    [JsonPropertyName("assetId")]
    public AssetReferenceId AssetId { get; init; }

    /// <summary>
    /// Optional authored pin. A missing value floats only until a production
    /// materialization boundary resolves and records a concrete approved version.
    /// </summary>
    [JsonPropertyName("version")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IdentityAssetVersion? Version { get; init; }

    /// <summary>Data-driven purpose, for example <c>visual-reference</c> or <c>voice-reference</c>.</summary>
    [JsonPropertyName("purpose")]
    public string Purpose { get; init; } = string.Empty;

    /// <summary>Optional variant this asset represents, for example <c>winter-jacket</c>.</summary>
    [JsonPropertyName("variant")]
    public string? Variant { get; init; }
}
