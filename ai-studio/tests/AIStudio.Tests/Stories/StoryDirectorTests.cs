using AIStudio.Application.Stories;
using Xunit;

namespace AIStudio.Tests.Stories;

public sealed class StoryDirectorTests
{
    private static StoryDirector Director(params NarrativePattern[] patterns) =>
        new(new NarrativePatternRegistry(patterns.Length > 0 ? patterns : SeedNarrativePatterns.All));

    [Fact]
    public void ProducesDeterministicPlanFromPattern()
    {
        var result = Director().Direct(StoryTestSupport.Request(targetDuration: 60));
        var plan = result.Plan;

        Assert.Equal("run-ai-locally-anime-short-story", plan.Id.Value);
        Assert.Equal(1, plan.Version.Value);
        Assert.Equal("run-ai-locally-anime-short", plan.SourceConceptId.Value);
        Assert.Equal("problem-solution-short", plan.NarrativePattern.Value);
        Assert.Equal(1, plan.NarrativePatternVersion.Value);
        Assert.Equal(60, plan.TargetDurationSeconds);

        Assert.Equal(
            ["hook", "problem", "discovery", "solution", "payoff"],
            plan.Beats.Select(beat => beat.Role.Value));
        Assert.Equal([1, 2, 3, 4, 5], plan.Beats.Select(beat => beat.Order));
        Assert.Equal(60, plan.Beats.Sum(beat => beat.TargetDurationSeconds));
        Assert.Empty(plan.Validate(SeedNarrativePatterns.All[0]));
    }

    [Fact]
    public void BeatContinuityFormsASimpleChain()
    {
        var plan = Director().Direct(StoryTestSupport.Request(targetDuration: 60)).Plan;

        Assert.Empty(plan.Beats[0].ContinuityFrom);
        Assert.Equal("beat-01", plan.Beats[1].ContinuityFrom.Single().Value);
        Assert.Equal("beat-04", plan.Beats[4].ContinuityFrom.Single().Value);
    }

    [Fact]
    public void UsesSourceConceptDurationWhenTargetIsNotSupplied()
    {
        var direction = StoryTestSupport.Direction(duration: 45);

        var plan = Director().Direct(StoryTestSupport.Request(direction)).Plan;

        Assert.Equal(45, plan.TargetDurationSeconds);
        Assert.Equal(45, plan.Beats.Sum(beat => beat.TargetDurationSeconds));
    }

    [Fact]
    public void UsesExplicitPlanIdAndVersion()
    {
        var plan = Director()
            .Direct(StoryTestSupport.Request(planId: "custom-story", targetDuration: 60))
            .Plan;

        Assert.Equal("custom-story", plan.Id.Value);
    }

    [Fact]
    public void UnknownPatternThrowsStableCode()
    {
        var exception = Assert.Throws<StoryDirectorException>(
            () => Director().Direct(StoryTestSupport.Request(pattern: "missing-pattern")));

        Assert.Equal(StoryDirectorErrorCodes.PatternNotFound, exception.Code);
    }

    [Fact]
    public void MissingPatternIdThrowsRequestInvalid()
    {
        var request = StoryTestSupport.Request() with { NarrativePattern = default };

        var exception = Assert.Throws<StoryDirectorException>(() => Director().Direct(request));

        Assert.Equal(StoryDirectorErrorCodes.RequestInvalid, exception.Code);
    }

    [Fact]
    public void ExplicitPatternVersionIsResolvedDeterministically()
    {
        var v1 = StoryTestSupport.Pattern("anime-horror-reveal", version: 1, slots: [StoryTestSupport.Slot("normality")]);
        var v2 = StoryTestSupport.Pattern("anime-horror-reveal", version: 2, slots: [StoryTestSupport.Slot("sting")]);
        var director = Director(v1, v2);

        var plan = director
            .Direct(StoryTestSupport.Request(pattern: "anime-horror-reveal", patternVersion: 1, targetDuration: 30))
            .Plan;

        Assert.Equal(1, plan.NarrativePatternVersion.Value);
        Assert.Equal("normality", plan.Beats.Single().Role.Value);
    }

    [Fact]
    public void DirectorHasNoAiOrProcessDependency()
    {
        var parameters = typeof(StoryDirector)
            .GetConstructors()
            .Single()
            .GetParameters()
            .Select(parameter => parameter.ParameterType)
            .ToList();

        Assert.Equal([typeof(INarrativePatternRegistry)], parameters);
    }

    [Fact]
    public void BeatsCarryNarrativePurposeNotFinalDialogue()
    {
        var plan = Director().Direct(StoryTestSupport.Request(targetDuration: 60)).Plan;
        var pattern = SeedNarrativePatterns.All[0];

        for (var index = 0; index < plan.Beats.Count; index++)
        {
            Assert.Equal(pattern.BeatSlots[index].Purpose, plan.Beats[index].Purpose);
            Assert.DoesNotContain('?', plan.Beats[index].Purpose);
            Assert.DoesNotContain('"', plan.Beats[index].Purpose);
        }
    }
}
