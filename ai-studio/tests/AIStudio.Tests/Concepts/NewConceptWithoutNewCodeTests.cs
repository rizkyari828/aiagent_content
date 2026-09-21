using System.Text.Json;
using AIStudio.Application.Concepts;
using Xunit;

namespace AIStudio.Tests.Concepts;

/// <summary>
/// Proves the central product principle: a new content concept with new format and
/// style values is DATA. It needs no new enum, subtype, handler, workflow, or
/// concept-registry change, and it can reference an existing trusted recipe.
/// </summary>
public sealed class NewConceptWithoutNewCodeTests
{
    private const string AnimeConceptJson = """
        {
          "id": "local-ai-anime-tech-short",
          "version": 1,
          "title": "Local AI, Anime Style",
          "description": "Anime-styled short about local AI.",
          "audience": "developers",
          "format": "anime-short",
          "style": "anime-cinematic",
          "recipeId": "tech-explainer",
          "recipeVersion": 1,
          "duration": 45,
          "tags": ["local-ai", "anime"]
        }
        """;

    [Fact]
    public void NewFormatAndStyleAreJustData()
    {
        var concept = JsonSerializer.Deserialize<ConceptManifest>(AnimeConceptJson);

        Assert.NotNull(concept);
        Assert.Equal("anime-short", concept!.Format);
        Assert.Equal("anime-cinematic", concept.Style);
        Assert.Empty(concept.Validate());
    }

    [Fact]
    public void NewConceptRegistersAndResolvesWithoutChangingTheRegistry()
    {
        var concept = JsonSerializer.Deserialize<ConceptManifest>(AnimeConceptJson)!;

        var registry = new ConceptRegistry(SeedConcepts.All);
        registry.Register(concept);

        var resolution = ConceptTestSupport.Resolver().Resolve(
            registry.Get(new ConceptId("local-ai-anime-tech-short"), new ConceptVersion(1)));

        Assert.Equal(ConceptResolutionStatus.Ready, resolution.Status);
        Assert.Equal("tech-explainer", resolution.RecipeId.Value);
    }

    [Fact]
    public void CleanJsonShapeUsesPlainScalars()
    {
        var concept = JsonSerializer.Deserialize<ConceptManifest>(AnimeConceptJson)!;

        var json = JsonSerializer.Serialize(concept);

        Assert.Contains("\"id\":\"local-ai-anime-tech-short\"", json);
        Assert.Contains("\"format\":\"anime-short\"", json);
        Assert.Contains("\"recipeId\":\"tech-explainer\"", json);
        Assert.Contains("\"recipeVersion\":1", json);
    }
}
