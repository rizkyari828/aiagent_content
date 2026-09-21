using AIStudio.Application.Creative;
using Xunit;

namespace AIStudio.Tests.Creative;

public sealed class CreativeDirectionParserTests
{
    [Fact]
    public void ParsesValidJsonIntoCreativeDirection()
    {
        var direction = CreativeDirectionParser.Parse(CreativeTestSupport.ValidDirectionJson);

        Assert.Equal("idea-123", direction.IdeaReference);
        Assert.Equal("run-ai-locally-anime-short", direction.Concept.Id.Value);
        Assert.Equal("anime-short", direction.Concept.Format);
        Assert.Equal("anime-cinematic", direction.Concept.Style);
        Assert.Equal("motion-comic", direction.Concept.RecipeId.Value);
        Assert.Equal("problem-discovery-payoff", direction.Treatment.StoryApproach);
        Assert.Empty(direction.Concept.Validate());
        Assert.Empty(direction.Treatment.Validate());
    }

    [Fact]
    public void RejectsMalformedJson()
    {
        var exception = Assert.Throws<CreativeDirectionException>(
            () => CreativeDirectionParser.Parse("{ not json"));

        Assert.Equal(CreativeIssueCodes.DirectionInvalidJson, exception.Code);
    }

    [Fact]
    public void RejectsEmptyResponse()
    {
        var exception = Assert.Throws<CreativeDirectionException>(
            () => CreativeDirectionParser.Parse("   "));

        Assert.Equal(CreativeIssueCodes.DirectionInvalidJson, exception.Code);
    }

    [Fact]
    public void RejectsMissingConcept()
    {
        var json = """{"treatment":{"storyApproach":"a","hookTreatment":"b","pacing":"c","visualStrategy":"d","endingTreatment":"e"}}""";

        var exception = Assert.Throws<CreativeDirectionException>(
            () => CreativeDirectionParser.Parse(json));

        Assert.Equal(CreativeIssueCodes.DirectionMissingConcept, exception.Code);
    }

    [Fact]
    public void RejectsMissingTreatment()
    {
        var json = """{"concept":{"id":"c","version":1,"title":"t","format":"youtube-short","style":"clean-tech","recipeId":"tech-explainer","duration":45}}""";

        var exception = Assert.Throws<CreativeDirectionException>(
            () => CreativeDirectionParser.Parse(json));

        Assert.Equal(CreativeIssueCodes.DirectionMissingTreatment, exception.Code);
    }

    [Fact]
    public void RejectsMissingRequiredConceptFieldsUsingExistingValidation()
    {
        var json = """{"concept":{"id":"c","version":1,"format":"youtube-short","style":"clean-tech","recipeId":"tech-explainer","duration":45},"treatment":{"storyApproach":"a","hookTreatment":"b","pacing":"c","visualStrategy":"d","endingTreatment":"e"}}""";

        var exception = Assert.Throws<CreativeDirectionException>(
            () => CreativeDirectionParser.Parse(json));

        Assert.Equal(CreativeIssueCodes.DirectionInvalidConcept, exception.Code);
    }

    [Fact]
    public void RejectsMissingRequiredTreatmentFields()
    {
        var json = """{"concept":{"id":"c","version":1,"title":"t","format":"youtube-short","style":"clean-tech","recipeId":"tech-explainer","duration":45},"treatment":{"storyApproach":"a"}}""";

        var exception = Assert.Throws<CreativeDirectionException>(
            () => CreativeDirectionParser.Parse(json));

        Assert.Equal(CreativeIssueCodes.DirectionInvalidTreatment, exception.Code);
    }

    [Fact]
    public void UnknownRecipeIsStillRepresentable()
    {
        var json = """{"concept":{"id":"c","version":1,"title":"t","format":"anime-short","style":"anime-cinematic","recipeId":"future-anime-video","duration":45},"treatment":{"storyApproach":"a","hookTreatment":"b","pacing":"c","visualStrategy":"d","endingTreatment":"e"}}""";

        var direction = CreativeDirectionParser.Parse(json);

        Assert.Equal("future-anime-video", direction.Concept.RecipeId.Value);
        Assert.Equal("anime-cinematic", direction.Concept.Style);
    }
}
