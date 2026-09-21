using AIStudio.Application.ProductionRecipes;

namespace AIStudio.Application.Creative;

/// <summary>
/// Safe, planning-only summary of one registered recipe. It exposes only the
/// recipe id/version/display name, its high-level required capability ids, and
/// whether it currently resolves — never a provider class, executable path, model
/// path, script, or secret. ProductionRecipe stays authoritative for requirements.
/// </summary>
public sealed record CreativeRecipeOption
{
    public ProductionRecipeId Id { get; init; }

    public ProductionRecipeVersion Version { get; init; }

    public string DisplayName { get; init; } = string.Empty;

    /// <summary>Required capability ids, in declaration order.</summary>
    public IReadOnlyList<string> Capabilities { get; init; } = [];

    public bool Resolvable { get; init; }

    public bool UsesFallbacks { get; init; }
}

/// <summary>
/// Concise production awareness for the Creative Director: the safe recipe catalog
/// plus available/unavailable capability ids. This is planning context only; the
/// director selects creative intent or a recipe, never an implementation class.
/// </summary>
public sealed record CreativePlanningContext
{
    public IReadOnlyList<CreativeRecipeOption> Recipes { get; init; } = [];

    public IReadOnlyList<string> AvailableCapabilities { get; init; } = [];

    public IReadOnlyList<string> UnavailableCapabilities { get; init; } = [];
}

/// <summary>Builds the safe creative planning context from the trusted registries.</summary>
public interface ICreativePlanningContextProvider
{
    CreativePlanningContext Build();
}
