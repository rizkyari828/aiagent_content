using AIStudio.Application.Capabilities;
using AIStudio.Application.ProductionRecipes;

namespace AIStudio.Application.Concepts;

/// <summary>
/// Whether a valid concept can currently be produced, given its recipe and the
/// registered capabilities. Planning only: this never executes a provider.
/// </summary>
public enum ConceptResolutionStatus
{
    /// <summary>Recipe found and every required primary capability resolves.</summary>
    Ready = 0,

    /// <summary>Recipe found and production is possible, but at least one fallback is used.</summary>
    ReadyWithFallbacks = 1,

    /// <summary>The recipe is missing, or a required capability has no usable provider.</summary>
    Unsupported = 2
}

/// <summary>
/// A resolution-level problem, kept distinct from concept validation so callers can
/// tell "unknown recipe" apart from "known recipe, unavailable capability".
/// </summary>
public sealed record ConceptResolutionIssue
{
    public string Code { get; init; } = string.Empty;

    public string Message { get; init; } = string.Empty;
}

/// <summary>Stable resolution codes, safe to surface to future planners.</summary>
public static class ConceptResolutionIssueCodes
{
    public const string RecipeNotFound = "recipe_not_found";
    public const string RecipeUnsupported = "recipe_unsupported";
}

/// <summary>
/// Execution-free plan for a concept: which recipe version was chosen, whether it
/// is producible, and the underlying recipe/capability resolution. The current
/// production pipeline does not consume this yet.
/// </summary>
public sealed record ConceptResolution
{
    public ConceptId Id { get; init; }

    public ConceptVersion Version { get; init; }

    public ProductionRecipeId RecipeId { get; init; }

    /// <summary>The requested recipe version, or the deterministically chosen latest version.</summary>
    public ProductionRecipeVersion? RecipeVersion { get; init; }

    public ConceptResolutionStatus Status { get; init; }

    /// <summary>Underlying recipe resolution; null when the recipe is not registered.</summary>
    public ProductionRecipeResolution? RecipeResolution { get; init; }

    public IReadOnlyList<ConceptResolutionIssue> Issues { get; init; } = [];

    /// <summary>Capability gaps inherited from the recipe resolution (empty when the recipe is missing).</summary>
    public IReadOnlyList<CapabilityGap> Gaps => RecipeResolution?.Gaps ?? [];

    public bool IsProducible => Status != ConceptResolutionStatus.Unsupported;
}
