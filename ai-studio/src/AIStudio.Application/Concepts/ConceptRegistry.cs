namespace AIStudio.Application.Concepts;

/// <summary>
/// Deterministic in-memory concept registry. Registration validates structurally
/// and fails fast on an invalid manifest or a duplicate id + version, so a bad
/// concept cannot silently shadow a trusted one. It reads no files and touches no
/// database; concepts are data and cannot introduce executable behavior.
/// </summary>
public sealed class ConceptRegistry : IConceptRegistry
{
    private readonly object _gate = new();
    private readonly Dictionary<(ConceptId Id, ConceptVersion Version), ConceptManifest> _byVersion = [];
    private readonly Dictionary<ConceptId, ConceptManifest> _latest = [];

    public ConceptRegistry(IEnumerable<ConceptManifest>? concepts = null)
    {
        if (concepts is null)
        {
            return;
        }

        foreach (var concept in concepts)
        {
            Register(concept);
        }
    }

    public IReadOnlyList<ConceptManifest> Concepts
    {
        get
        {
            lock (_gate)
            {
                return _byVersion.Values
                    .OrderBy(concept => concept.Id.Value, StringComparer.Ordinal)
                    .ThenBy(concept => concept.Version.Value)
                    .ToList();
            }
        }
    }

    public void Register(ConceptManifest concept)
    {
        ArgumentNullException.ThrowIfNull(concept);

        var issues = ConceptValidator.Validate(concept);
        if (issues.Count > 0)
        {
            throw new InvalidOperationException(
                $"Concept '{concept.Id}' v{concept.Version.Value} is invalid: {issues[0].Code}.");
        }

        lock (_gate)
        {
            if (!_byVersion.TryAdd((concept.Id, concept.Version), concept))
            {
                throw new InvalidOperationException(
                    $"Duplicate concept registration '{concept.Id}' v{concept.Version.Value}.");
            }

            if (!_latest.TryGetValue(concept.Id, out var current)
                || concept.Version.Value > current.Version.Value)
            {
                _latest[concept.Id] = concept;
            }
        }
    }

    public bool Contains(ConceptId id, ConceptVersion version)
    {
        lock (_gate)
        {
            return _byVersion.ContainsKey((id, version));
        }
    }

    public ConceptManifest Get(ConceptId id, ConceptVersion version)
    {
        lock (_gate)
        {
            return _byVersion.TryGetValue((id, version), out var concept)
                ? concept
                : throw new KeyNotFoundException(
                    $"Concept '{id}' v{version.Value} is not registered.");
        }
    }

    public ConceptManifest GetLatest(ConceptId id)
    {
        lock (_gate)
        {
            return _latest.TryGetValue(id, out var concept)
                ? concept
                : throw new KeyNotFoundException($"Concept '{id}' is not registered.");
        }
    }
}
