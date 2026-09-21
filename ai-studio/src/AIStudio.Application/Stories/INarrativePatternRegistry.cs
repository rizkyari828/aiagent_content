using System.Diagnostics.CodeAnalysis;

namespace AIStudio.Application.Stories;

/// <summary>
/// Trusted, in-memory catalog of reusable narrative patterns keyed by stable id +
/// version. A pattern is declarative data: it validates structurally and registers
/// without any C# type, enum, or director subclass. No database, cache, hot reload,
/// or dynamic executable plugin in v1 — a new pattern is added as data, not by
/// loading code.
/// </summary>
public interface INarrativePatternRegistry
{
    /// <summary>All registered patterns, ordered deterministically by id then version.</summary>
    IReadOnlyList<NarrativePattern> Patterns { get; }

    /// <summary>Validates and registers a pattern; rejects invalid or duplicate id + version.</summary>
    void Register(NarrativePattern pattern);

    bool Contains(NarrativePatternId id, NarrativePatternVersion version);

    /// <summary>The exact registered version; throws when it is not registered.</summary>
    NarrativePattern Get(NarrativePatternId id, NarrativePatternVersion version);

    /// <summary>The highest registered version for an id; throws when none is registered.</summary>
    NarrativePattern GetLatest(NarrativePatternId id);

    /// <summary>Non-throwing exact lookup.</summary>
    bool TryGet(
        NarrativePatternId id,
        NarrativePatternVersion version,
        [NotNullWhen(true)] out NarrativePattern? pattern);

    /// <summary>Non-throwing latest lookup.</summary>
    bool TryGetLatest(NarrativePatternId id, [NotNullWhen(true)] out NarrativePattern? pattern);
}
