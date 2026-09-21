using System.Diagnostics.CodeAnalysis;

namespace AIStudio.Application.Bibles;

/// <summary>
/// Trusted, in-memory catalog of world bibles keyed by stable id + version.
/// Registration validates structurally and fails fast on an invalid bible or a
/// duplicate id + version, so a bad environment cannot silently shadow a trusted
/// one. It is data only: a world bible can never register executable behavior,
/// resolve an asset, or load code. No database, cache, or hot reload in v1.
/// </summary>
public interface IWorldBibleRegistry
{
    /// <summary>All registered bibles, ordered deterministically by id then version.</summary>
    IReadOnlyList<WorldBible> Worlds { get; }

    /// <summary>Validates and registers a bible; rejects invalid or duplicate id + version.</summary>
    void Register(WorldBible world);

    bool Contains(WorldBibleId id, WorldBibleVersion version);

    /// <summary>The exact registered version; throws when it is not registered.</summary>
    WorldBible Get(WorldBibleId id, WorldBibleVersion version);

    /// <summary>The highest registered version for an id; throws when none is registered.</summary>
    WorldBible GetLatest(WorldBibleId id);

    /// <summary>Non-throwing exact lookup.</summary>
    bool TryGet(
        WorldBibleId id,
        WorldBibleVersion version,
        [NotNullWhen(true)] out WorldBible? world);

    /// <summary>Non-throwing latest lookup.</summary>
    bool TryGetLatest(WorldBibleId id, [NotNullWhen(true)] out WorldBible? world);
}
