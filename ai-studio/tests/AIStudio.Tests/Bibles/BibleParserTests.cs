using AIStudio.Application.Bibles;
using Xunit;

namespace AIStudio.Tests.Bibles;

public sealed class BibleParserTests
{
    private const string ValidCharacterJson = """
        {
          "id": "rio",
          "version": 1,
          "displayName": "Rio",
          "identity": {
            "role": "protagonist",
            "species": "human",
            "hair": "short-black",
            "eyes": "brown",
            "distinguishingTraits": ["small scar above left eyebrow"]
          },
          "personalityTraits": ["curious", "persistent"],
          "baselineVariant": "default",
          "variants": [{ "id": "default" }, { "id": "school-uniform" }],
          "relationships": [{ "target": "hana", "type": "sibling" }],
          "assetReferences": [{ "assetId": "character-rio-front-v1", "purpose": "visual-reference" }]
        }
        """;

    private const string ValidWorldJson = """
        {
          "id": "rio-bedroom",
          "version": 1,
          "displayName": "Rio's Bedroom",
          "identity": {
            "environmentType": "bedroom",
            "visualDescription": "a small bedroom with a desk beside the window",
            "spatialTraits": ["single-window"]
          },
          "recurringProps": ["desk", "window"],
          "continuityRules": ["desk remains beside window"],
          "locations": [{ "id": "desk-area" }],
          "assetReferences": [{ "assetId": "environment-bedroom-reference", "purpose": "environment-reference" }]
        }
        """;

    [Fact]
    public void ParsesAValidCharacterBible()
    {
        var character = BibleParser.ParseCharacter(ValidCharacterJson);

        Assert.Equal("rio", character.Id.Value);
        Assert.Equal("short-black", character.Identity.Hair);
        Assert.Equal(["default", "school-uniform"], character.Variants.Select(variant => variant.Id));
    }

    [Fact]
    public void ParsesAValidWorldBible()
    {
        var world = BibleParser.ParseWorld(ValidWorldJson);

        Assert.Equal("rio-bedroom", world.Id.Value);
        Assert.Equal("bedroom", world.Identity.EnvironmentType);
        Assert.Equal(["desk", "window"], world.RecurringProps);
    }

    [Theory]
    [InlineData("")]
    [InlineData("{ not json")]
    [InlineData("[]")]
    public void RejectsMalformedJson(string json)
    {
        var character = Assert.Throws<BibleException>(() => BibleParser.ParseCharacter(json));
        var world = Assert.Throws<BibleException>(() => BibleParser.ParseWorld(json));

        Assert.Equal(BibleParserErrorCodes.InvalidJson, character.Code);
        Assert.Equal(BibleParserErrorCodes.InvalidJson, world.Code);
    }

    [Fact]
    public void RejectsUnknownTopLevelMembers()
    {
        var json = ValidCharacterJson.Insert(1, "\"assemblyName\":\"Evil.Type\",");

        var exception = Assert.Throws<BibleException>(() => BibleParser.ParseCharacter(json));

        Assert.Equal(BibleParserErrorCodes.InvalidJson, exception.Code);
    }

    [Fact]
    public void RejectsUnknownNestedMembers()
    {
        var json = ValidCharacterJson.Replace(
            "{ \"id\": \"default\" }",
            "{ \"id\": \"default\", \"command\": \"rm -rf /\" }");

        var exception = Assert.Throws<BibleException>(() => BibleParser.ParseCharacter(json));

        Assert.Equal(BibleParserErrorCodes.InvalidJson, exception.Code);
    }

    [Fact]
    public void RejectsStructurallyInvalidBible()
    {
        var exception = Assert.Throws<BibleException>(
            () => BibleParser.ParseCharacter("""{ "id": "rio" }"""));

        Assert.Equal(BibleParserErrorCodes.BibleInvalid, exception.Code);
    }

    [Fact]
    public void RejectsEmptyObject()
    {
        var character = Assert.Throws<BibleException>(() => BibleParser.ParseCharacter("{}"));
        var world = Assert.Throws<BibleException>(() => BibleParser.ParseWorld("{}"));

        Assert.Equal(BibleParserErrorCodes.BibleInvalid, character.Code);
        Assert.Equal(BibleParserErrorCodes.BibleInvalid, world.Code);
    }
}
