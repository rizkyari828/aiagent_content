using System.Text.Json;
using AIStudio.Application.Stories;
using Xunit;

namespace AIStudio.Tests.Stories;

public sealed class StoryPlanSerializationTests
{
    [Fact]
    public void StoryPlanRoundTripsThroughJson()
    {
        var plan = StoryTestSupport.Plan(
            [
                StoryTestSupport.Beat("beat-01", 1, role: "hook", duration: 20),
                StoryTestSupport.Beat(
                    "beat-02",
                    2,
                    role: "payoff",
                    duration: 40,
                    continuityFrom: ["beat-01"],
                    characterRefs: ["developer"],
                    worldRefs: ["bedroom"])
            ],
            targetDuration: 60);

        var json = JsonSerializer.Serialize(plan);
        var roundTripped = JsonSerializer.Deserialize<StoryPlan>(json);

        Assert.NotNull(roundTripped);
        Assert.Equal(plan.Id, roundTripped!.Id);
        Assert.Equal(plan.Version, roundTripped.Version);
        Assert.Equal(plan.SourceConceptId, roundTripped.SourceConceptId);
        Assert.Equal(plan.NarrativePattern, roundTripped.NarrativePattern);
        Assert.Equal(plan.NarrativePatternVersion, roundTripped.NarrativePatternVersion);
        Assert.Equal(plan.TargetDurationSeconds, roundTripped.TargetDurationSeconds);
        Assert.Equal(plan.Beats.Count, roundTripped.Beats.Count);
        Assert.Equal("payoff", roundTripped.Beats[1].Role.Value);
        Assert.Equal(["beat-01"], roundTripped.Beats[1].ContinuityFrom.Select(id => id.Value));
        Assert.Equal(["developer"], roundTripped.Beats[1].CharacterRefs);
        Assert.Empty(roundTripped.Validate());
    }

    [Fact]
    public void StoryPlanUsesCleanScalars()
    {
        var json = JsonSerializer.Serialize(StoryTestSupport.Plan());

        Assert.Contains("\"id\":\"run-ai-locally-story\"", json);
        Assert.Contains("\"version\":1", json);
        Assert.Contains("\"sourceConceptId\":\"run-ai-locally-anime-short\"", json);
        Assert.Contains("\"narrativePattern\":\"problem-solution-short\"", json);
        Assert.Contains("\"narrativePatternVersion\":1", json);
        Assert.Contains("\"targetDurationSeconds\":60", json);
    }

    [Fact]
    public void NarrativePatternRoundTripsThroughJson()
    {
        var json = JsonSerializer.Serialize(SeedNarrativePatterns.All[0]);
        var roundTripped = JsonSerializer.Deserialize<NarrativePattern>(json);

        Assert.NotNull(roundTripped);
        Assert.Equal("problem-solution-short", roundTripped!.Id.Value);
        Assert.Equal(5, roundTripped.BeatSlots.Count);
        Assert.Equal("hook", roundTripped.BeatSlots[0].Role.Value);
        Assert.Empty(roundTripped.Validate());
    }
}
