using AIStudio.Application.Capabilities;
using AIStudio.Application.ProductionRecipes;

namespace AIStudio.Application.Creative;

/// <summary>
/// Deterministic, read-only projection of the trusted recipe and capability
/// registries into the safe summary the Creative Director may see. It executes no
/// provider and exposes no implementation detail.
/// </summary>
public sealed class CreativePlanningContextProvider : ICreativePlanningContextProvider
{
    private readonly IProductionRecipeRegistry _recipes;
    private readonly IProductionRecipeResolver _recipeResolver;
    private readonly ICapabilityRegistry _capabilities;

    public CreativePlanningContextProvider(
        IProductionRecipeRegistry recipes,
        IProductionRecipeResolver recipeResolver,
        ICapabilityRegistry capabilities)
    {
        _recipes = recipes ?? throw new ArgumentNullException(nameof(recipes));
        _recipeResolver = recipeResolver ?? throw new ArgumentNullException(nameof(recipeResolver));
        _capabilities = capabilities ?? throw new ArgumentNullException(nameof(capabilities));
    }

    public CreativePlanningContext Build()
    {
        var recipes = _recipes.Recipes
            .Select(BuildOption)
            .ToList();

        var available = new List<string>();
        var unavailable = new List<string>();

        foreach (var descriptor in _capabilities.Capabilities)
        {
            if (_capabilities.GetAvailableProviders(descriptor.Id).Count > 0)
            {
                available.Add(descriptor.Id.Value);
            }
            else
            {
                unavailable.Add(descriptor.Id.Value);
            }
        }

        return new CreativePlanningContext
        {
            Recipes = recipes,
            AvailableCapabilities = available,
            UnavailableCapabilities = unavailable
        };
    }

    private CreativeRecipeOption BuildOption(ProductionRecipe recipe)
    {
        var resolution = _recipeResolver.Resolve(recipe);

        return new CreativeRecipeOption
        {
            Id = recipe.Id,
            Version = recipe.Version,
            DisplayName = recipe.DisplayName,
            Capabilities = recipe.Requirements
                .Where(requirement => requirement.IsRequired)
                .Select(requirement => requirement.Capability.CapabilityId.Value)
                .Distinct(StringComparer.Ordinal)
                .ToList(),
            Resolvable = resolution.IsProducible,
            UsesFallbacks = resolution.Requirements.Any(
                requirement => requirement.Capability.UsedFallback)
        };
    }
}
