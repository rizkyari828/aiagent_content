using System.Text.Json;
using Xunit;

namespace AIStudio.Tests.StoryContext;

public sealed class StoryContextBuilderTests
{
    [Fact]
    public void BuildsFullStoryContext()
    {
        var result = StoryContextTestSupport.Builder().Build(StoryContextTestSupport.Request());

        Assert.True(result.IsValid);
        var context = result.Context;

        Assert.Equal("local-ai-tech-explainer", context.Concept.Id.Value);
        Assert.Equal("Run AI Locally Without API Fees", context.Concept.Title);
        Assert.Equal("developers", context.Concept.Audience);
        Assert.Equal("youtube-longform", context.Concept.Format);
        Assert.Equal("clean-tech", context.Concept.Style);
        Assert.Equal(60, context.Concept.Duration);

        Assert.Equal("problem-discovery-payoff", context.Treatment.StoryApproach);
        Assert.Equal("problem-solution-short", context.Story.Pattern.Value);
        Assert.Equal(60, context.Story.TargetDurationSeconds);

        Assert.Null(context.BeatScope);
        Assert.Null(context.States);
    }

    [Fact]
    public void ProjectsBeatsInOrderAndPreservesPurpose()
    {
        var context = StoryContextTestSupport.Builder().Build(StoryContextTestSupport.Request()).Context;

        Assert.Equal(["beat-01", "beat-02", "beat-03"], context.Beats.Select(beat => beat.Id.Value));
        Assert.Equal([1, 2, 3], context.Beats.Select(beat => beat.Order));
        Assert.Equal(["hook", "problem", "payoff"], context.Beats.Select(beat => beat.Role.Value));
        Assert.Equal(
            ["hook purpose", "problem purpose", "payoff purpose"],
            context.Beats.Select(beat => beat.Purpose));
        Assert.Equal([15, 25, 20], context.Beats.Select(beat => beat.DurationSeconds));
        Assert.Equal(["beat-01"], context.Beats[1].ContinuityFrom.Select(id => id.Value));
        Assert.Equal(["bedroom"], context.Beats[0].WorldRefs);
    }

    [Fact]
    public void TreatmentIsReusedAsContext()
    {
        var direction = StoryContextTestSupport.Direction();

        var context = StoryContextTestSupport.Builder()
            .Build(StoryContextTestSupport.Request(direction))
            .Context;

        Assert.Equal(direction.Treatment, context.Treatment);
    }

    [Fact]
    public void ProjectionIsDeterministic()
    {
        var builder = StoryContextTestSupport.Builder();

        var first = JsonSerializer.Serialize(builder.Build(StoryContextTestSupport.Request()).Context);
        var second = JsonSerializer.Serialize(builder.Build(StoryContextTestSupport.Request()).Context);

        Assert.Equal(first, second);
    }

    [Fact]
    public void ConceptProjectionOmitsRegistryInternals()
    {
        var context = StoryContextTestSupport.Builder().Build(StoryContextTestSupport.Request()).Context;

        var json = JsonSerializer.Serialize(context.Concept);

        Assert.DoesNotContain("recipeId", json);
        Assert.DoesNotContain("recipeVersion", json);
        Assert.DoesNotContain("tags", json);
        Assert.DoesNotContain("description", json);
    }
}
