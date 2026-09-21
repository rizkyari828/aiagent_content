namespace AIStudio.Application.ProductionRecipes;

/// <summary>
/// One deterministic validation problem found in a production recipe. A recipe
/// that reports no issues is structurally valid data; whether it is producible is
/// decided later by <c>IProductionRecipeResolver</c> together with the capability
/// registry.
/// </summary>
public sealed record ProductionRecipeIssue
{
    public string Code { get; init; } = string.Empty;

    public string Message { get; init; } = string.Empty;
}

/// <summary>Stable validation codes, safe to surface to future data loaders.</summary>
public static class ProductionRecipeIssueCodes
{
    public const string RecipeIdInvalid = "recipe_id_invalid";
    public const string RecipeVersionInvalid = "recipe_version_invalid";
    public const string RequirementsEmpty = "recipe_requirements_empty";
    public const string RequiredRequirementsEmpty = "recipe_required_requirements_empty";
    public const string RequirementCapabilityInvalid = "recipe_requirement_capability_invalid";
    public const string RequirementDuplicate = "recipe_requirement_duplicate";
    public const string FallbackDuplicate = "recipe_fallback_duplicate";
    public const string FallbackRepeatsPrimary = "recipe_fallback_repeats_primary";
}
