using AIStudio.Application.Capabilities;

namespace AIStudio.Application.ProductionRecipes;

/// <summary>
/// Deterministic recipe resolver. It does not re-implement capability resolution:
/// each requirement (including its fallback chain) is delegated to
/// <see cref="ICapabilityRegistry.Resolve"/>, so provider selection, fallback
/// ordering, and gap reasons stay defined in one place. Resolution is pure
/// metadata planning and never loads or runs a provider.
/// </summary>
public sealed class ProductionRecipeResolver : IProductionRecipeResolver
{
    private readonly ICapabilityRegistry _capabilities;

    public ProductionRecipeResolver(ICapabilityRegistry capabilities)
    {
        _capabilities = capabilities ?? throw new ArgumentNullException(nameof(capabilities));
    }

    public ProductionRecipeResolution Resolve(ProductionRecipe recipe)
    {
        ArgumentNullException.ThrowIfNull(recipe);

        var requirements = new List<ProductionRecipeRequirementResolution>(recipe.Requirements.Count);
        var gaps = new List<CapabilityGap>();
        var unsupported = false;
        var usedFallback = false;

        foreach (var requirement in recipe.Requirements)
        {
            var capabilityResolution = _capabilities.Resolve(requirement.Capability);

            requirements.Add(new ProductionRecipeRequirementResolution
            {
                Requirement = requirement,
                Capability = capabilityResolution
            });

            if (capabilityResolution.IsResolved)
            {
                usedFallback |= capabilityResolution.UsedFallback;
                continue;
            }

            if (capabilityResolution.Gap is not null)
            {
                gaps.Add(capabilityResolution.Gap);
            }

            unsupported |= requirement.IsRequired;
        }

        var status = unsupported
            ? ProductionRecipeStatus.Unsupported
            : usedFallback
                ? ProductionRecipeStatus.SupportedWithFallbacks
                : ProductionRecipeStatus.FullySupported;

        return new ProductionRecipeResolution
        {
            RecipeId = recipe.Id,
            RecipeVersion = recipe.Version,
            Status = status,
            Requirements = requirements,
            Gaps = gaps
        };
    }
}
