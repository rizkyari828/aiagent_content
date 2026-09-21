namespace AIStudio.Application.Concepts;

/// <summary>
/// Trusted, in-memory catalog of concept manifests keyed by stable id + version.
/// It is the boundary a future AI planner would hand a validated manifest to; it
/// accepts declarative concept data only, validates it, and can never register an
/// executable capability or provider. No database, cache, file watcher, or hot
/// reload in v1.
/// </summary>
public interface IConceptRegistry
{
    /// <summary>All registered concepts, ordered deterministically by id then version.</summary>
    IReadOnlyList<ConceptManifest> Concepts { get; }

    /// <summary>Validates and registers a concept; rejects invalid or duplicate id + version.</summary>
    void Register(ConceptManifest concept);

    bool Contains(ConceptId id, ConceptVersion version);

    /// <summary>The exact registered version; throws when it is not registered.</summary>
    ConceptManifest Get(ConceptId id, ConceptVersion version);

    /// <summary>The highest registered version for an id; throws when none is registered.</summary>
    ConceptManifest GetLatest(ConceptId id);
}
