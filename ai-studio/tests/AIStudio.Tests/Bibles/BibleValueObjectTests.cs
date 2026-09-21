using AIStudio.Application.Bibles;
using Xunit;

namespace AIStudio.Tests.Bibles;

public sealed class BibleValueObjectTests
{
    [Theory]
    [InlineData("rio")]
    [InlineData("rio-bedroom")]
    [InlineData("character-rio-front-v1")]
    public void ValidIdsAreAccepted(string value)
    {
        Assert.Equal(value, new CharacterBibleId(value).Value);
        Assert.Equal(value, new WorldBibleId(value).Value);
        Assert.Equal(value, new AssetReferenceId(value).Value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Rio")]
    [InlineData("-leading")]
    [InlineData("has space")]
    public void InvalidIdsAreRejected(string value)
    {
        Assert.Throws<ArgumentException>(() => new CharacterBibleId(value));
        Assert.Throws<ArgumentException>(() => new WorldBibleId(value));
        Assert.Throws<ArgumentException>(() => new AssetReferenceId(value));
    }

    [Fact]
    public void TryParseRejectsInvalidValuesWithoutThrowing()
    {
        Assert.False(CharacterBibleId.TryParse("Bad", out _));
        Assert.False(WorldBibleId.TryParse(null, out _));
        Assert.True(AssetReferenceId.TryParse("laptop-prop", out var assetId));
        Assert.Equal("laptop-prop", assetId.Value);
    }

    [Fact]
    public void VersionsMustBePositive()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new CharacterBibleVersion(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new WorldBibleVersion(-1));

        Assert.False(default(CharacterBibleVersion).IsValid);
        Assert.False(default(WorldBibleVersion).IsValid);
        Assert.True(new CharacterBibleVersion(1).IsValid);
        Assert.True(new WorldBibleVersion(2).IsValid);
    }
}
