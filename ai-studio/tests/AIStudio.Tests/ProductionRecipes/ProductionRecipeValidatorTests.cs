using AIStudio.Application.Capabilities;
using AIStudio.Application.ProductionRecipes;
using Xunit;

namespace AIStudio.Tests.ProductionRecipes;

public sealed class ProductionRecipeValidatorTests
{
    [Fact]
    public void SeedRecipesAreStructurallyValid()
    {
        Assert.All(SeedProductionRecipes.All, recipe => Assert.Empty(recipe.Validate()));
    }

    [Fact]
    public void EmptyRecipeReportsIdentityAndRequirementIssues()
    {
        var issues = ProductionRecipeValidator.Validate(new ProductionRecipe());

        Assert.Contains(ProductionRecipeIssueCodes.RecipeIdInvalid, Codes(issues));
        Assert.Contains(ProductionRecipeIssueCodes.RecipeVersionInvalid, Codes(issues));
        Assert.Contains(ProductionRecipeIssueCodes.RequirementsEmpty, Codes(issues));
    }

    [Fact]
    public void RecipeWithoutAnyRequiredRequirementIsRejected()
    {
        var recipe = ProductionRecipeTestSupport.Recipe(
            "optional-only",
            1,
            ProductionRecipeRequirement.Optional(CapabilityIds.VisualThreeD));

        Assert.Contains(ProductionRecipeIssueCodes.RequiredRequirementsEmpty, Codes(recipe.Validate()));
    }

    [Fact]
    public void DuplicatePrimaryCapabilityIsRejected()
    {
        var recipe = ProductionRecipeTestSupport.Recipe(
            "duplicate",
            1,
            ProductionRecipeRequirement.Required(CapabilityIds.VisualDiagram),
            ProductionRecipeRequirement.Required(CapabilityIds.VisualDiagram));

        Assert.Contains(ProductionRecipeIssueCodes.RequirementDuplicate, Codes(recipe.Validate()));
    }

    [Fact]
    public void PrimaryCapabilityRepeatedInItsOwnFallbackIsRejected()
    {
        var recipe = ProductionRecipeTestSupport.Recipe(
            "self-fallback",
            1,
            ProductionRecipeRequirement.Required(CapabilityIds.VisualDiagram, CapabilityIds.VisualDiagram));

        Assert.Contains(ProductionRecipeIssueCodes.FallbackRepeatsPrimary, Codes(recipe.Validate()));
    }

    [Fact]
    public void DuplicateFallbackCapabilityIsRejected()
    {
        var recipe = ProductionRecipeTestSupport.Recipe(
            "duplicate-fallback",
            1,
            ProductionRecipeRequirement.Required(
                CapabilityIds.VisualDiagram,
                CapabilityIds.VisualUiMotion,
                CapabilityIds.VisualUiMotion));

        Assert.Contains(ProductionRecipeIssueCodes.FallbackDuplicate, Codes(recipe.Validate()));
    }

    [Fact]
    public void UnimplementedButValidCapabilityIsStillValidRecipeData()
    {
        var recipe = ProductionRecipeTestSupport.Recipe(
            "future-format",
            1,
            ProductionRecipeRequirement.Required(new CapabilityId("visual.character_animation")));

        Assert.Empty(recipe.Validate());
    }

    [Theory]
    [InlineData("")]
    [InlineData("visual")]
    [InlineData("Visual.Burned")]
    public void InvalidCapabilityIdsCannotBeRepresented(string value)
    {
        Assert.False(CapabilityId.TryParse(value, out _));
        Assert.Throws<ArgumentException>(() => new CapabilityId(value));
    }

    private static IReadOnlyList<string> Codes(IReadOnlyList<ProductionRecipeIssue> issues) =>
        issues.Select(issue => issue.Code).ToList();
}
