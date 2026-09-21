using System.Text.Json.Serialization;

namespace AIStudio.Application.Bibles;

/// <summary>
/// Stable character identity: WHAT should stay recognizable across every scene. It
/// is descriptive, engine-neutral data (role, species, proportions, hair, eyes,
/// distinguishing features, baseline outfit identity) that any engine — image,
/// image-to-video, 3D, or SVG — can interpret later. It never carries pose, action,
/// emotion, or location, which are mutable scene state, and never names code, a
/// provider, a path, or a real asset file. Trait and descriptor values are validated
/// data, so a human, robot, animal, or fantasy character needs no new C# subclass.
/// </summary>
public sealed record CharacterBible
{
    /// <summary>Variant id that is always implicitly approved as the base appearance.</summary>
    public const string DefaultVariant = "default";

    [JsonPropertyName("id")]
    public CharacterBibleId Id { get; init; }

    [JsonPropertyName("version")]
    public CharacterBibleVersion Version { get; init; }

    [JsonPropertyName("displayName")]
    public string DisplayName { get; init; } = string.Empty;

    [JsonPropertyName("identity")]
    public CharacterIdentity Identity { get; init; } = new();

    /// <summary>Data-driven personality descriptors, for example <c>curious</c>.</summary>
    [JsonPropertyName("personalityTraits")]
    public IReadOnlyList<string> PersonalityTraits { get; init; } = [];

    /// <summary>Approved base variant id; must be <c>default</c> or a declared variant.</summary>
    [JsonPropertyName("baselineVariant")]
    public string BaselineVariant { get; init; } = DefaultVariant;

    /// <summary>Approved appearance variants. A variant refines appearance, never identity.</summary>
    [JsonPropertyName("variants")]
    public IReadOnlyList<CharacterVariant> Variants { get; init; } = [];

    /// <summary>Lightweight data-driven relationships to other characters.</summary>
    [JsonPropertyName("relationships")]
    public IReadOnlyList<CharacterRelationship> Relationships { get; init; } = [];

    /// <summary>Engine-neutral references resolved later by a future Asset Registry.</summary>
    [JsonPropertyName("assetReferences")]
    public IReadOnlyList<AssetReference> AssetReferences { get; init; } = [];

    /// <summary>Structural validation; nothing is resolved or generated here.</summary>
    public IReadOnlyList<BibleIssue> Validate() => CharacterBibleValidator.Validate(this);

    /// <summary><c>default</c> is always an allowed variant; others must be declared.</summary>
    public bool IsVariantAllowed(string? variantId) =>
        variantId == DefaultVariant
        || (variantId is not null && Variants.Any(variant => variant.Id == variantId));

    public bool HasVariant(string variantId) =>
        Variants.Any(variant => variant.Id == variantId);
}

/// <summary>
/// Stable physical identity of a character. Every field is descriptive data, so the
/// same shape describes a human protagonist, a robot mascot, or a fantasy creature.
/// </summary>
public sealed record CharacterIdentity
{
    /// <summary>Data-driven narrative role, for example <c>protagonist</c>.</summary>
    [JsonPropertyName("role")]
    public string Role { get; init; } = string.Empty;

    /// <summary>Data-driven species/kind, for example <c>human</c>, <c>robot</c>, <c>cat</c>.</summary>
    [JsonPropertyName("species")]
    public string? Species { get; init; }

    [JsonPropertyName("agePresentation")]
    public string? AgePresentation { get; init; }

    [JsonPropertyName("bodyStyle")]
    public string? BodyStyle { get; init; }

    [JsonPropertyName("hair")]
    public string? Hair { get; init; }

    [JsonPropertyName("eyes")]
    public string? Eyes { get; init; }

    /// <summary>Free-text baseline visual description (1..2000 characters).</summary>
    [JsonPropertyName("visualDescription")]
    public string? VisualDescription { get; init; }

    /// <summary>Data-driven distinguishing features, for example <c>scar-left-brow</c>.</summary>
    [JsonPropertyName("distinguishingTraits")]
    public IReadOnlyList<string> DistinguishingTraits { get; init; } = [];
}

/// <summary>
/// An approved appearance variant of the same character (for example
/// <c>winter-jacket</c>). A variant is NOT a new character: it changes appearance
/// while the character identity stays authoritative. This is a contract only, not a
/// wardrobe system.
/// </summary>
public sealed record CharacterVariant
{
    /// <summary>Data-driven variant id, for example <c>school-uniform</c>.</summary>
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("displayName")]
    public string? DisplayName { get; init; }

    [JsonPropertyName("description")]
    public string? Description { get; init; }
}

/// <summary>
/// A lightweight, data-driven relationship to another character. The relationship
/// type is data (for example <c>sibling</c> or <c>mentor</c>), so a new relationship
/// kind never requires an enum or code change.
/// </summary>
public sealed record CharacterRelationship
{
    [JsonPropertyName("target")]
    public CharacterBibleId Target { get; init; }

    /// <summary>Data-driven relationship type, for example <c>sibling</c>.</summary>
    [JsonPropertyName("type")]
    public string Type { get; init; } = string.Empty;

    [JsonPropertyName("description")]
    public string? Description { get; init; }
}

/// <summary>
/// Mutable, scene-level character state. It references a stable
/// <see cref="CharacterBible"/> by id and may legitimately change per scene
/// (emotion, pose, action, location, approved variant, held props) without
/// affecting identity. State is deliberately NOT part of <see cref="CharacterBible"/>.
/// </summary>
public sealed record CharacterState
{
    [JsonPropertyName("characterRef")]
    public CharacterBibleId CharacterRef { get; init; }

    /// <summary>Approved appearance variant in effect; <c>default</c> unless changed.</summary>
    [JsonPropertyName("variant")]
    public string Variant { get; init; } = CharacterBible.DefaultVariant;

    [JsonPropertyName("emotion")]
    public string? Emotion { get; init; }

    [JsonPropertyName("pose")]
    public string? Pose { get; init; }

    [JsonPropertyName("action")]
    public string? Action { get; init; }

    [JsonPropertyName("worldRef")]
    public WorldBibleId? WorldRef { get; init; }

    [JsonPropertyName("heldProps")]
    public IReadOnlyList<string> HeldProps { get; init; } = [];

    public IReadOnlyList<BibleIssue> Validate() => CharacterBibleValidator.Validate(this);

    /// <summary>Validates this state against its character bible, including variant approval.</summary>
    public IReadOnlyList<BibleIssue> Validate(CharacterBible bible) =>
        CharacterBibleValidator.Validate(this, bible);
}
