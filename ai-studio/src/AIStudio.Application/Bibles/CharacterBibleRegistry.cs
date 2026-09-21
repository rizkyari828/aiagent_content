using System.Diagnostics.CodeAnalysis;

namespace AIStudio.Application.Bibles;

/// <summary>
/// Deterministic in-memory character bible registry. Registration validates
/// structurally and fails fast on an invalid bible or a duplicate id + version. It
/// reads no files, touches no database, and executes nothing; bibles are data and
/// cannot introduce executable behavior.
/// </summary>
public sealed class CharacterBibleRegistry : ICharacterBibleRegistry
{
    private readonly object _gate = new();
    private readonly Dictionary<(CharacterBibleId Id, CharacterBibleVersion Version), CharacterBible> _byVersion = [];
    private readonly Dictionary<CharacterBibleId, CharacterBible> _latest = [];

    public CharacterBibleRegistry(IEnumerable<CharacterBible>? characters = null)
    {
        if (characters is null)
        {
            return;
        }

        foreach (var character in characters)
        {
            Register(character);
        }
    }

    public IReadOnlyList<CharacterBible> Characters
    {
        get
        {
            lock (_gate)
            {
                return _byVersion.Values
                    .OrderBy(character => character.Id.Value, StringComparer.Ordinal)
                    .ThenBy(character => character.Version.Value)
                    .ToList();
            }
        }
    }

    public void Register(CharacterBible bible)
    {
        ArgumentNullException.ThrowIfNull(bible);

        var issues = CharacterBibleValidator.Validate(bible);
        if (issues.Count > 0)
        {
            throw new InvalidOperationException(
                $"Character bible '{bible.Id}' v{bible.Version.Value} is invalid: {issues[0].Code}.");
        }

        lock (_gate)
        {
            if (!_byVersion.TryAdd((bible.Id, bible.Version), bible))
            {
                throw new InvalidOperationException(
                    $"Duplicate character bible registration '{bible.Id}' v{bible.Version.Value}.");
            }

            if (!_latest.TryGetValue(bible.Id, out var current)
                || bible.Version.Value > current.Version.Value)
            {
                _latest[bible.Id] = bible;
            }
        }
    }

    public bool Contains(CharacterBibleId id, CharacterBibleVersion version)
    {
        lock (_gate)
        {
            return _byVersion.ContainsKey((id, version));
        }
    }

    public CharacterBible Get(CharacterBibleId id, CharacterBibleVersion version) =>
        TryGet(id, version, out var bible)
            ? bible
            : throw new KeyNotFoundException($"Character bible '{id}' v{version.Value} is not registered.");

    public CharacterBible GetLatest(CharacterBibleId id) =>
        TryGetLatest(id, out var bible)
            ? bible
            : throw new KeyNotFoundException($"Character bible '{id}' is not registered.");

    public bool TryGet(
        CharacterBibleId id,
        CharacterBibleVersion version,
        [NotNullWhen(true)] out CharacterBible? bible)
    {
        lock (_gate)
        {
            if (_byVersion.TryGetValue((id, version), out var found))
            {
                bible = found;
                return true;
            }
        }

        bible = null;
        return false;
    }

    public bool TryGetLatest(CharacterBibleId id, [NotNullWhen(true)] out CharacterBible? bible)
    {
        lock (_gate)
        {
            if (_latest.TryGetValue(id, out var found))
            {
                bible = found;
                return true;
            }
        }

        bible = null;
        return false;
    }
}
