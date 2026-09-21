using AIStudio.Application.ProductionRecipes;
using Xunit;

namespace AIStudio.Tests.ProductionRecipes;

public sealed class ProductionRecipeIdTests
{
    [Theory]
    [InlineData("tech-explainer")]
    [InlineData("motion-comic")]
    [InlineData("kids.3d_story")]
    [InlineData("a")]
    public void AcceptsStableIdentifiers(string value)
    {
        var id = new ProductionRecipeId(value);

        Assert.Equal(value, id.Value);
        Assert.Equal(value, id.ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("Tech-Explainer")]
    [InlineData("-tech")]
    [InlineData(".tech")]
    [InlineData("tech explainer")]
    [InlineData("tech!")]
    public void RejectsMalformedIdentifiers(string value)
    {
        Assert.False(ProductionRecipeId.IsValid(value));
        Assert.Throws<ArgumentException>(() => new ProductionRecipeId(value));
    }

    [Fact]
    public void RejectsOverlongIdentifier()
    {
        Assert.False(ProductionRecipeId.IsValid(new string('a', 65)));
    }

    [Fact]
    public void TryParseReturnsFalseForMalformedValue()
    {
        Assert.False(ProductionRecipeId.TryParse("Tech", out var parsed));
        Assert.Equal(default, parsed);
    }

    [Fact]
    public void SeedIdentifiersAreStable()
    {
        Assert.Equal("tech-explainer", new ProductionRecipeId("tech-explainer").Value);
        Assert.Equal("motion-comic", new ProductionRecipeId("motion-comic").Value);
    }

    [Fact]
    public void VersionRejectsZeroAndNegativeValues()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ProductionRecipeVersion(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ProductionRecipeVersion(-1));
        Assert.False(default(ProductionRecipeVersion).IsValid);
        Assert.True(new ProductionRecipeVersion(1).IsValid);
    }
}
