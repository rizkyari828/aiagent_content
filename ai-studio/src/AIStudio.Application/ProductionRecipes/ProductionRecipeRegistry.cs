namespace AIStudio.Application.ProductionRecipes;

/// <summary>
/// Deterministic in-memory recipe registry. Invalid or duplicate id/version
/// registrations fail fast, mirroring the capability registry so a bad catalog
/// cannot silently shadow a recipe. It never reads files or touches a database.
/// </summary>
public sealed class ProductionRecipeRegistry : IProductionRecipeRegistry
{
    private readonly IReadOnlyList<ProductionRecipe> _recipes;
    private readonly Dictionary<(ProductionRecipeId Id, ProductionRecipeVersion Version), ProductionRecipe> _byVersion;
    private readonly Dictionary<ProductionRecipeId, ProductionRecipe> _latest;

    public ProductionRecipeRegistry(IEnumerable<ProductionRecipe> recipes)
    {
        ArgumentNullException.ThrowIfNull(recipes);

        var ordered = recipes
            .OrderBy(recipe => recipe.Id.Value, StringComparer.Ordinal)
            .ThenBy(recipe => recipe.Version.Value)
            .ToList();

        _byVersion = [];
        _latest = [];

        foreach (var recipe in ordered)
        {
            var issues = ProductionRecipeValidator.Validate(recipe);
            if (issues.Count > 0)
            {
                throw new InvalidOperationException(
                    $"Production recipe '{recipe.Id}' v{recipe.Version.Value} is invalid: {issues[0].Code}.");
            }

            if (!_byVersion.TryAdd((recipe.Id, recipe.Version), recipe))
            {
                throw new InvalidOperationException(
                    $"Duplicate production recipe registration '{recipe.Id}' v{recipe.Version.Value}.");
            }

            if (!_latest.TryGetValue(recipe.Id, out var current)
                || recipe.Version.Value > current.Version.Value)
            {
                _latest[recipe.Id] = recipe;
            }
        }

        _recipes = ordered;
    }

    public IReadOnlyList<ProductionRecipe> Recipes => _recipes;

    public bool Contains(ProductionRecipeId id, ProductionRecipeVersion version) =>
        _byVersion.ContainsKey((id, version));

    public ProductionRecipe Get(ProductionRecipeId id, ProductionRecipeVersion version) =>
        _byVersion.TryGetValue((id, version), out var recipe)
            ? recipe
            : throw new KeyNotFoundException(
                $"Production recipe '{id}' v{version.Value} is not registered.");

    public ProductionRecipe GetLatest(ProductionRecipeId id) =>
        _latest.TryGetValue(id, out var recipe)
            ? recipe
            : throw new KeyNotFoundException($"Production recipe '{id}' is not registered.");
}
