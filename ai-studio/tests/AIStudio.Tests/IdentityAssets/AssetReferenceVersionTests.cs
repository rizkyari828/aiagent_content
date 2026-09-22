using System.Text.Json;
using AIStudio.Application.Bibles;
using AIStudio.Application.IdentityAssets;
using Xunit;

namespace AIStudio.Tests.IdentityAssets;

public sealed class AssetReferenceVersionTests
{
    [Fact]
    public void OldBibleJsonWithoutVersionParsesAsFloatingReference()
    {
        var bible = BibleParser.ParseCharacter(CharacterJson(
            """{ "assetId": "student-ref", "purpose": "character-primary-reference" }"""));

        Assert.Null(bible.AssetReferences.Single().Version);
    }

    [Fact]
    public void ExplicitNullVersionParsesAsFloatingReference()
    {
        var bible = BibleParser.ParseCharacter(CharacterJson(
            """{ "assetId": "student-ref", "version": null, "purpose": "character-primary-reference" }"""));

        Assert.Null(bible.AssetReferences.Single().Version);
    }

    [Fact]
    public void PositiveVersionParsesAsPinnedReference()
    {
        var bible = BibleParser.ParseCharacter(CharacterJson(
            """{ "assetId": "student-ref", "version": 2, "purpose": "character-primary-reference" }"""));

        Assert.Equal(2, bible.AssetReferences.Single().Version!.Value.Value);
    }

    [Fact]
    public void InvalidVersionIsRejected()
    {
        var exception = Assert.Throws<BibleException>(
            () => BibleParser.ParseCharacter(CharacterJson(
                """{ "assetId": "student-ref", "version": 0, "purpose": "character-primary-reference" }""")));

        Assert.Equal(BibleParserErrorCodes.InvalidJson, exception.Code);
    }

    [Fact]
    public void FloatingReferenceSerializationOmitsVersion()
    {
        var json = JsonSerializer.Serialize(IdentityAssetTestSupport.Reference());

        Assert.DoesNotContain("\"version\"", json);
    }

    private static string CharacterJson(string assetReference) =>
        $$"""
          {
            "id": "student",
            "version": 1,
            "displayName": "Student",
            "identity": { "role": "protagonist" },
            "personalityTraits": [],
            "baselineVariant": "default",
            "variants": [],
            "relationships": [],
            "assetReferences": [{{assetReference}}]
          }
          """;
}
