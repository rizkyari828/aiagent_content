using AIStudio.Application.Bibles;
using Xunit;

namespace AIStudio.Tests.Bibles;

/// <summary>
/// Proves the central principle: stable identity and mutable scene state are
/// separate. Scene state may change freely (emotion, location, approved variant)
/// while the character/world identity stays the same and recognizable.
/// </summary>
public sealed class IdentityStateSeparationTests
{
    [Fact]
    public void StableCharacterIdentitySurvivesSceneStateChanges()
    {
        var rio = BibleTestSupport.Character(
            hair: "short-black",
            eyes: "brown",
            distinguishingTraits: ["small scar above left eyebrow"]);

        var sceneA = BibleTestSupport.CharacterState(emotion: "tired", worldRef: "rio-bedroom");
        var sceneB = BibleTestSupport.CharacterState(emotion: "excited", worldRef: "technical-office");

        Assert.Empty(sceneA.Validate(rio));
        Assert.Empty(sceneB.Validate(rio));

        Assert.Equal(rio.Id, sceneA.CharacterRef);
        Assert.Equal(rio.Id, sceneB.CharacterRef);
        Assert.NotEqual(sceneA.Emotion, sceneB.Emotion);
        Assert.NotEqual(sceneA.WorldRef, sceneB.WorldRef);

        // Identity is untouched by scene state.
        Assert.Equal("short-black", rio.Identity.Hair);
        Assert.Equal("brown", rio.Identity.Eyes);
        Assert.Equal(["small scar above left eyebrow"], rio.Identity.DistinguishingTraits);
    }

    [Fact]
    public void ApprovedVariantUseDoesNotCreateANewCharacter()
    {
        var rio = BibleTestSupport.Character(
            variants: [BibleTestSupport.Variant("default"), BibleTestSupport.Variant("winter-jacket")]);
        var registry = new CharacterBibleRegistry([rio]);

        var winterState = BibleTestSupport.CharacterState(variant: "winter-jacket");
        Assert.Empty(winterState.Validate(rio));
        Assert.True(rio.IsVariantAllowed("winter-jacket"));

        // Still exactly one logical character with the same id.
        Assert.Single(registry.Characters);
        Assert.Equal("rio", registry.GetLatest(new CharacterBibleId("rio")).Id.Value);
    }

    [Fact]
    public void UnknownVariantIsRejectedWithoutMutatingIdentity()
    {
        var rio = BibleTestSupport.Character(
            variants: [BibleTestSupport.Variant("default"), BibleTestSupport.Variant("winter-jacket")]);

        var unknown = BibleTestSupport.CharacterState(variant: "space-suit");

        Assert.Contains(
            BibleIssueCodes.CharacterStateVariantUnknown,
            unknown.Validate(rio).Select(issue => issue.Code));
        Assert.False(rio.IsVariantAllowed("space-suit"));
        Assert.Equal(2, rio.Variants.Count);
    }

    [Fact]
    public void StableWorldIdentitySurvivesMutableStateChanges()
    {
        var bedroom = BibleTestSupport.World(
            environmentType: "bedroom",
            recurringProps: ["desk", "gaming-pc", "window"],
            continuityRules: ["desk remains beside window"]);

        var morning = BibleTestSupport.WorldState(timeOfDay: "morning", lighting: "on");
        var nightStorm = BibleTestSupport.WorldState(
            timeOfDay: "night",
            weather: "rain",
            temporaryProps: ["umbrella"],
            notes: "messy desk");

        Assert.Empty(morning.Validate());
        Assert.Empty(nightStorm.Validate());

        // Identity is untouched by mutable state.
        Assert.Equal("bedroom", bedroom.Identity.EnvironmentType);
        Assert.Equal(["desk", "gaming-pc", "window"], bedroom.RecurringProps);
        Assert.Equal(["desk remains beside window"], bedroom.ContinuityRules);
    }

    [Fact]
    public void CharacterVersionEvolutionStaysTheSameLogicalCharacter()
    {
        var registry = new CharacterBibleRegistry(
        [
            BibleTestSupport.Character("rio", version: 1, hair: "short-black"),
            BibleTestSupport.Character("rio", version: 2, hair: "short-black-undercut")
        ]);

        // Both versions are the same logical character id.
        Assert.Equal(2, registry.Characters.Count);
        Assert.All(registry.Characters, character => Assert.Equal("rio", character.Id.Value));
        Assert.Equal("short-black-undercut", registry.GetLatest(new CharacterBibleId("rio")).Identity.Hair);
        Assert.Equal("short-black", registry.Get(new CharacterBibleId("rio"), new CharacterBibleVersion(1)).Identity.Hair);
    }
}
