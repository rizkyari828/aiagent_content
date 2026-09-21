using System.Text.Json.Serialization;
using AIStudio.Application.ProductionRecipes;

namespace AIStudio.Application.Concepts;

/// <summary>
/// Declarative description of WHAT a concept is trying to make, such as a local-AI
/// anime short or a technical explainer. It references a trusted
/// <c>ProductionRecipe</c> that says HOW it is produced; it never copies recipe
/// capability requirements and never carries anything executable (type or assembly
/// names, paths, shell/Python/JS, model URLs, provider workflows, or secrets).
/// Format, style, audience and tags are data, so a new concept does not require a
/// new type, enum, handler, or workflow.
/// </summary>
public sealed record ConceptManifest
{
    [JsonPropertyName("id")]
    public ConceptId Id { get; init; }

    [JsonPropertyName("version")]
    public ConceptVersion Version { get; init; }

    [JsonPropertyName("title")]
    public string Title { get; init; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; init; } = string.Empty;

    /// <summary>Free-text audience descriptor, for example <c>developers</c>. Data only.</summary>
    [JsonPropertyName("audience")]
    public string Audience { get; init; } = string.Empty;

    /// <summary>Declarative format token, for example <c>youtube-short</c>.</summary>
    [JsonPropertyName("format")]
    public string Format { get; init; } = string.Empty;

    /// <summary>Declarative style token, for example <c>clean-tech</c>.</summary>
    [JsonPropertyName("style")]
    public string Style { get; init; } = string.Empty;

    /// <summary>Trusted recipe this concept requests. The recipe says HOW.</summary>
    [JsonPropertyName("recipeId")]
    public ProductionRecipeId RecipeId { get; init; }

    /// <summary>Requested recipe version; null means the latest registered version.</summary>
    [JsonPropertyName("recipeVersion")]
    public ProductionRecipeVersion? RecipeVersion { get; init; }

    /// <summary>Target duration in seconds.</summary>
    [JsonPropertyName("duration")]
    public int Duration { get; init; }

    /// <summary>Declarative tags; identifiers only, order-preserving and unique after validation.</summary>
    [JsonPropertyName("tags")]
    public IReadOnlyList<string> Tags { get; init; } = [];

    /// <summary>Structural validation; producibility is decided by the concept resolver.</summary>
    public IReadOnlyList<ConceptValidationIssue> Validate() => ConceptValidator.Validate(this);
}
