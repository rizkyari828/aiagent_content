using System.Text.Json;
using AIStudio.Tests.Bibles;
using Xunit;
using StoryContextModel = AIStudio.Application.StoryContext.StoryContext;

namespace AIStudio.Tests.StoryContext;

public sealed class StoryContextSerializationTests
{
    [Fact]
    public void JsonRoundTrips()
    {
        var context = StoryContextTestSupport.Builder()
            .Build(StoryContextTestSupport.Request(
                includeAssetReferences: true,
                characterStates: [BibleTestSupport.CharacterState(emotion: "excited")],
                worldStates: [BibleTestSupport.WorldState(timeOfDay: "night")]))
            .Context;

        var json = JsonSerializer.Serialize(context);
        var roundTripped = JsonSerializer.Deserialize<StoryContextModel>(json);

        Assert.NotNull(roundTripped);
        Assert.Equal(context.Concept.Id, roundTripped!.Concept.Id);
        Assert.Equal(context.Beats.Count, roundTripped.Beats.Count);
        Assert.Equal(context.Characters.Count, roundTripped.Characters.Count);
        Assert.Equal(["beat-01", "beat-02", "beat-03"], roundTripped.Beats.Select(beat => beat.Id.Value));
        Assert.Equal("excited", roundTripped.States!.Characters.Single().Emotion);
        Assert.Equal(json, JsonSerializer.Serialize(roundTripped));
    }

    [Fact]
    public void JsonContainsNoProviderPathsOrRegistryInternals()
    {
        var context = StoryContextTestSupport.Builder().Build(StoryContextTestSupport.Request()).Context;

        var json = JsonSerializer.Serialize(context);

        Assert.DoesNotContain("Infrastructure", json);
        Assert.DoesNotContain("http", json);
        Assert.DoesNotContain("recipeId", json);
        Assert.DoesNotContain("character-rio-front-v1", json);
    }

    [Fact]
    public void JsonExposesOnlySafeAssetMetadataWhenRequested()
    {
        var context = StoryContextTestSupport.Builder()
            .Build(StoryContextTestSupport.Request(includeAssetReferences: true))
            .Context;

        var json = JsonSerializer.Serialize(context);

        Assert.Contains("character-rio-front-v1", json);
        Assert.Contains("\"purpose\":\"visual-reference\"", json);
        Assert.DoesNotContain("\"path\"", json);
        Assert.DoesNotContain("\"url\"", json);
    }
}
