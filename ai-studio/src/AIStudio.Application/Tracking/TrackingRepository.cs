using AIStudio.Domain.Tracking;

namespace AIStudio.Application.Tracking;

/// <summary>
/// Sums of RECORDED time entries, kept per kind. Human and machine minutes are separate
/// quantities with different meanings, so this port never returns a combined total.
/// A project with no entries reports zero, which is a known "nothing recorded" value —
/// unlike an unmeasured metric, which stays null.
/// </summary>
public sealed record TimeEntryTotals(decimal HumanMinutes, decimal MachineMinutes);

/// <summary>
/// One <see cref="CostEntryKind"/> subtotal for one currency. Amounts recorded in
/// different currencies are never added together.
/// </summary>
public sealed record CostKindTotals(
    string Currency,
    CostEntryKind Kind,
    decimal Total);

/// <summary>
/// The recorded-observation lookup used by the metric workflow to bind a snapshot to the
/// publication record that already exists for the project. It is a narrow read contract
/// over the publishing module's application boundary; it duplicates no publishing rule.
/// </summary>
public interface IPublicationReferenceLookup
{
    Task<Guid?> FindPublicationRecordIdAsync(
        Guid contentProjectId,
        CancellationToken cancellationToken);
}

/// <summary>
/// Tracking-owned persistence boundary for the three manually recorded tables. Snapshot
/// reads are point-in-time observations: this port exposes the newest snapshot and a
/// bounded list, never a summed metric.
/// </summary>
public interface ITrackingRepository
{
    Task<MetricSnapshot?> FindSnapshotAsync(
        Guid contentProjectId,
        DateTimeOffset capturedAt,
        CancellationToken cancellationToken);

    Task<MetricSnapshot?> FindLatestSnapshotAsync(
        Guid contentProjectId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<MetricSnapshot>> ListSnapshotsAsync(
        Guid contentProjectId,
        int take,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<MetricSnapshot>> ListAllSnapshotsAsync(
        Guid contentProjectId,
        CancellationToken cancellationToken) =>
        ListSnapshotsAsync(contentProjectId, TrackingTake.Maximum, cancellationToken);

    Task<IReadOnlyList<TimeEntry>> ListTimeEntriesAsync(
        Guid contentProjectId,
        int take,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<CostEntry>> ListCostEntriesAsync(
        Guid contentProjectId,
        int take,
        CancellationToken cancellationToken);

    Task<TimeEntryTotals> GetTimeTotalsAsync(
        Guid contentProjectId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<CostKindTotals>> GetCostTotalsAsync(
        Guid contentProjectId,
        CancellationToken cancellationToken);

    void Add(MetricSnapshot snapshot);

    void Add(TimeEntry entry);

    void Add(CostEntry entry);

    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}
