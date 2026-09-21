using System.Text.Json;
using AIStudio.Application.AI;
using AIStudio.Application.Stories;
using AIStudio.Tests.Creative;
using Xunit;

namespace AIStudio.Tests.Stories;

public sealed class QwenStoryDirectorTests
{
    [Fact]
    public void CallsAiTextGeneratorOnceWithStructuredOutputRequest()
    {
        var (director, ai) = Build(ValidResponse());

        director.Direct(Request());

        var request = Assert.Single(ai.Requests);
        Assert.Equal(AiResponseFormat.JsonObject, request.ResponseFormat);
        Assert.Null(request.Model);
        Assert.False(string.IsNullOrWhiteSpace(request.SystemPrompt));
        Assert.Contains("\"sourceConceptId\"", request.Prompt);
    }

    [Fact]
    public void ParsesValidModelPlan()
    {
        var (director, _) = Build(ValidResponse());

        var plan = director.Direct(Request()).Plan;

        Assert.Equal("run-ai-locally-anime-short-story", plan.Id.Value);
        Assert.Equal("run-ai-locally-anime-short", plan.SourceConceptId.Value);
        Assert.Equal("problem-solution-short", plan.NarrativePattern.Value);
        Assert.Equal(
            ["hook", "problem", "discovery", "solution", "payoff"],
            plan.Beats.Select(beat => beat.Role.Value));
        Assert.Equal(60, plan.Beats.Sum(beat => beat.TargetDurationSeconds));
        Assert.Empty(plan.Validate());
    }

    [Fact]
    public void RejectsMalformedJson()
    {
        var (director, _) = Build("{ not json");

        var exception = Assert.Throws<StoryPlanException>(() => director.Direct(Request()));

        Assert.Equal(StoryPlanParserErrorCodes.InvalidJson, exception.Code);
    }

    [Fact]
    public void RejectsAdditionalProperties()
    {
        var json = ValidResponse().Replace("{\"id\":", "{\"surprise\":true,\"id\":", StringComparison.Ordinal);
        var (director, _) = Build(json);

        var exception = Assert.Throws<StoryPlanException>(() => director.Direct(Request()));

        Assert.Equal(StoryPlanParserErrorCodes.InvalidJson, exception.Code);
    }

    [Fact]
    public void RejectsInvalidIdentifierToken()
    {
        // Mirrors the real-Qwen Creative failure mode: a structurally correct value
        // with an invalid token (space) must be rejected, never repaired.
        var json = ValidResponse().Replace("\"beat-01\"", "\"beat 01\"", StringComparison.Ordinal);
        var (director, _) = Build(json);

        var exception = Assert.Throws<StoryPlanException>(() => director.Direct(Request()));

        Assert.Equal(StoryPlanParserErrorCodes.InvalidJson, exception.Code);
    }

    [Fact]
    public void SurfacesValidatorFailure()
    {
        var json = ValidResponse().Replace(
            "\"targetDurationSeconds\":60",
            "\"targetDurationSeconds\":600",
            StringComparison.Ordinal);
        var (director, _) = Build(json);

        var exception = Assert.Throws<StoryPlanException>(() => director.Direct(Request()));

        Assert.Equal(StoryPlanParserErrorCodes.PlanInvalid, exception.Code);
    }

    [Fact]
    public void RejectsSourceConceptMismatch()
    {
        var json = ValidResponse().Replace(
            "\"sourceConceptId\":\"run-ai-locally-anime-short\"",
            "\"sourceConceptId\":\"some-other-concept\"",
            StringComparison.Ordinal);
        var (director, _) = Build(json);

        var exception = Assert.Throws<StoryDirectorException>(() => director.Direct(Request()));

        Assert.Equal(StoryDirectorErrorCodes.PlanInvalid, exception.Code);
    }

    [Fact]
    public void RejectsSwappedRegisteredPattern()
    {
        // The parser validates against the plan's own pattern reference, so a model
        // that swaps to another registered pattern must still fail the director check.
        var (director, _) = Build(PlanJson(pattern: "explanatory-flow"));

        var exception = Assert.Throws<StoryDirectorException>(
            () => director.Direct(Request(pattern: "problem-solution-short")));

        Assert.Equal(StoryDirectorErrorCodes.PlanInvalid, exception.Code);
    }

    [Fact]
    public void RejectsUnknownPatternInResponse()
    {
        var json = ValidResponse().Replace(
            "\"narrativePattern\":\"problem-solution-short\"",
            "\"narrativePattern\":\"invented-pattern\"",
            StringComparison.Ordinal);
        var (director, _) = Build(json);

        var exception = Assert.Throws<StoryPlanException>(
            () => director.Direct(Request(pattern: "problem-solution-short")));

        Assert.Equal(StoryPlanParserErrorCodes.PatternUnknown, exception.Code);
    }

    [Fact]
    public void ProviderFailureDoesNotFallBackToDeterministic()
    {
        var director = new QwenStoryDirector(
            new ThrowingAiTextGenerator(),
            new NarrativePatternRegistry(SeedNarrativePatterns.All));

        Assert.Throws<AiGenerationException>(() => director.Direct(Request()));
    }

    [Fact]
    public void MissingPatternIdIsRejectedBeforeCallingTheModel()
    {
        var (director, ai) = Build(ValidResponse());
        var request = Request() with { NarrativePattern = default };

        var exception = Assert.Throws<StoryDirectorException>(() => director.Direct(request));

        Assert.Equal(StoryDirectorErrorCodes.RequestInvalid, exception.Code);
        Assert.Empty(ai.Requests);
    }

    [Fact]
    public void UnknownRequestedPatternThrowsStableCode()
    {
        var (director, ai) = Build(ValidResponse());

        var exception = Assert.Throws<StoryDirectorException>(
            () => director.Direct(Request(pattern: "missing-pattern")));

        Assert.Equal(StoryDirectorErrorCodes.PatternNotFound, exception.Code);
        Assert.Empty(ai.Requests);
    }

    private static (QwenStoryDirector Director, FakeAiTextGenerator Ai) Build(string response)
    {
        var ai = new FakeAiTextGenerator
        {
            Response = new AiTextResponse(response, "fake-model", null, null, null)
        };

        return (new QwenStoryDirector(ai, new NarrativePatternRegistry(SeedNarrativePatterns.All)), ai);
    }

    private static StoryDirectorRequest Request(
        int targetDuration = 60,
        string conceptId = "run-ai-locally-anime-short",
        string pattern = "problem-solution-short") =>
        StoryTestSupport.Request(
            direction: StoryTestSupport.Direction(conceptId: conceptId, duration: targetDuration),
            pattern: pattern,
            targetDuration: targetDuration);

    private static string ValidResponse(
        int targetDuration = 60,
        string conceptId = "run-ai-locally-anime-short",
        string pattern = "problem-solution-short") =>
        PlanJson(pattern, targetDuration, conceptId);

    private static string PlanJson(
        string pattern = "problem-solution-short",
        int targetDuration = 60,
        string conceptId = "run-ai-locally-anime-short")
    {
        var plan = new StoryDirector(new NarrativePatternRegistry(SeedNarrativePatterns.All))
            .Direct(StoryTestSupport.Request(
                direction: StoryTestSupport.Direction(conceptId: conceptId, duration: targetDuration),
                pattern: pattern,
                targetDuration: targetDuration))
            .Plan;

        return JsonSerializer.Serialize(plan);
    }

    private sealed class ThrowingAiTextGenerator : IAiTextGenerator
    {
        public Task<AiTextResponse> GenerateAsync(
            AiTextRequest request,
            CancellationToken cancellationToken) =>
            Task.FromException<AiTextResponse>(
                new AiGenerationException(AiErrorCode.ProviderUnavailable, "provider down"));
    }
}
