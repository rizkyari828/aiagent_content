using AIStudio.Application.Stories;
using Xunit;

namespace AIStudio.Tests.Stories;

public sealed class StoryValueObjectTests
{
    [Theory]
    [InlineData("run-ai-locally-story")]
    [InlineData("plan.1")]
    [InlineData("problem-solution-short")]
    public void ValidStoryIdentifiersAreAccepted(string value)
    {
        Assert.Equal(value, new StoryPlanId(value).Value);
        Assert.Equal(value, new NarrativePatternId(value).Value);
        Assert.Equal(value, new StoryBeatId(value).Value);
        Assert.Equal(value, new StoryBeatRole(value).Value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Run-AI")]
    [InlineData("-leading")]
    [InlineData("has space")]
    public void InvalidStoryIdentifiersAreRejected(string value)
    {
        Assert.Throws<ArgumentException>(() => new StoryPlanId(value));
        Assert.Throws<ArgumentException>(() => new NarrativePatternId(value));
        Assert.Throws<ArgumentException>(() => new StoryBeatId(value));
        Assert.Throws<ArgumentException>(() => new StoryBeatRole(value));
    }

    [Fact]
    public void TryParseRejectsInvalidValuesWithoutThrowing()
    {
        Assert.False(StoryPlanId.TryParse("Bad", out _));
        Assert.False(NarrativePatternId.TryParse(null, out _));
        Assert.True(StoryBeatId.TryParse("beat-01", out var beatId));
        Assert.Equal("beat-01", beatId.Value);
    }

    [Fact]
    public void VersionsMustBePositive()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new StoryPlanVersion(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new NarrativePatternVersion(-1));

        Assert.False(default(StoryPlanVersion).IsValid);
        Assert.False(default(NarrativePatternVersion).IsValid);
        Assert.True(new StoryPlanVersion(1).IsValid);
        Assert.True(new NarrativePatternVersion(2).IsValid);
    }

    [Fact]
    public void ArbitraryBeatRolesAreJustData()
    {
        var roles = new[]
        {
            "hook", "setup", "problem", "discovery", "conflict", "reveal", "twist",
            "payoff", "tutorial-step", "product-demo", "chorus", "emotional-turn",
            "cliffhanger", "verification", "celebration"
        };

        foreach (var role in roles)
        {
            Assert.Equal(role, new StoryBeatRole(role).Value);
        }
    }
}
