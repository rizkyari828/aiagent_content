using AIStudio.Application.Stories;
using Xunit;

namespace AIStudio.Tests.Stories;

public sealed class NarrativePatternRegistryTests
{
    [Fact]
    public void ListsSeedPatternsInDeterministicOrder()
    {
        var registry = new NarrativePatternRegistry(SeedNarrativePatterns.All);

        Assert.Equal(
            ["explanatory-flow", "problem-solution-short"],
            registry.Patterns.Select(pattern => pattern.Id.Value));
    }

    [Fact]
    public void RegisterThenRetrieveByStableIdAndVersion()
    {
        var registry = new NarrativePatternRegistry();
        var pattern = StoryTestSupport.Pattern("custom-pattern");

        registry.Register(pattern);

        Assert.True(registry.Contains(new NarrativePatternId("custom-pattern"), new NarrativePatternVersion(1)));
        Assert.Equal(
            "custom-pattern",
            registry.Get(new NarrativePatternId("custom-pattern"), new NarrativePatternVersion(1)).Id.Value);
        Assert.Equal("custom-pattern", registry.GetLatest(new NarrativePatternId("custom-pattern")).Id.Value);
    }

    [Fact]
    public void GetLatestReturnsHighestVersionDeterministically()
    {
        var v1 = StoryTestSupport.Pattern("evolving", version: 1);
        var v2 = StoryTestSupport.Pattern("evolving", version: 2);
        var registry = new NarrativePatternRegistry([v2, v1]);

        Assert.Equal(2, registry.GetLatest(new NarrativePatternId("evolving")).Version.Value);
        Assert.Equal(1, registry.Get(new NarrativePatternId("evolving"), new NarrativePatternVersion(1)).Version.Value);
    }

    [Fact]
    public void DuplicateIdAndVersionRegistrationIsRejected()
    {
        var registry = new NarrativePatternRegistry([StoryTestSupport.Pattern("duplicate")]);

        Assert.Throws<InvalidOperationException>(
            () => registry.Register(StoryTestSupport.Pattern("duplicate")));
    }

    [Fact]
    public void InvalidPatternRegistrationIsRejected()
    {
        var registry = new NarrativePatternRegistry();

        Assert.Throws<InvalidOperationException>(() => registry.Register(new NarrativePattern()));
    }

    [Fact]
    public void MissingPatternThrows()
    {
        var registry = new NarrativePatternRegistry(SeedNarrativePatterns.All);

        Assert.Throws<KeyNotFoundException>(
            () => registry.GetLatest(new NarrativePatternId("missing-pattern")));
        Assert.False(registry.TryGetLatest(new NarrativePatternId("missing-pattern"), out _));
    }

    [Fact]
    public void SeedPatternsAreStructurallyValid()
    {
        foreach (var pattern in SeedNarrativePatterns.All)
        {
            Assert.Empty(pattern.Validate());
        }

        Assert.Equal(2, SeedNarrativePatterns.All.Count);
    }

    [Fact]
    public void SeedPatternsExposeGenericRolesNotEnums()
    {
        var registry = new NarrativePatternRegistry(SeedNarrativePatterns.All);

        var roles = registry
            .GetLatest(new NarrativePatternId("problem-solution-short"))
            .BeatSlots
            .Select(slot => slot.Role.Value);

        Assert.Equal(["hook", "problem", "discovery", "solution", "payoff"], roles);
    }
}
