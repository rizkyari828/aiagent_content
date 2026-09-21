using System.Text.Json;
using AIStudio.Application.Stories;
using Xunit;

namespace AIStudio.Tests.Stories;

public sealed class StoryPlanParserTests
{
    private static readonly NarrativePatternRegistry Patterns = new(SeedNarrativePatterns.All);

    private static string ValidPlanJson() =>
        JsonSerializer.Serialize(
            new StoryDirector(Patterns).Direct(StoryTestSupport.Request(targetDuration: 60)).Plan);

    [Fact]
    public void ParsesAValidPlanWithPatternValidation()
    {
        var plan = StoryPlanParser.Parse(ValidPlanJson(), Patterns);

        Assert.Equal("run-ai-locally-anime-short-story", plan.Id.Value);
        Assert.Equal(5, plan.Beats.Count);
        Assert.Empty(plan.Validate(Patterns.GetLatest(new NarrativePatternId("problem-solution-short"))));
    }

    [Fact]
    public void ParsesAValidPlanWithoutARegistry()
    {
        var plan = StoryPlanParser.Parse(ValidPlanJson());

        Assert.Equal("problem-solution-short", plan.NarrativePattern.Value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("{ not json")]
    [InlineData("[]")]
    public void RejectsMalformedJson(string json)
    {
        var exception = Assert.Throws<StoryPlanException>(() => StoryPlanParser.Parse(json));

        Assert.Equal(StoryPlanParserErrorCodes.InvalidJson, exception.Code);
    }

    [Fact]
    public void RejectsUnknownTopLevelMembers()
    {
        var json = ValidPlanJson().Insert(1, "\"assemblyName\":\"Evil.Type\",");
        var exception = Assert.Throws<StoryPlanException>(() => StoryPlanParser.Parse(json, Patterns));

        Assert.Equal(StoryPlanParserErrorCodes.InvalidJson, exception.Code);
    }

    [Fact]
    public void RejectsUnknownBeatMembers()
    {
        var json = ValidPlanJson()
            .Replace("{\"id\":\"beat-01\"", "{\"command\":\"rm -rf /\",\"id\":\"beat-01\"");

        var exception = Assert.Throws<StoryPlanException>(() => StoryPlanParser.Parse(json, Patterns));

        Assert.Equal(StoryPlanParserErrorCodes.InvalidJson, exception.Code);
    }

    [Fact]
    public void RejectsUnknownNarrativePattern()
    {
        var json = ValidPlanJson().Replace("problem-solution-short", "missing-pattern");

        var exception = Assert.Throws<StoryPlanException>(() => StoryPlanParser.Parse(json, Patterns));

        Assert.Equal(StoryPlanParserErrorCodes.PatternUnknown, exception.Code);
    }

    [Fact]
    public void RejectsStructurallyInvalidPlan()
    {
        var plan = StoryTestSupport.Plan(
            [StoryTestSupport.Beat("beat-01", 0, duration: 0, purpose: string.Empty)],
            targetDuration: 60);
        var json = JsonSerializer.Serialize(plan);

        var exception = Assert.Throws<StoryPlanException>(() => StoryPlanParser.Parse(json, Patterns));

        Assert.Equal(StoryPlanParserErrorCodes.PlanInvalid, exception.Code);
    }

    [Fact]
    public void RejectsPlanMissingARequiredPatternSlot()
    {
        var plan = StoryTestSupport.Plan(
            [
                StoryTestSupport.Beat("beat-01", 1, role: "hook", duration: 30),
                StoryTestSupport.Beat("beat-02", 2, role: "problem", duration: 30)
            ],
            targetDuration: 60);
        var json = JsonSerializer.Serialize(plan);

        var exception = Assert.Throws<StoryPlanException>(() => StoryPlanParser.Parse(json, Patterns));

        Assert.Equal(StoryPlanParserErrorCodes.PlanInvalid, exception.Code);
    }

    [Fact]
    public void EmptyObjectIsRejected()
    {
        var exception = Assert.Throws<StoryPlanException>(() => StoryPlanParser.Parse("{}"));

        Assert.Equal(StoryPlanParserErrorCodes.PlanInvalid, exception.Code);
    }
}
