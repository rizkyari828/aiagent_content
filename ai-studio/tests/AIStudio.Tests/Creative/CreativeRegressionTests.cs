using AIStudio.Application.Creative;
using Xunit;

namespace AIStudio.Tests.Creative;

/// <summary>
/// Guards the authoritative boundary: the Solution Idea / GenerateIdea layer
/// decides WHAT idea is worth making; the Creative Director only decides HOW an
/// approved idea is expressed. These tests prove the approved idea stays the
/// authoritative input and survives into the prompt and resulting data.
/// </summary>
public sealed class CreativeRegressionTests
{
    [Fact]
    public async Task ApprovedIdeaIsAuthoritativeInputAndSurvivesIntoThePrompt()
    {
        var ai = new FakeAiTextGenerator();
        var director = CreativeTestSupport.Director(ai);
        var idea = CreativeTestSupport.Idea();

        var result = await director.DirectAsync(
            idea,
            options: null,
            TestContext.Current.CancellationToken);

        var prompt = ai.Requests[0].Prompt;
        Assert.Contains("Run AI locally", prompt);
        Assert.Contains("Stop paying API fees", prompt);
        Assert.Contains("developers", prompt);
        Assert.Contains("educational", prompt);
        Assert.Contains("Do not invent a new topic", prompt);

        // The approved idea object is never mutated by the director.
        Assert.Equal("Run AI locally", idea.Topic);
        Assert.Equal("Stop paying API fees", idea.Angle);

        // The direction retains the approved audience and idea trace.
        Assert.Equal("developers", result.Direction.Concept.Audience);
        Assert.Equal("idea-123", result.Direction.IdeaReference);
    }

    [Fact]
    public async Task CreativeDirectorMayChangeFormatAndStyleButNotTheAudience()
    {
        var ai = new FakeAiTextGenerator();
        var director = CreativeTestSupport.Director(ai);

        var result = await director.DirectAsync(
            CreativeTestSupport.Idea(),
            options: null,
            TestContext.Current.CancellationToken);

        // HOW changed: the director chose an anime short.
        Assert.Equal("anime-short", result.Direction.Concept.Format);
        Assert.Equal("anime-cinematic", result.Direction.Concept.Style);

        // WHAT stayed authoritative: the approved audience is preserved.
        Assert.Equal("developers", result.Direction.Concept.Audience);
    }
}
