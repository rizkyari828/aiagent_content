using System.Text.Json;
using AIStudio.Application.Bibles;
using Xunit;

namespace AIStudio.Tests.Bibles;

public sealed class BibleSerializationTests
{
    [Fact]
    public void CharacterBibleRoundTripsThroughJson()
    {
        var character = BibleTestSupport.Character(
            distinguishingTraits: ["small scar above left eyebrow"],
            personalityTraits: ["curious", "persistent"],
            variants: [BibleTestSupport.Variant("default"), BibleTestSupport.Variant("winter-jacket")],
            relationships: [BibleTestSupport.Relationship("hana", "sibling")],
            assetReferences: [BibleTestSupport.Asset("character-rio-front-v1", "visual-reference")]);

        var json = JsonSerializer.Serialize(character);
        var roundTripped = JsonSerializer.Deserialize<CharacterBible>(json);

        Assert.NotNull(roundTripped);
        Assert.Equal(character.Id, roundTripped!.Id);
        Assert.Equal(character.Version, roundTripped.Version);
        Assert.Equal("short-black", roundTripped.Identity.Hair);
        Assert.Equal(["curious", "persistent"], roundTripped.PersonalityTraits);
        Assert.Equal(["default", "winter-jacket"], roundTripped.Variants.Select(variant => variant.Id));
        Assert.Equal("hana", roundTripped.Relationships[0].Target.Value);
        Assert.Equal("sibling", roundTripped.Relationships[0].Type);
        Assert.Equal("character-rio-front-v1", roundTripped.AssetReferences[0].AssetId.Value);
        Assert.Empty(roundTripped.Validate());
    }

    [Fact]
    public void WorldBibleRoundTripsThroughJson()
    {
        var world = BibleTestSupport.World(
            recurringProps: ["desk", "window"],
            continuityRules: ["desk remains beside window"],
            locations: [BibleTestSupport.Location("desk-area")],
            assetReferences: [BibleTestSupport.Asset("environment-bedroom-reference", "environment-reference")]);

        var json = JsonSerializer.Serialize(world);
        var roundTripped = JsonSerializer.Deserialize<WorldBible>(json);

        Assert.NotNull(roundTripped);
        Assert.Equal("bedroom", roundTripped!.Identity.EnvironmentType);
        Assert.Equal(["desk", "window"], roundTripped.RecurringProps);
        Assert.Equal(["desk remains beside window"], roundTripped.ContinuityRules);
        Assert.Equal("desk-area", roundTripped.Locations[0].Id);
        Assert.Equal("environment-bedroom-reference", roundTripped.AssetReferences[0].AssetId.Value);
        Assert.Empty(roundTripped.Validate());
    }

    [Fact]
    public void CharacterStateRoundTripsThroughJson()
    {
        var state = BibleTestSupport.CharacterState(
            emotion: "excited",
            worldRef: "rio-bedroom",
            heldProps: ["laptop"]);

        var json = JsonSerializer.Serialize(state);
        var roundTripped = JsonSerializer.Deserialize<CharacterState>(json);

        Assert.NotNull(roundTripped);
        Assert.Equal("rio", roundTripped!.CharacterRef.Value);
        Assert.Equal("excited", roundTripped.Emotion);
        Assert.Equal("rio-bedroom", roundTripped.WorldRef!.Value.Value);
        Assert.Equal(["laptop"], roundTripped.HeldProps);
        Assert.Empty(roundTripped.Validate());
    }

    [Fact]
    public void CharacterBibleUsesCleanScalars()
    {
        var json = JsonSerializer.Serialize(BibleTestSupport.Character());

        Assert.Contains("\"id\":\"rio\"", json);
        Assert.Contains("\"version\":1", json);
        Assert.Contains("\"role\":\"protagonist\"", json);
        Assert.Contains("\"species\":\"human\"", json);
        Assert.Contains("\"baselineVariant\":\"default\"", json);
    }
}
