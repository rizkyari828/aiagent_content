using AIStudio.Application.Capabilities;

namespace AIStudio.Application.ProductionRecipes;

/// <summary>
/// Narrow, deterministic structural validation for a production recipe. It checks
/// only the recipe's own data: identity, non-empty requirement set, at least one
/// required requirement, and per-requirement capability duplication / fallback
/// hygiene. It never consults the capability registry, so a recipe that requests a
/// valid but not-yet-implemented capability is still valid data.
/// </summary>
public static class ProductionRecipeValidator
{
    public static IReadOnlyList<ProductionRecipeIssue> Validate(ProductionRecipe recipe)
    {
        ArgumentNullException.ThrowIfNull(recipe);

        var issues = new List<ProductionRecipeIssue>();

        if (!ProductionRecipeId.IsValid(recipe.Id.Value))
        {
            issues.Add(Issue(
                ProductionRecipeIssueCodes.RecipeIdInvalid,
                "A production recipe id is required and must be a lowercase identifier such as 'tech-explainer'."));
        }

        if (!recipe.Version.IsValid)
        {
            issues.Add(Issue(
                ProductionRecipeIssueCodes.RecipeVersionInvalid,
                $"A production recipe version must be at least {ProductionRecipeVersion.Minimum}."));
        }

        var requirements = recipe.Requirements;
        if (requirements is null || requirements.Count == 0)
        {
            issues.Add(Issue(
                ProductionRecipeIssueCodes.RequirementsEmpty,
                "A production recipe must declare at least one capability requirement."));

            return issues;
        }

        if (!requirements.Any(requirement => requirement is not null && requirement.IsRequired))
        {
            issues.Add(Issue(
                ProductionRecipeIssueCodes.RequiredRequirementsEmpty,
                "A production recipe must declare at least one required capability requirement."));
        }

        var seenCapabilities = new HashSet<CapabilityId>();

        foreach (var requirement in requirements)
        {
            var capability = requirement?.Capability;
            if (capability is null || !CapabilityId.IsValid(capability.CapabilityId.Value))
            {
                issues.Add(Issue(
                    ProductionRecipeIssueCodes.RequirementCapabilityInvalid,
                    "A production recipe requirement must reference a valid capability id."));

                continue;
            }

            if (!seenCapabilities.Add(capability.CapabilityId))
            {
                issues.Add(Issue(
                    ProductionRecipeIssueCodes.RequirementDuplicate,
                    $"Capability '{capability.CapabilityId}' is required more than once."));
            }

            var seenFallbacks = new HashSet<CapabilityId>();
            foreach (var fallback in capability.FallbackCapabilityIds ?? [])
            {
                if (!CapabilityId.IsValid(fallback.Value))
                {
                    issues.Add(Issue(
                        ProductionRecipeIssueCodes.RequirementCapabilityInvalid,
                        $"Capability '{capability.CapabilityId}' declares an invalid fallback capability id."));

                    continue;
                }

                if (fallback.Equals(capability.CapabilityId))
                {
                    issues.Add(Issue(
                        ProductionRecipeIssueCodes.FallbackRepeatsPrimary,
                        $"Capability '{capability.CapabilityId}' lists itself as a fallback."));
                }

                if (!seenFallbacks.Add(fallback))
                {
                    issues.Add(Issue(
                        ProductionRecipeIssueCodes.FallbackDuplicate,
                        $"Capability '{capability.CapabilityId}' lists fallback '{fallback}' more than once."));
                }
            }
        }

        return issues;
    }

    private static ProductionRecipeIssue Issue(string code, string message) =>
        new() { Code = code, Message = message };
}
