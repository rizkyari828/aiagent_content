using AIStudio.Application.Bibles;
using Xunit;

namespace AIStudio.Tests.Bibles;

/// <summary>
/// Proves the bible model is engine-neutral and category-agnostic: an anime
/// detective, a 3D preschool child, and a non-human robot mascot all use the SAME
/// <see cref="CharacterBible"/>, and a cyberpunk alley, a preschool playroom, and a
/// technical office all use the SAME <see cref="WorldBible"/>. No subclasses,
/// renderer-specific enums, or per-category C# types.
/// </summary>
public sealed class NewBibleCategoryWithoutNewCodeTests
{
    private const string AnimeDetectiveJson = """
        {
          "id": "detective-yuki",
          "version": 1,
          "displayName": "Yuki",
          "identity": {
            "role": "protagonist",
            "species": "human",
            "agePresentation": "teen",
            "hair": "violet-bob",
            "eyes": "amber",
            "distinguishingTraits": ["hair ribbon", "school badge"]
          },
          "personalityTraits": ["observant", "stubborn"],
          "baselineVariant": "default",
          "variants": [{ "id": "default" }, { "id": "school-uniform" }],
          "assetReferences": [
            { "assetId": "character-yuki-turnaround-v1", "purpose": "turnaround" }
          ]
        }
        """;

    private const string RobotMascotJson = """
        {
          "id": "bolt",
          "version": 1,
          "displayName": "Bolt",
          "identity": {
            "role": "mascot",
            "species": "robot",
            "bodyStyle": "rounded",
            "eyes": "led-blue"
          },
          "personalityTraits": ["eager"],
          "baselineVariant": "default",
          "relationships": [{ "target": "milo", "type": "companion" }],
          "assetReferences": [
            { "assetId": "character-bolt-model-v1", "purpose": "three-d-model" },
            { "assetId": "character-bolt-voice-v1", "purpose": "voice-reference" }
          ]
        }
        """;

    private static CharacterBible AnimeDetective() => BibleTestSupport.Character(
        id: "detective-yuki",
        role: "protagonist",
        species: "human",
        hair: "violet-bob",
        eyes: "amber",
        distinguishingTraits: ["hair ribbon", "school badge"],
        personalityTraits: ["observant", "stubborn"],
        variants: [BibleTestSupport.Variant("default"), BibleTestSupport.Variant("school-uniform")],
        assetReferences: [BibleTestSupport.Asset("character-yuki-turnaround-v1", "turnaround")]);

    private static CharacterBible PreschoolChild() => BibleTestSupport.Character(
        id: "milo",
        displayName: "Milo",
        role: "friend",
        species: "stylized-child",
        hair: "curly-brown",
        personalityTraits: ["cheerful"],
        baselineVariant: "colorful-overalls",
        variants: [BibleTestSupport.Variant("default"), BibleTestSupport.Variant("colorful-overalls")],
        assetReferences: [BibleTestSupport.Asset("character-milo-model-v1", "three-d-model")]);

    private static CharacterBible RobotMascot() => BibleTestSupport.Character(
        id: "bolt",
        displayName: "Bolt",
        role: "mascot",
        species: "robot",
        hair: null,
        eyes: "led-blue",
        personalityTraits: ["eager"],
        relationships: [BibleTestSupport.Relationship("milo", "companion")],
        assetReferences:
        [
            BibleTestSupport.Asset("character-bolt-model-v1", "three-d-model"),
            BibleTestSupport.Asset("character-bolt-voice-v1", "voice-reference")
        ]);

    [Fact]
    public void VeryDifferentCharactersUseTheSameModel()
    {
        var characters = new[] { AnimeDetective(), PreschoolChild(), RobotMascot() };

        foreach (var character in characters)
        {
            Assert.Empty(character.Validate());
        }

        var registry = new CharacterBibleRegistry(characters);

        Assert.Equal(3, registry.Characters.Count);
    }

    [Fact]
    public void VeryDifferentWorldsUseTheSameModel()
    {
        var worlds = new[]
        {
            BibleTestSupport.World(
                id: "cyberpunk-alley",
                environmentType: "urban-alley",
                visualDescription: "a rain-slick neon alley",
                spatialTraits: ["narrow", "neon-lit"],
                recurringProps: ["neon-sign", "dumpster"],
                continuityRules: ["neon sign stays above the door"]),
            BibleTestSupport.World(
                id: "preschool-playroom",
                environmentType: "playroom",
                visualDescription: "a bright playroom with a soft rug",
                recurringProps: ["toy-chest", "rug"]),
            BibleTestSupport.World(
                id: "technical-office",
                environmentType: "office",
                visualDescription: "a tidy office with a wide desk",
                recurringProps: ["desk", "monitor"])
        };

        foreach (var world in worlds)
        {
            Assert.Empty(world.Validate());
        }

        var registry = new WorldBibleRegistry(worlds);

        Assert.Equal(3, registry.Worlds.Count);
    }

    [Fact]
    public void NewCharacterCategoriesParseAsData()
    {
        var anime = BibleParser.ParseCharacter(AnimeDetectiveJson);
        var robot = BibleParser.ParseCharacter(RobotMascotJson);

        Assert.Equal("detective-yuki", anime.Id.Value);
        Assert.Equal("school-uniform", anime.Variants[1].Id);
        Assert.Equal("robot", robot.Identity.Species);
        Assert.Equal("companion", robot.Relationships[0].Type);
        Assert.Equal(
            ["three-d-model", "voice-reference"],
            robot.AssetReferences.Select(reference => reference.Purpose));
    }

    [Fact]
    public void DataDrivenTraitsRelationshipTypesAndAssetPurposesNeedNoEnum()
    {
        var character = BibleTestSupport.Character(
            personalityTraits: ["secretly-kind"],
            relationships: [BibleTestSupport.Relationship("mentor", "reluctant-mentor")],
            assetReferences:
            [
                BibleTestSupport.Asset("character-rio-rig-v1", "rig"),
                BibleTestSupport.Asset("character-rio-texture-v1", "texture"),
                BibleTestSupport.Asset("character-rio-hologram-v1", "hologram-reference")
            ]);

        Assert.Empty(character.Validate());
    }
}
