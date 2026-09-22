using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using AIStudio.Application.Bibles;
using AIStudio.Application.Stories;

namespace AIStudio.Application.IdentityAssets;

/// <summary>
/// Thread-safe, in-memory metadata authority. Approved records are replaced
/// immutably on approval and have no update operation.
/// </summary>
public sealed class IdentityAssetRegistry : IIdentityAssetRegistry
{
    private readonly object _gate = new();
    private readonly TimeProvider _timeProvider;
    private readonly IIdentityAssetMetadataPersistence? _persistence;
    private readonly Dictionary<(AssetReferenceId Id, IdentityAssetVersion Version), IdentityAsset> _byVersion = [];
    private readonly Dictionary<AssetReferenceId, IdentityAsset> _latest = [];
    private readonly Dictionary<AssetReferenceId, IdentityAsset> _latestApproved = [];

    public IdentityAssetRegistry(
        IEnumerable<IdentityAsset>? assets = null,
        TimeProvider? timeProvider = null,
        IIdentityAssetMetadataPersistence? persistence = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
        _persistence = persistence;

        if (persistence is not null)
        {
            // Rehydrate durable state before serving any lookup so Draft/Approved
            // status, ApprovedAt, and version allocation survive restart.
            foreach (var asset in persistence.LoadAll())
            {
                var issues = IdentityAssetValidator.Validate(asset);
                if (issues.Count > 0)
                {
                    throw new InvalidOperationException(
                        $"Persisted identity asset '{asset.Id}' v{asset.Version.Value} is invalid: {issues[0].Code}.");
                }

                lock (_gate)
                {
                    AddToIndex(Snapshot(asset));
                }
            }
        }

        foreach (var asset in assets ?? [])
        {
            Register(asset);
        }
    }

    public IReadOnlyList<IdentityAsset> Assets
    {
        get
        {
            lock (_gate)
            {
                return _byVersion.Values
                    .OrderBy(asset => asset.Id.Value, StringComparer.Ordinal)
                    .ThenBy(asset => asset.Version.Value)
                    .ToList();
            }
        }
    }

    public IdentityAssetVersion NextVersion(AssetReferenceId id)
    {
        RequireId(id);

        lock (_gate)
        {
            if (!_latest.TryGetValue(id, out var latest))
            {
                return new IdentityAssetVersion(1);
            }

            if (latest.Version.Value == int.MaxValue)
            {
                throw new InvalidOperationException($"Identity asset '{id}' has exhausted integer versions.");
            }

            return new IdentityAssetVersion(latest.Version.Value + 1);
        }
    }

    public void Register(IdentityAsset asset)
    {
        ArgumentNullException.ThrowIfNull(asset);

        var issues = IdentityAssetValidator.Validate(asset);
        if (issues.Count > 0)
        {
            throw new InvalidOperationException(
                $"Identity asset '{asset.Id}' v{asset.Version.Value} is invalid: {issues[0].Code}.");
        }

        if (asset.Status != IdentityAssetStatus.Draft)
        {
            throw new InvalidOperationException("Only Draft identity assets can be newly registered.");
        }

        var snapshot = Snapshot(asset);

        // Durable first: expose the record only after it is safely persisted.
        _persistence?.Create(snapshot);

        lock (_gate)
        {
            AddToIndex(snapshot);
        }
    }

    private void AddToIndex(IdentityAsset snapshot)
    {
        if (!_byVersion.TryAdd((snapshot.Id, snapshot.Version), snapshot))
        {
            throw new InvalidOperationException(
                $"Duplicate identity asset registration '{snapshot.Id}' v{snapshot.Version.Value}.");
        }

        if (!_latest.TryGetValue(snapshot.Id, out var current)
            || snapshot.Version.Value > current.Version.Value)
        {
            _latest[snapshot.Id] = snapshot;
        }

        if (snapshot.Status == IdentityAssetStatus.Approved
            && (!_latestApproved.TryGetValue(snapshot.Id, out var currentApproved)
                || snapshot.Version.Value > currentApproved.Version.Value))
        {
            _latestApproved[snapshot.Id] = snapshot;
        }
    }

    public IdentityAsset Approve(AssetReferenceId id, IdentityAssetVersion version)
    {
        lock (_gate)
        {
            if (!_byVersion.TryGetValue((id, version), out var asset))
            {
                throw new KeyNotFoundException($"Identity asset '{id}' v{version.Value} is not registered.");
            }

            if (asset.Status == IdentityAssetStatus.Approved)
            {
                return asset;
            }

            var approved = asset with
            {
                Status = IdentityAssetStatus.Approved,
                ApprovedAt = _timeProvider.GetUtcNow()
            };

            // Durable first, so an approval is never visible before it is persisted.
            _persistence?.Update(approved);

            _byVersion[(id, version)] = approved;
            if (_latest.TryGetValue(id, out var latest) && latest.Version == version)
            {
                _latest[id] = approved;
            }

            if (!_latestApproved.TryGetValue(id, out var current)
                || version.Value > current.Version.Value)
            {
                _latestApproved[id] = approved;
            }

            return approved;
        }
    }

    public IdentityAsset Get(AssetReferenceId id, IdentityAssetVersion version) =>
        TryGet(id, version, out var asset)
            ? asset
            : throw new KeyNotFoundException($"Identity asset '{id}' v{version.Value} is not registered.");

    public IdentityAsset GetLatest(AssetReferenceId id) =>
        TryGetLatest(id, out var asset)
            ? asset
            : throw new KeyNotFoundException($"Identity asset '{id}' is not registered.");

    public IdentityAsset GetLatestApproved(AssetReferenceId id) =>
        TryGetLatestApproved(id, out var asset)
            ? asset
            : throw new KeyNotFoundException($"Identity asset '{id}' has no approved version.");

    public bool TryGet(
        AssetReferenceId id,
        IdentityAssetVersion version,
        [NotNullWhen(true)] out IdentityAsset? asset)
    {
        lock (_gate)
        {
            return _byVersion.TryGetValue((id, version), out asset);
        }
    }

    public bool TryGetLatest(
        AssetReferenceId id,
        [NotNullWhen(true)] out IdentityAsset? asset)
    {
        lock (_gate)
        {
            return _latest.TryGetValue(id, out asset);
        }
    }

    public bool TryGetLatestApproved(
        AssetReferenceId id,
        [NotNullWhen(true)] out IdentityAsset? asset)
    {
        lock (_gate)
        {
            return _latestApproved.TryGetValue(id, out asset);
        }
    }

    private static IdentityAsset Snapshot(IdentityAsset asset) =>
        asset with
        {
            Provenance = asset.Provenance with
            {
                ParentAssetIds = new ReadOnlyCollection<AssetReferenceId>(
                    (asset.Provenance.ParentAssetIds ?? []).ToArray())
            }
        };

    private static void RequireId(AssetReferenceId id)
    {
        if (!StoryIdentifier.IsValid(id.Value))
        {
            throw new ArgumentException("A valid identity asset id is required.", nameof(id));
        }
    }
}
