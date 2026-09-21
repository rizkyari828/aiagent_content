using System.Diagnostics.CodeAnalysis;

namespace AIStudio.Application.Stories;

/// <summary>
/// Deterministic in-memory narrative pattern registry. Registration validates
/// structurally and fails fast on an invalid pattern or a duplicate id + version,
/// so a bad format cannot silently shadow a trusted one. It reads no files and
/// touches no database; patterns are data and cannot introduce executable behavior.
/// </summary>
public sealed class NarrativePatternRegistry : INarrativePatternRegistry
{
    private readonly object _gate = new();
    private readonly Dictionary<(NarrativePatternId Id, NarrativePatternVersion Version), NarrativePattern> _byVersion = [];
    private readonly Dictionary<NarrativePatternId, NarrativePattern> _latest = [];

    public NarrativePatternRegistry(IEnumerable<NarrativePattern>? patterns = null)
    {
        if (patterns is null)
        {
            return;
        }

        foreach (var pattern in patterns)
        {
            Register(pattern);
        }
    }

    public IReadOnlyList<NarrativePattern> Patterns
    {
        get
        {
            lock (_gate)
            {
                return _byVersion.Values
                    .OrderBy(pattern => pattern.Id.Value, StringComparer.Ordinal)
                    .ThenBy(pattern => pattern.Version.Value)
                    .ToList();
            }
        }
    }

    public void Register(NarrativePattern pattern)
    {
        ArgumentNullException.ThrowIfNull(pattern);

        var issues = NarrativePatternValidator.Validate(pattern);
        if (issues.Count > 0)
        {
            throw new InvalidOperationException(
                $"Narrative pattern '{pattern.Id}' v{pattern.Version.Value} is invalid: {issues[0].Code}.");
        }

        lock (_gate)
        {
            if (!_byVersion.TryAdd((pattern.Id, pattern.Version), pattern))
            {
                throw new InvalidOperationException(
                    $"Duplicate narrative pattern registration '{pattern.Id}' v{pattern.Version.Value}.");
            }

            if (!_latest.TryGetValue(pattern.Id, out var current)
                || pattern.Version.Value > current.Version.Value)
            {
                _latest[pattern.Id] = pattern;
            }
        }
    }

    public bool Contains(NarrativePatternId id, NarrativePatternVersion version)
    {
        lock (_gate)
        {
            return _byVersion.ContainsKey((id, version));
        }
    }

    public NarrativePattern Get(NarrativePatternId id, NarrativePatternVersion version) =>
        TryGet(id, version, out var pattern)
            ? pattern
            : throw new KeyNotFoundException($"Narrative pattern '{id}' v{version.Value} is not registered.");

    public NarrativePattern GetLatest(NarrativePatternId id) =>
        TryGetLatest(id, out var pattern)
            ? pattern
            : throw new KeyNotFoundException($"Narrative pattern '{id}' is not registered.");

    public bool TryGet(
        NarrativePatternId id,
        NarrativePatternVersion version,
        [NotNullWhen(true)] out NarrativePattern? pattern)
    {
        lock (_gate)
        {
            if (_byVersion.TryGetValue((id, version), out var found))
            {
                pattern = found;
                return true;
            }
        }

        pattern = null;
        return false;
    }

    public bool TryGetLatest(NarrativePatternId id, [NotNullWhen(true)] out NarrativePattern? pattern)
    {
        lock (_gate)
        {
            if (_latest.TryGetValue(id, out var found))
            {
                pattern = found;
                return true;
            }
        }

        pattern = null;
        return false;
    }
}
