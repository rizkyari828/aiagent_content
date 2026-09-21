using AIStudio.Application.Stories;
using Xunit;

namespace AIStudio.Tests.Stories;

/// <summary>
/// Guards the pipeline boundary: the Creative Director owns format, style, recipe
/// and creative treatment; the Story Director only consumes them to build narrative
/// progression. Story data stays declarative narrative intent — never executable
/// instructions, dialogue, camera, or provider detail.
/// </summary>
public sealed class StoryBoundaryRegressionTests
{
    [Fact]
    public void StoryPlanReferencesButDoesNotRedefineConceptIdentity()
    {
        var direction = StoryTestSupport.Direction(conceptId: "my-concept", duration: 60);

        var plan = new StoryDirector(new NarrativePatternRegistry(SeedNarrativePatterns.All))
            .Direct(StoryTestSupport.Request(direction, targetDuration: 60))
            .Plan;

        Assert.Equal("my-concept", plan.SourceConceptId.Value);
        Assert.Equal("my-concept-story", plan.Id.Value);
    }

    [Fact]
    public void DirectorDoesNotMutateTheSourceCreativeDirection()
    {
        var direction = StoryTestSupport.Direction();
        var originalAudience = direction.Concept.Audience;
        var originalFormat = direction.Concept.Format;
        var originalStyle = direction.Concept.Style;
        var originalRecipe = direction.Concept.RecipeId;

        _ = new StoryDirector(new NarrativePatternRegistry(SeedNarrativePatterns.All))
            .Direct(StoryTestSupport.Request(direction, targetDuration: 60));

        Assert.Equal(originalAudience, direction.Concept.Audience);
        Assert.Equal(originalFormat, direction.Concept.Format);
        Assert.Equal(originalStyle, direction.Concept.Style);
        Assert.Equal(originalRecipe, direction.Concept.RecipeId);
        Assert.Equal("cinematic developer struggling with API cost", direction.Treatment.HookTreatment);
    }

    [Fact]
    public void StoryPlanDoesNotOwnCreativeDirectorConcerns()
    {
        var properties = typeof(StoryPlan).GetProperties().Select(property => property.Name).ToList();

        Assert.DoesNotContain("Audience", properties);
        Assert.DoesNotContain("Format", properties);
        Assert.DoesNotContain("Style", properties);
        Assert.DoesNotContain("RecipeId", properties);
        Assert.DoesNotContain("Treatment", properties);
    }

    [Fact]
    public void StoryBeatCarriesNarrativeIntentNotProductionDetail()
    {
        var names = typeof(StoryBeat)
            .GetProperties()
            .Select(property => property.Name.ToLowerInvariant());

        foreach (var forbidden in new[] { "script", "dialogue", "camera", "engine", "prompt", "command", "shader", "manim", "comfy", "blender" })
        {
            Assert.DoesNotContain(forbidden, names);
        }
    }

    [Fact]
    public void DirectionRemainsTheAuthoritativeInputToSerialization()
    {
        var direction = StoryTestSupport.Direction();

        _ = new StoryDirector(new NarrativePatternRegistry(SeedNarrativePatterns.All))
            .Direct(StoryTestSupport.Request(direction, targetDuration: 60));

        Assert.Equal("developers", direction.Concept.Audience);
    }
}
