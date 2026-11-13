using AIStudio.Application.Content;
using AIStudio.Application.Tracking;
using AIStudio.Domain.Tracking;

namespace AIStudio.Tests.Tracking;

internal static class TrackingTestData
{
    public static readonly DateTimeOffset Now =
        new(2026, 11, 3, 10, 0, 0, TimeSpan.Zero);

    public static readonly DateTimeOffset CapturedAt = Now.AddHours(-1);

    public static ContentProjectSnapshot Project(Guid projectId) =>
        new(projectId, "Tracking fixture", null);
}

internal sealed class FixedTrackingTimeProvider(DateTimeOffset utcNow) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => utcNow;
}

/// <summary>
/// In-memory tracking repository. Its aggregate methods mirror the SQL adapter: time is
/// summed per kind and cost is summed per currency and kind, never across them.
/// </summary>
internal sealed class FakeTrackingRepository : ITrackingRepository
{
    public List<MetricSnapshot> Snapshots { get; } = [];

    public List<TimeEntry> TimeEntries { get; } = [];

    public List<CostEntry> CostEntries { get; } = [];

    public int SaveCount { get; private set; }

    public Task<MetricSnapshot?> FindSnapshotAsync(
        Guid contentProjectId,
        DateTimeOffset capturedAt,
        CancellationToken cancellationToken) =>
        Task.FromResult(Snapshots.SingleOrDefault(
            snapshot => snapshot.ContentProjectId == contentProjectId
                && snapshot.CapturedAt == capturedAt));

    public Task<MetricSnapshot?> FindLatestSnapshotAsync(
        Guid contentProjectId,
        CancellationToken cancellationToken) =>
        Task.FromResult(Snapshots
            .Where(snapshot => snapshot.ContentProjectId == contentProjectId)
            .OrderByDescending(snapshot => snapshot.CapturedAt)
            .FirstOrDefault());

    public Task<IReadOnlyList<MetricSnapshot>> ListSnapshotsAsync(
        Guid contentProjectId,
        int take,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<MetricSnapshot>>(Snapshots
            .Where(snapshot => snapshot.ContentProjectId == contentProjectId)
            .OrderByDescending(snapshot => snapshot.CapturedAt)
            .Take(take)
            .ToArray());

    public Task<IReadOnlyList<MetricSnapshot>> ListAllSnapshotsAsync(
        Guid contentProjectId,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<MetricSnapshot>>(Snapshots
            .Where(snapshot => snapshot.ContentProjectId == contentProjectId)
            .OrderByDescending(snapshot => snapshot.CapturedAt)
            .ThenBy(snapshot => snapshot.Id)
            .ToArray());

    public Task<IReadOnlyList<TimeEntry>> ListTimeEntriesAsync(
        Guid contentProjectId,
        int take,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<TimeEntry>>(TimeEntries
            .Where(entry => entry.ContentProjectId == contentProjectId)
            .OrderByDescending(entry => entry.RecordedAt)
            .Take(take)
            .ToArray());

    public Task<IReadOnlyList<CostEntry>> ListCostEntriesAsync(
        Guid contentProjectId,
        int take,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<CostEntry>>(CostEntries
            .Where(entry => entry.ContentProjectId == contentProjectId)
            .OrderByDescending(entry => entry.IncurredAt)
            .Take(take)
            .ToArray());

    public Task<TimeEntryTotals> GetTimeTotalsAsync(
        Guid contentProjectId,
        CancellationToken cancellationToken)
    {
        var entries = TimeEntries
            .Where(entry => entry.ContentProjectId == contentProjectId)
            .ToArray();
        return Task.FromResult(new TimeEntryTotals(
            entries.Where(entry => entry.Kind == TimeEntryKind.Human).Sum(entry => entry.Minutes),
            entries.Where(entry => entry.Kind == TimeEntryKind.Machine).Sum(entry => entry.Minutes)));
    }

    public Task<IReadOnlyList<CostKindTotals>> GetCostTotalsAsync(
        Guid contentProjectId,
        CancellationToken cancellationToken)
    {
        var totals = CostEntries
            .Where(entry => entry.ContentProjectId == contentProjectId)
            .GroupBy(entry => new { entry.Currency, entry.Kind })
            .Select(group => new CostKindTotals(
                group.Key.Currency,
                group.Key.Kind,
                group.Sum(entry => entry.Amount)))
            .ToArray();
        return Task.FromResult<IReadOnlyList<CostKindTotals>>(totals);
    }

    public void Add(MetricSnapshot snapshot) => Snapshots.Add(snapshot);

    public void Add(TimeEntry entry) => TimeEntries.Add(entry);

    public void Add(CostEntry entry) => CostEntries.Add(entry);

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken)
    {
        SaveCount++;
        return Task.FromResult(1);
    }
}

internal sealed class ProjectReaderStub(ContentProjectSnapshot? project)
    : IContentProjectReader
{
    public Task<ContentProjectSnapshot?> FindByIdAsync(
        Guid id,
        CancellationToken cancellationToken) =>
        Task.FromResult(project is not null && project.Id == id ? project : null);
}

internal sealed class PublicationLookupStub(Guid? publicationRecordId)
    : IPublicationReferenceLookup
{
    public Task<Guid?> FindPublicationRecordIdAsync(
        Guid contentProjectId,
        CancellationToken cancellationToken) =>
        Task.FromResult(publicationRecordId);
}
