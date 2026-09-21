using AIStudio.Application.ProductionRecipes;

namespace AIStudio.Application.Concepts;

/// <summary>
/// Deterministic concept resolver. It maps a concept to its trusted recipe (exact
/// version, or the latest registered version when none is requested) and then
/// delegates entirely to <see cref="IProductionRecipeResolver"/>, so capability
/// selection, fallback ordering, and gaps stay defined in one place. A missing
/// recipe is reported as <see cref="ConceptResolutionIssueCodes.RecipeNotFound"/>,
/// distinct from a known recipe whose required capability is unavailable.
/// </summary>
public sealed class ConceptResolver : IConceptResolver
{
    private readonly IProductionRecipeRegistry _recipes;
    private readonly IProductionRecipeResolver _recipeResolver;

    public ConceptResolver(
        IProductionRecipeRegistry recipes,
        IProductionRecipeResolver recipeResolver)
    {
        _recipes = recipes ?? throw new ArgumentNullException(nameof(recipes));
        _recipeResolver = recipeResolver ?? throw new ArgumentNullException(nameof(recipeResolver));
    }

    public ConceptResolution Resolve(ConceptManifest concept)
    {
        ArgumentNullException.ThrowIfNull(concept);

        ProductionRecipe? recipe;
        if (concept.RecipeVersion is { } requestedVersion)
        {
            _recipes.TryGet(concept.RecipeId, requestedVersion, out recipe);
        }
        else
        {
            _recipes.TryGetLatest(concept.RecipeId, out recipe);
        }

        if (recipe is null)
        {
            return new ConceptResolution
            {
                Id = concept.Id,
                Version = concept.Version,
                RecipeId = concept.RecipeId,
                RecipeVersion = concept.RecipeVersion,
                Status = ConceptResolutionStatus.Unsupported,
                Issues =
                [
                    new ConceptResolutionIssue
                    {
                        Code = ConceptResolutionIssueCodes.RecipeNotFound,
                        Message = $"Production recipe '{concept.RecipeId}' is not registered."
                    }
                ]
            };
        }

        var recipeResolution = _recipeResolver.Resolve(recipe);
        var status = recipeResolution.Status switch
        {
            ProductionRecipeStatus.FullySupported => ConceptResolutionStatus.Ready,
            ProductionRecipeStatus.SupportedWithFallbacks => ConceptResolutionStatus.ReadyWithFallbacks,
            _ => ConceptResolutionStatus.Unsupported
        };

        var issues = new List<ConceptResolutionIssue>();
        if (status == ConceptResolutionStatus.Unsupported)
        {
            issues.Add(new ConceptResolutionIssue
            {
                Code = ConceptResolutionIssueCodes.RecipeUnsupported,
                Message = $"Production recipe '{recipe.Id}' v{recipe.Version.Value} has unresolved required capabilities."
            });
        }

        return new ConceptResolution
        {
            Id = concept.Id,
            Version = concept.Version,
            RecipeId = recipe.Id,
            RecipeVersion = recipe.Version,
            Status = status,
            RecipeResolution = recipeResolution,
            Issues = issues
        };
    }
}
