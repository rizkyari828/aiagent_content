using AIStudio.Application.Bibles;
using AIStudio.Application.Concepts;
using AIStudio.Application.Stories;
using AIStudio.Application.StoryContext;
using AIStudio.Tests.Bibles;
using Xunit;

namespace AIStudio.Tests.StoryContext;

public sealed class StoryBeatScopeTests
{
    private static StoryPlan EightBeatPlan() =>
        new StoryPlanBuilder().Build();

    private static StoryContextBuilder Builder() =>
        new(
            new CharacterBibleRegistry(
            [
                BibleTestSupport.Character("alpha"),
                BibleTestSupport.Character("beta"),
                BibleTestSupport.Character("rio"),
                BibleTestSupport.Character("delta"),
                BibleTestSupport.Character("epsilon"),
                BibleTestSupport.Character("zeta")
            ]),
            new WorldBibleRegistry(
            [
                BibleTestSupport.World("stage", environmentType: "stage"),
                BibleTestSupport.World("lab", environmentType: "lab"),
                BibleTestSupport.World("bedroom", environmentType: "bedroom"),
                BibleTestSupport.World("office", environmentType: "office"),
                BibleTestSupport.World("park", environmentType: "park")
            ]));

    [Fact]
    public void WholeStoryContextIncludesEveryBeat()
    {
        var result = Builder().Build(StoryContextTestSupport.Request(plan: EightBeatPlan()));

        Assert.True(result.IsValid);
        Assert.Equal(8, result.Context.Beats.Count);
        Assert.Null(result.Context.BeatScope);
    }

    [Fact]
    public void BeatScopeIncludesOnlyTheBeatAndItsContinuityDependencies()
    {
        var result = Builder().Build(StoryContextTestSupport.Request(plan: EightBeatPlan(), beatId: "beat-05"));

        Assert.True(result.IsValid);
        Assert.Equal("beat-05", result.Context.BeatScope!.Value.Value);
        Assert.Equal(["beat-03", "beat-04", "beat-05"], result.Context.Beats.Select(beat => beat.Id.Value));
    }

    [Fact]
    public void BeatScopeExcludesUnrelatedBeatsCharactersAndWorlds()
    {
        var context = Builder()
            .Build(StoryContextTestSupport.Request(plan: EightBeatPlan(), beatId: "beat-05"))
            .Context;

        Assert.Equal(["rio"], context.Characters.Select(character => character.Id.Value));
        Assert.Equal(["bedroom"], context.Worlds.Select(world => world.Id.Value));
        Assert.DoesNotContain("delta", context.Characters.Select(character => character.Id.Value));
        Assert.DoesNotContain("beat-01", context.Beats.Select(beat => beat.Id.Value));
        Assert.DoesNotContain("office", context.Worlds.Select(world => world.Id.Value));
    }

    [Fact]
    public void UnknownBeatIdProducesAnExplicitIssue()
    {
        var result = Builder().Build(StoryContextTestSupport.Request(plan: EightBeatPlan(), beatId: "beat-99"));

        Assert.False(result.IsValid);
        Assert.Contains(result.Issues, issue => issue.Code == StoryContextIssueCodes.BeatNotFound);
        Assert.Empty(result.Context.Beats);
    }
}

/// <summary>
/// Builds an eight-beat plan where beat-05's continuity chain is exactly beats 3-4-5,
/// and the other beats reference characters/worlds outside that chain.
/// </summary>
internal sealed class StoryPlanBuilder
{
    public StoryPlan Build() =>
        new()
        {
            Id = new StoryPlanId("eight-beat-story"),
            Version = new StoryPlanVersion(1),
            SourceConceptId = new ConceptId("local-ai-tech-explainer"),
            NarrativePattern = new NarrativePatternId("problem-solution-short"),
            NarrativePatternVersion = new NarrativePatternVersion(1),
            TargetDurationSeconds = 80,
            Beats =
            [
                Beat("beat-01", 1, "hook", ["alpha"], ["stage"]),
                Beat("beat-02", 2, "setup", ["beta"], ["lab"]),
                Beat("beat-03", 3, "problem", ["rio"], ["bedroom"]),
                Beat("beat-04", 4, "obstacle", ["rio"], ["bedroom"], "beat-03"),
                Beat("beat-05", 5, "reveal", ["rio"], ["bedroom"], "beat-04"),
                Beat("beat-06", 6, "resolution", ["delta"], ["office"]),
                Beat("beat-07", 7, "payoff", ["epsilon"], ["office"]),
                Beat("beat-08", 8, "celebration", ["zeta"], ["park"])
            ]
        };

    private static StoryBeat Beat(
        string id,
        int order,
        string role,
        IReadOnlyList<string> characterRefs,
        IReadOnlyList<string> worldRefs,
        string? continuityFrom = null) =>
        new()
        {
            Id = new StoryBeatId(id),
            Order = order,
            Role = new StoryBeatRole(role),
            Importance = StoryBeat.DefaultImportance,
            Purpose = $"{role} purpose",
            TargetDurationSeconds = 10,
            CharacterRefs = characterRefs,
            WorldRefs = worldRefs,
            ContinuityFrom = continuityFrom is null ? [] : [new StoryBeatId(continuityFrom)]
        };
}
