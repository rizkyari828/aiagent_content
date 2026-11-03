using AIStudio.Application.Tracking;
using AIStudio.Domain.Tracking;
using AIStudio.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace AIStudio.Infrastructure.Tracking;

/// <summary>
/// EF adapter for the three manually recorded tracking tables. Metric snapshots are
/// returned as point-in-time observations (newest first, bounded); totals are computed by
/// the database per kind and per currency and are never combined across kinds or
/// currencies.
/// </summary>
public sealed class TrackingRepository(ApplicationDbContext dbContext)
    : ITrackingRepository
{
    /// <summary>
    /// The one-snapshot-per-(project, capture instant) index. A concurrent duplicate that
    /// slips past the workflow pre-check surfaces as a stable conflict instead of a
    /// generic persistence error.
    /// </summary>
    public const string MetricSnapshotUniqueIndex =
        "ux_metric_snapshots_content_project_id_captured_at";

    public Task<MetricSnapshot?> FindSnapshotAsync(
        Guid contentProjectId,
        DateTimeOffset capturedAt,
        CancellationToken cancellationToken) =>
        dbContext.MetricSnapshots
            .AsNoTracking()
            .SingleOrDefaultAsync(
                snapshot => snapshot.ContentProjectId == contentProjectId
                    && snapshot.CapturedAt == capturedAt,
                cancellationToken);

    public Task<MetricSnapshot?> FindLatestSnapshotAsync(
        Guid contentProjectId,
        CancellationToken cancellationToken) =>
        dbContext.MetricSnapshots
            .AsNoTracking()
            .Where(snapshot => snapshot.ContentProjectId == contentProjectId)
            .OrderByDescending(snapshot => snapshot.CapturedAt)
            .ThenBy(snapshot => snapshot.Id)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<MetricSnapshot>> ListSnapshotsAsync(
        Guid contentProjectId,
        int take,
        CancellationToken cancellationToken) =>
        await dbContext.MetricSnapshots
            .AsNoTracking()
            .Where(snapshot => snapshot.ContentProjectId == contentProjectId)
            .OrderByDescending(snapshot => snapshot.CapturedAt)
            .ThenBy(snapshot => snapshot.Id)
            .Take(take)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<TimeEntry>> ListTimeEntriesAsync(
        Guid contentProjectId,
        int take,
        CancellationToken cancellationToken) =>
        await dbContext.TimeEntries
            .AsNoTracking()
            .Where(entry => entry.ContentProjectId == contentProjectId)
            .OrderByDescending(entry => entry.RecordedAt)
            .ThenBy(entry => entry.Id)
            .Take(take)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<CostEntry>> ListCostEntriesAsync(
        Guid contentProjectId,
        int take,
        CancellationToken cancellationToken) =>
        await dbContext.CostEntries
            .AsNoTracking()
            .Where(entry => entry.ContentProjectId == contentProjectId)
            .OrderByDescending(entry => entry.IncurredAt)
            .ThenBy(entry => entry.Id)
            .Take(take)
            .ToListAsync(cancellationToken);

    public async Task<TimeEntryTotals> GetTimeTotalsAsync(
        Guid contentProjectId,
        CancellationToken cancellationToken)
    {
        var totals = await dbContext.TimeEntries
            .Where(entry => entry.ContentProjectId == contentProjectId)
            .GroupBy(entry => entry.Kind)
            .Select(group => new
            {
                Kind = group.Key,
                Total = group.Sum(entry => entry.Minutes)
            })
            .ToListAsync(cancellationToken);

        var humanMinutes = totals
            .Where(total => total.Kind == TimeEntryKind.Human)
            .Sum(total => total.Total);
        var machineMinutes = totals
            .Where(total => total.Kind == TimeEntryKind.Machine)
            .Sum(total => total.Total);

        return new TimeEntryTotals(humanMinutes, machineMinutes);
    }

    public async Task<IReadOnlyList<CostKindTotals>> GetCostTotalsAsync(
        Guid contentProjectId,
        CancellationToken cancellationToken)
    {
        var totals = await dbContext.CostEntries
            .Where(entry => entry.ContentProjectId == contentProjectId)
            .GroupBy(entry => new { entry.Currency, entry.Kind })
            .Select(group => new
            {
                group.Key.Currency,
                group.Key.Kind,
                Total = group.Sum(entry => entry.Amount)
            })
            .ToListAsync(cancellationToken);

        return totals
            .Select(total => new CostKindTotals(total.Currency, total.Kind, total.Total))
            .ToArray();
    }

    public void Add(MetricSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        dbContext.MetricSnapshots.Add(snapshot);
    }

    public void Add(TimeEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        dbContext.TimeEntries.Add(entry);
    }

    public void Add(CostEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        dbContext.CostEntries.Add(entry);
    }

    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsDuplicateSnapshot(exception))
        {
            throw new TrackingException(
                "metric_snapshot_already_recorded",
                "A metric snapshot with the same capture instant already exists for this content project.",
                exception);
        }
    }

    private static bool IsDuplicateSnapshot(DbUpdateException exception) =>
        exception.InnerException is PostgresException postgres
        && string.Equals(
            postgres.SqlState,
            PostgresErrorCodes.UniqueViolation,
            StringComparison.Ordinal)
        && string.Equals(
            postgres.ConstraintName,
            MetricSnapshotUniqueIndex,
            StringComparison.Ordinal);
}
