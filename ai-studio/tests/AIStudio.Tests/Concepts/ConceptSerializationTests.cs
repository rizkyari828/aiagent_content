using System.Text.Json;
using AIStudio.Application.Concepts;
using Xunit;

namespace AIStudio.Tests.Concepts;

public sealed class ConceptSerializationTests
{
    [Fact]
    public void ConceptRoundTripsThroughPlainSystemTextJson()
    {
        var concept = SeedConcepts.All.Single(c => c.Id.Value == "local-ai-tech-explainer");

        var json = JsonSerializer.Serialize(concept);
        var restored = JsonSerializer.Deserialize<ConceptManifest>(json);

        Assert.NotNull(restored);
        Assert.Equal(concept.Id, restored!.Id);
        Assert.Equal(concept.Version, restored.Version);
        Assert.Equal(concept.Title, restored.Title);
        Assert.Equal(concept.Audience, restored.Audience);
        Assert.Equal(concept.Format, restored.Format);
        Assert.Equal(concept.Style, restored.Style);
        Assert.Equal(concept.RecipeId, restored.RecipeId);
        Assert.Equal(concept.RecipeVersion, restored.RecipeVersion);
        Assert.Equal(concept.Duration, restored.Duration);
        Assert.Equal(concept.Tags, restored.Tags);
        Assert.Empty(restored.Validate());
    }

    [Fact]
    public void OmittedRecipeVersionRoundTripsAsNullMeaningLatest()
    {
        var concept = SeedConcepts.All.Single(c => c.Id.Value == "local-ai-motion-comic");

        var json = JsonSerializer.Serialize(concept);
        var restored = JsonSerializer.Deserialize<ConceptManifest>(json);

        Assert.NotNull(restored);
        Assert.Null(restored!.RecipeVersion);
        Assert.Empty(restored.Validate());
    }
}
