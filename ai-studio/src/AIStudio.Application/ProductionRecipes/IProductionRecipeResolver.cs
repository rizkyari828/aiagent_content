namespace AIStudio.Application.ProductionRecipes;

/// <summary>
/// Resolves a production recipe against the capability registry into a
/// deterministic plan. It delegates each requirement to
/// <c>ICapabilityRegistry.Resolve</c> and never executes a provider.
/// </summary>
public interface IProductionRecipeResolver
{
    ProductionRecipeResolution Resolve(ProductionRecipe recipe);
}
