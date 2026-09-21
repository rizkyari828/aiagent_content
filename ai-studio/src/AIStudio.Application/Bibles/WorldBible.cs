using System.Text.Json.Serialization;

namespace AIStudio.Application.Bibles;

/// <summary>
/// Stable world/environment identity: WHAT should stay recognizable across every
/// scene (recurring layout, landmarks, core props, visual identity, environmental
/// rules). It is engine-neutral descriptive data usable later by image,
/// image-to-video, 3D, or SVG production, and never names a renderer, provider,
/// path, or real asset file. Time of day, weather, lighting, temporary props, and
/// damage are mutable <see cref="WorldState"/>, not identity.
/// </summary>
public sealed record WorldBible
{
    [JsonPropertyName("id")]
    public WorldBibleId Id { get; init; }

    [JsonPropertyName("version")]
    public WorldBibleVersion Version { get; init; }

    [JsonPropertyName("displayName")]
    public string DisplayName { get; init; } = string.Empty;

    [JsonPropertyName("identity")]
    public WorldIdentity Identity { get; init; } = new();

    /// <summary>Recurring prop identifiers, for example <c>desk</c> or <c>gaming-pc</c>.</summary>
    [JsonPropertyName("recurringProps")]
    public IReadOnlyList<string> RecurringProps { get; init; } = [];

    /// <summary>Free-text continuity rules, for example "desk remains beside window".</summary>
    [JsonPropertyName("continuityRules")]
    public IReadOnlyList<string> ContinuityRules { get; init; } = [];

    /// <summary>Optional named sub-locations of this world.</summary>
    [JsonPropertyName("locations")]
    public IReadOnlyList<WorldLocation> Locations { get; init; } = [];

    /// <summary>Engine-neutral references resolved later by a future Asset Registry.</summary>
    [JsonPropertyName("assetReferences")]
    public IReadOnlyList<AssetReference> AssetReferences { get; init; } = [];

    /// <summary>Structural validation; nothing is resolved or rendered here.</summary>
    public IReadOnlyList<BibleIssue> Validate() => WorldBibleValidator.Validate(this);

    public bool HasLocation(string? locationId) =>
        locationId is not null && Locations.Any(location => location.Id == locationId);
}

/// <summary>Stable visual and spatial identity of a world.</summary>
public sealed record WorldIdentity
{
    /// <summary>Data-driven environment type, for example <c>bedroom</c> or <c>spaceship</c>.</summary>
    [JsonPropertyName("environmentType")]
    public string EnvironmentType { get; init; } = string.Empty;

    /// <summary>Free-text baseline visual description (1..2000 characters).</summary>
    [JsonPropertyName("visualDescription")]
    public string VisualDescription { get; init; } = string.Empty;

    /// <summary>Data-driven spatial traits, for example <c>single-window</c>.</summary>
    [JsonPropertyName("spatialTraits")]
    public IReadOnlyList<string> SpatialTraits { get; init; } = [];
}

/// <summary>
/// An optional named sub-location inside a world, for example a <c>classroom</c>
/// within a <c>school</c>. Kept deliberately simple: a contract, not a scene graph.
/// </summary>
public sealed record WorldLocation
{
    /// <summary>Data-driven location id, for example <c>classroom</c>.</summary>
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("displayName")]
    public string? DisplayName { get; init; }

    [JsonPropertyName("description")]
    public string? Description { get; init; }
}

/// <summary>
/// Mutable, scene-level world state. It references a stable
/// <see cref="WorldBible"/> by id and may legitimately change per scene (time of
/// day, weather, lighting, temporary props, notes) without affecting world identity.
/// </summary>
public sealed record WorldState
{
    [JsonPropertyName("worldRef")]
    public WorldBibleId WorldRef { get; init; }

    [JsonPropertyName("timeOfDay")]
    public string? TimeOfDay { get; init; }

    [JsonPropertyName("weather")]
    public string? Weather { get; init; }

    [JsonPropertyName("lighting")]
    public string? Lighting { get; init; }

    [JsonPropertyName("temporaryProps")]
    public IReadOnlyList<string> TemporaryProps { get; init; } = [];

    [JsonPropertyName("notes")]
    public string? Notes { get; init; }

    public IReadOnlyList<BibleIssue> Validate() => WorldBibleValidator.Validate(this);
}
