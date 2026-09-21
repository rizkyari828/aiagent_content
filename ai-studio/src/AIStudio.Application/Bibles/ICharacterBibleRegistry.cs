using System.Diagnostics.CodeAnalysis;

namespace AIStudio.Application.Bibles;

/// <summary>
/// Trusted, in-memory catalog of character bibles keyed by stable id + version.
/// Registration validates structurally and fails fast on an invalid bible or a
/// duplicate id + version, so a bad identity cannot silently shadow a trusted one.
/// It is data only: a character bible can never register executable behavior,
/// resolve an asset, or load code. No database, cache, or hot reload in v1.
/// </summary>
public interface ICharacterBibleRegistry
{
    /// <summary>All registered bibles, ordered deterministically by id then version.</summary>
    IReadOnlyList<CharacterBible> Characters { get; }

    /// <summary>Validates and registers a bible; rejects invalid or duplicate id + version.</summary>
    void Register(CharacterBible bible);

    bool Contains(CharacterBibleId id, CharacterBibleVersion version);

    /// <summary>The exact registered version; throws when it is not registered.</summary>
    CharacterBible Get(CharacterBibleId id, CharacterBibleVersion version);

    /// <summary>The highest registered version for an id; throws when none is registered.</summary>
    CharacterBible GetLatest(CharacterBibleId id);

    /// <summary>Non-throwing exact lookup.</summary>
    bool TryGet(
        CharacterBibleId id,
        CharacterBibleVersion version,
        [NotNullWhen(true)] out CharacterBible? bible);

    /// <summary>Non-throwing latest lookup.</summary>
    bool TryGetLatest(CharacterBibleId id, [NotNullWhen(true)] out CharacterBible? bible);
}
