using AIStudio.Application.AI;
using AIStudio.Application.Concepts;
using AIStudio.Application.Creative;
using AIStudio.Tests.Concepts;
using Xunit;

namespace AIStudio.Tests.Creative;

public sealed class CreativeDirectorTests
{
    [Fact]
    public async Task CallsTheAiBoundaryOnceAndReturnsAValidatedDirection()
    {
        var ai = new FakeAiTextGenerator();
        var director = CreativeTestSupport.Director(ai);

        var result = await director.DirectAsync(
            CreativeTestSupport.Idea(),
            options: null,
            TestContext.Current.CancellationToken);

        Assert.Equal(1, ai.CallCount);
        Assert.Equal("fake-model", result.Model);
        Assert.Equal("run-ai-locally-anime-short", result.Direction.Concept.Id.Value);
        Assert.Equal(AiResponseFormat.JsonObject, ai.Requests[0].ResponseFormat);
        Assert.False(ai.Requests[0].Think);
    }

    [Fact]
    public async Task ApprovedIdeaGuardrailsOverrideUntrustedModelData()
    {
        var misbehaving = CreativeTestSupport.ValidDirectionJson
            .Replace("\"developers\"", "\"kids\"")
            .Replace("\"idea-123\"", "\"other-idea\"");
        var ai = new FakeAiTextGenerator
        {
            Response = new AiTextResponse(misbehaving, "fake-model", null, null, null)
        };
        var director = CreativeTestSupport.Director(ai);

        var result = await director.DirectAsync(
            CreativeTestSupport.Idea(),
            options: null,
            TestContext.Current.CancellationToken);

        Assert.Equal("developers", result.Direction.Concept.Audience);
        Assert.Equal("idea-123", result.Direction.IdeaReference);
    }

    [Fact]
    public async Task InvalidIdeaIsRejectedBeforeCallingTheModel()
    {
        var ai = new FakeAiTextGenerator();
        var director = CreativeTestSupport.Director(ai);

        var exception = await Assert.ThrowsAsync<CreativeDirectionException>(
            () => director.DirectAsync(new ApprovedIdea(), null, TestContext.Current.CancellationToken));

        Assert.Equal(CreativeIssueCodes.IdeaInvalid, exception.Code);
        Assert.Equal(0, ai.CallCount);
    }

    [Fact]
    public async Task DirectedConceptResolvesThroughTheExistingConceptResolver()
    {
        var director = CreativeTestSupport.Director(new FakeAiTextGenerator());

        var result = await director.DirectAsync(
            CreativeTestSupport.Idea(),
            options: null,
            TestContext.Current.CancellationToken);

        var conceptResolver = ConceptTestSupport.Resolver();
        var resolution = conceptResolver.Resolve(result.Direction.Concept);

        Assert.Equal(ConceptResolutionStatus.Ready, resolution.Status);
    }

    [Fact]
    public async Task ResolutionSelectsProvidersWithoutExecutingThem()
    {
        var director = CreativeTestSupport.Director(new FakeAiTextGenerator());

        var result = await director.DirectAsync(
            CreativeTestSupport.Idea(),
            options: null,
            TestContext.Current.CancellationToken);

        // The registries are descriptor-only, so a successful resolution proves no
        // provider implementation was constructed or executed.
        var resolution = ConceptTestSupport.Resolver().Resolve(result.Direction.Concept);

        Assert.True(resolution.IsProducible);
        Assert.All(resolution.RecipeResolution!.Requirements, requirement =>
            Assert.NotNull(requirement.Capability.Provider));
    }
}
