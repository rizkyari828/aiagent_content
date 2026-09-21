using AIStudio.Application.Concepts;
using Xunit;

namespace AIStudio.Tests.Concepts;

public sealed class ConceptIdTests
{
    [Theory]
    [InlineData("local-ai-tech-explainer")]
    [InlineData("local-ai-motion-comic")]
    [InlineData("3d-kids-story")]
    [InlineData("anime.tech_short")]
    public void AcceptsStableIdentifiers(string value)
    {
        var id = new ConceptId(value);

        Assert.Equal(value, id.Value);
        Assert.Equal(value, id.ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("Local-AI")]
    [InlineData("-anime")]
    [InlineData(".anime")]
    [InlineData("anime short")]
    [InlineData("anime!")]
    public void RejectsMalformedIdentifiers(string value)
    {
        Assert.False(ConceptIdentifier.IsValid(value));
        Assert.Throws<ArgumentException>(() => new ConceptId(value));
    }

    [Fact]
    public void RejectsOverlongIdentifier()
    {
        Assert.False(ConceptIdentifier.IsValid(new string('a', 65)));
    }

    [Fact]
    public void TryParseReturnsFalseForMalformedValue()
    {
        Assert.False(ConceptId.TryParse("Anime", out var parsed));
        Assert.Equal(default, parsed);
    }

    [Fact]
    public void VersionRejectsZeroAndNegativeValues()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ConceptVersion(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ConceptVersion(-1));
        Assert.False(default(ConceptVersion).IsValid);
        Assert.True(new ConceptVersion(1).IsValid);
    }

    [Theory]
    [InlineData("anime-short")]
    [InlineData("anime-cinematic")]
    [InlineData("youtube-longform")]
    [InlineData("clean-tech")]
    public void FormatAndStyleTokensStayDataDriven(string value)
    {
        Assert.True(ConceptIdentifier.IsValid(value));
    }
}
