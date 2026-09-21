using System.Text.Json;
using AIStudio.Application.Bibles;
using AIStudio.Tests.Bibles;
using Xunit;

namespace AIStudio.Tests.StoryContext;

public sealed class StoryContextStateAndImmutabilityTests
{
    [Fact]
    public void StateIsProjectedSeparatelyFromIdentity()
    {
        var characterState = BibleTestSupport.CharacterState(emotion: "excited", pose: "standing", worldRef: "bedroom");
        var worldState = BibleTestSupport.WorldState(timeOfDay: "night", weather: "rain");

        var context = StoryContextTestSupport.Builder()
            .Build(StoryContextTestSupport.Request(
                characterStates: [characterState],
                worldStates: [worldState]))
            .Context;

        Assert.NotNull(context.States);
        Assert.Equal("excited", context.States!.Characters.Single().Emotion);
        Assert.Equal("night", context.States.Worlds.Single().TimeOfDay);

        // Stable identity is untouched and carries no mutable state.
        var rio = context.Characters.Single(character => character.Id.Value == "rio");
        Assert.Equal("short-black", rio.Identity.Hair);
        Assert.Null(typeof(CharacterIdentity).GetProperty("Emotion"));
        Assert.Null(typeof(CharacterIdentity).GetProperty("Pose"));
    }

    [Fact]
    public void NoStateIsProjectedWhenNoneIsSupplied()
    {
        var context = StoryContextTestSupport.Builder().Build(StoryContextTestSupport.Request()).Context;

        Assert.Null(context.States);
    }

    [Fact]
    public void SourceModelsAreNotMutated()
    {
        var direction = StoryContextTestSupport.Direction();
        var plan = StoryContextTestSupport.ThreeBeatPlan();
        var characters = StoryContextTestSupport.Characters();
        var worlds = StoryContextTestSupport.Worlds();

        var directionBefore = JsonSerializer.Serialize(direction);
        var planBefore = JsonSerializer.Serialize(plan);
        var rioBefore = JsonSerializer.Serialize(characters.GetLatest(new CharacterBibleId("rio")));
        var bedroomBefore = JsonSerializer.Serialize(worlds.GetLatest(new WorldBibleId("bedroom")));

        _ = StoryContextTestSupport.Builder(characters, worlds).Build(
            StoryContextTestSupport.Request(
                direction,
                plan,
                includeAssetReferences: true,
                characterStates: [BibleTestSupport.CharacterState(emotion: "excited")]));

        Assert.Equal(directionBefore, JsonSerializer.Serialize(direction));
        Assert.Equal(planBefore, JsonSerializer.Serialize(plan));
        Assert.Equal(rioBefore, JsonSerializer.Serialize(characters.GetLatest(new CharacterBibleId("rio"))));
        Assert.Equal(bedroomBefore, JsonSerializer.Serialize(worlds.GetLatest(new WorldBibleId("bedroom"))));
    }

    [Fact]
    public void RegistriesAreNotMutated()
    {
        var characters = StoryContextTestSupport.Characters();
        var worlds = StoryContextTestSupport.Worlds();

        var characterCount = characters.Characters.Count;
        var worldCount = worlds.Worlds.Count;
        var rioBefore = characters.GetLatest(new CharacterBibleId("rio"));

        _ = StoryContextTestSupport.Builder(characters, worlds).Build(
            StoryContextTestSupport.Request());

        Assert.Equal(characterCount, characters.Characters.Count);
        Assert.Equal(worldCount, worlds.Worlds.Count);
        Assert.Equal(rioBefore, characters.GetLatest(new CharacterBibleId("rio")));
    }
}
