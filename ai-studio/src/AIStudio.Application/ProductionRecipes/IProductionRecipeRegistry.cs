namespace AIStudio.Application.ProductionRecipes;

/// <summary>
/// In-memory catalog of known production recipes, keyed by stable id + version.
/// Storage stays configuration/in-memory for v1 (no database, cache, or file
/// watcher). Registration is trusted application data; recipe definitions can
/// never register executable providers.
/// </summary>
public interface IProductionRecipeRegistry
{
    /// <summary>All registered recipes, ordered deterministically by id then version.</summary>
    IReadOnlyList<ProductionRecipe> Recipes { get; }

    bool Contains(ProductionRecipeId id, ProductionRecipeVersion version);

    /// <summary>The exact registered version; throws when it is not registered.</summary>
    ProductionRecipe Get(ProductionRecipeId id, ProductionRecipeVersion version);

    /// <summary>The highest registered version for an id; throws when none is registered.</summary>
    ProductionRecipe GetLatest(ProductionRecipeId id);
}
