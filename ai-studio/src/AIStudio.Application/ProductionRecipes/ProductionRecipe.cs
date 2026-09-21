namespace AIStudio.Application.ProductionRecipes;

/// <summary>
/// Declarative description of what a kind of production requires ("what
/// capabilities does this content format need?"), for example a tech explainer or
/// a motion comic. It is pure data: identity is <see cref="Id"/> + <see cref="Version"/>,
/// and everything else is a list of capability requirements. A recipe can never
/// register, load, or execute a provider; trusted application code still decides
/// how a requested capability is implemented.
/// </summary>
public sealed record ProductionRecipe
{
    public ProductionRecipeId Id { get; init; }

    public ProductionRecipeVersion Version { get; init; }

    public string DisplayName { get; init; } = string.Empty;

    public IReadOnlyList<ProductionRecipeRequirement> Requirements { get; init; } = [];

    /// <summary>Structural validation; producibility is decided by the resolver.</summary>
    public IReadOnlyList<ProductionRecipeIssue> Validate() => ProductionRecipeValidator.Validate(this);
}
