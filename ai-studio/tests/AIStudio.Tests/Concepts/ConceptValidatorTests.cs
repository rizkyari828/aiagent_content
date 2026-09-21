using AIStudio.Application.Concepts;
using AIStudio.Application.ProductionRecipes;
using Xunit;

namespace AIStudio.Tests.Concepts;

public sealed class ConceptValidatorTests
{
    [Fact]
    public void SeedConceptsAreStructurallyValid()
    {
        Assert.All(SeedConcepts.All, concept => Assert.Empty(concept.Validate()));
    }

    [Fact]
    public void EmptyConceptReportsIdentityAndMetadataIssues()
    {
        var codes = Codes(new ConceptManifest().Validate());

        Assert.Contains(ConceptValidationIssueCodes.ConceptIdInvalid, codes);
        Assert.Contains(ConceptValidationIssueCodes.ConceptVersionInvalid, codes);
        Assert.Contains(ConceptValidationIssueCodes.TitleEmpty, codes);
        Assert.Contains(ConceptValidationIssueCodes.FormatInvalid, codes);
        Assert.Contains(ConceptValidationIssueCodes.StyleInvalid, codes);
        Assert.Contains(ConceptValidationIssueCodes.RecipeIdInvalid, codes);
        Assert.Contains(ConceptValidationIssueCodes.DurationInvalid, codes);
    }

    [Fact]
    public void MalformedFormatAndStyleAreRejected()
    {
        var concept = ConceptTestSupport.Concept("bad-tokens") with
        {
            Format = "YouTube Longform",
            Style = "Clean Tech"
        };

        var codes = Codes(concept.Validate());

        Assert.Contains(ConceptValidationIssueCodes.FormatInvalid, codes);
        Assert.Contains(ConceptValidationIssueCodes.StyleInvalid, codes);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(86_401)]
    public void DurationOutsideBoundsIsRejected(int duration)
    {
        var concept = ConceptTestSupport.Concept("bad-duration", duration: duration);

        Assert.Contains(ConceptValidationIssueCodes.DurationInvalid, Codes(concept.Validate()));
    }

    [Fact]
    public void InvalidAndDuplicateTagsAreRejected()
    {
        var concept = ConceptTestSupport.Concept("bad-tags") with
        {
            Tags = ["local-ai", "Local AI", "local-ai"]
        };

        var codes = Codes(concept.Validate());

        Assert.Contains(ConceptValidationIssueCodes.TagInvalid, codes);
        Assert.Contains(ConceptValidationIssueCodes.TagDuplicate, codes);
    }

    [Fact]
    public void UnknownFutureRecipeStillLeavesTheConceptValid()
    {
        var concept = ConceptTestSupport.Concept(
            "future-anime-short",
            format: "anime-short",
            style: "anime-cinematic",
            recipeId: "future-anime-video");

        // Validation is structural only; it never consults the recipe registry.
        Assert.Empty(concept.Validate());
    }

    [Fact]
    public void UnknownRecipeIdIsStillValidatedAsAnIdentifier()
    {
        var concept = ConceptTestSupport.Concept("bad-recipe") with
        {
            RecipeId = new ProductionRecipeId("valid-recipe")
        };
        Assert.Empty(concept.Validate());

        var invalid = new ConceptManifest
        {
            Id = new ConceptId("concept"),
            Version = new ConceptVersion(1),
            Title = "Concept",
            Format = "youtube-short",
            Style = "clean-tech",
            RecipeId = default,
            Duration = 45
        };

        Assert.Contains(ConceptValidationIssueCodes.RecipeIdInvalid, Codes(invalid.Validate()));
    }

    private static IReadOnlyList<string> Codes(IReadOnlyList<ConceptValidationIssue> issues) =>
        issues.Select(issue => issue.Code).ToList();
}
