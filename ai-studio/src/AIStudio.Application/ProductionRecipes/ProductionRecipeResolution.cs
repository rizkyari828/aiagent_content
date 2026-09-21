using AIStudio.Application.Capabilities;

namespace AIStudio.Application.ProductionRecipes;

/// <summary>
/// Whether a recipe can currently be produced with the registered providers.
/// Planning only: this never executes a provider.
/// </summary>
public enum ProductionRecipeStatus
{
    /// <summary>Every requirement resolved on its primary capability.</summary>
    FullySupported = 0,

    /// <summary>Every required capability resolved, but at least one used a fallback.</summary>
    SupportedWithFallbacks = 1,

    /// <summary>At least one required capability has no usable primary or fallback provider.</summary>
    Unsupported = 2
}

/// <summary>Resolution of one recipe requirement, inheriting the capability registry result.</summary>
public sealed record ProductionRecipeRequirementResolution
{
    public ProductionRecipeRequirement Requirement { get; init; } = new();

    public CapabilityResolution Capability { get; init; } = new();

    public bool IsSatisfied => Capability?.IsResolved ?? false;
}

/// <summary>
/// Deterministic, execution-free plan for a recipe: which provider would serve
/// each requirement, which requirements needed a fallback, and which capabilities
/// are gaps. The current production pipeline does not consume this yet.
/// </summary>
public sealed record ProductionRecipeResolution
{
    public ProductionRecipeId RecipeId { get; init; }

    public ProductionRecipeVersion RecipeVersion { get; init; }

    public ProductionRecipeStatus Status { get; init; }

    public IReadOnlyList<ProductionRecipeRequirementResolution> Requirements { get; init; } = [];

    /// <summary>Gaps for every unsatisfied requirement (required and optional).</summary>
    public IReadOnlyList<CapabilityGap> Gaps { get; init; } = [];

    public bool IsProducible => Status != ProductionRecipeStatus.Unsupported;
}
