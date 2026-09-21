using AIStudio.Application.Concepts;
using AIStudio.Application.ProductionRecipes;
using Xunit;

namespace AIStudio.Tests.Concepts;

public sealed class ConceptRegistryTests
{
    [Fact]
    public void ListsRegisteredConceptsInDeterministicOrder()
    {
        var registry = new ConceptRegistry(SeedConcepts.All);

        Assert.Equal(
            ["local-ai-motion-comic", "local-ai-tech-explainer"],
            registry.Concepts.Select(concept => concept.Id.Value));
    }

    [Fact]
    public void RegisterThenRetrieveByStableIdAndVersion()
    {
        var registry = new ConceptRegistry();
        var concept = ConceptTestSupport.Concept("new-format");

        registry.Register(concept);

        Assert.True(registry.Contains(new ConceptId("new-format"), new ConceptVersion(1)));
        Assert.Equal("new-format", registry.Get(new ConceptId("new-format"), new ConceptVersion(1)).Id.Value);
        Assert.Equal("new-format", registry.GetLatest(new ConceptId("new-format")).Id.Value);
    }

    [Fact]
    public void GetLatestReturnsHighestVersion()
    {
        var v1 = ConceptTestSupport.Concept("evolving", version: 1);
        var v2 = ConceptTestSupport.Concept("evolving", version: 2);
        var registry = new ConceptRegistry([v2, v1]);

        Assert.Equal(2, registry.GetLatest(new ConceptId("evolving")).Version.Value);
        Assert.Equal(1, registry.Get(new ConceptId("evolving"), new ConceptVersion(1)).Version.Value);
    }

    [Fact]
    public void DuplicateIdAndVersionRegistrationIsRejected()
    {
        var registry = new ConceptRegistry([ConceptTestSupport.Concept("duplicate")]);

        Assert.Throws<InvalidOperationException>(
            () => registry.Register(ConceptTestSupport.Concept("duplicate")));
    }

    [Fact]
    public void InvalidConceptRegistrationIsRejected()
    {
        var registry = new ConceptRegistry();

        Assert.Throws<InvalidOperationException>(
            () => registry.Register(new ConceptManifest()));
    }

    [Fact]
    public void MissingConceptThrows()
    {
        var registry = new ConceptRegistry(SeedConcepts.All);

        Assert.Throws<KeyNotFoundException>(
            () => registry.GetLatest(new ConceptId("missing-concept")));
    }
}
