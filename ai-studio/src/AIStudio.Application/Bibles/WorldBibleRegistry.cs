using System.Diagnostics.CodeAnalysis;

namespace AIStudio.Application.Bibles;

/// <summary>
/// Deterministic in-memory world bible registry. Registration validates
/// structurally and fails fast on an invalid bible or a duplicate id + version. It
/// reads no files, touches no database, and executes nothing; bibles are data and
/// cannot introduce executable behavior.
/// </summary>
public sealed class WorldBibleRegistry : IWorldBibleRegistry
{
    private readonly object _gate = new();
    private readonly Dictionary<(WorldBibleId Id, WorldBibleVersion Version), WorldBible> _byVersion = [];
    private readonly Dictionary<WorldBibleId, WorldBible> _latest = [];

    public WorldBibleRegistry(IEnumerable<WorldBible>? worlds = null)
    {
        if (worlds is null)
        {
            return;
        }

        foreach (var world in worlds)
        {
            Register(world);
        }
    }

    public IReadOnlyList<WorldBible> Worlds
    {
        get
        {
            lock (_gate)
            {
                return _byVersion.Values
                    .OrderBy(world => world.Id.Value, StringComparer.Ordinal)
                    .ThenBy(world => world.Version.Value)
                    .ToList();
            }
        }
    }

    public void Register(WorldBible world)
    {
        ArgumentNullException.ThrowIfNull(world);

        var issues = WorldBibleValidator.Validate(world);
        if (issues.Count > 0)
        {
            throw new InvalidOperationException(
                $"World bible '{world.Id}' v{world.Version.Value} is invalid: {issues[0].Code}.");
        }

        lock (_gate)
        {
            if (!_byVersion.TryAdd((world.Id, world.Version), world))
            {
                throw new InvalidOperationException(
                    $"Duplicate world bible registration '{world.Id}' v{world.Version.Value}.");
            }

            if (!_latest.TryGetValue(world.Id, out var current)
                || world.Version.Value > current.Version.Value)
            {
                _latest[world.Id] = world;
            }
        }
    }

    public bool Contains(WorldBibleId id, WorldBibleVersion version)
    {
        lock (_gate)
        {
            return _byVersion.ContainsKey((id, version));
        }
    }

    public WorldBible Get(WorldBibleId id, WorldBibleVersion version) =>
        TryGet(id, version, out var world)
            ? world
            : throw new KeyNotFoundException($"World bible '{id}' v{version.Value} is not registered.");

    public WorldBible GetLatest(WorldBibleId id) =>
        TryGetLatest(id, out var world)
            ? world
            : throw new KeyNotFoundException($"World bible '{id}' is not registered.");

    public bool TryGet(
        WorldBibleId id,
        WorldBibleVersion version,
        [NotNullWhen(true)] out WorldBible? world)
    {
        lock (_gate)
        {
            if (_byVersion.TryGetValue((id, version), out var found))
            {
                world = found;
                return true;
            }
        }

        world = null;
        return false;
    }

    public bool TryGetLatest(WorldBibleId id, [NotNullWhen(true)] out WorldBible? world)
    {
        lock (_gate)
        {
            if (_latest.TryGetValue(id, out var found))
            {
                world = found;
                return true;
            }
        }

        world = null;
        return false;
    }
}
