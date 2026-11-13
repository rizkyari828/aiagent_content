using AIStudio.Application.Content;
using AIStudio.Domain.Tracking;

namespace AIStudio.Application.Tracking;

/// <summary>
/// Read-only project tracking summary. The metric part is the NEWEST snapshot, never a
/// sum of snapshots: successive captures are cumulative observations of the same totals.
/// Human and machine minutes and every currency are reported separately.
/// </summary>
public sealed class TrackingSummaryWorkflow(
    ITrackingRepository tracking,
    IContentProjectReader contentProjects)
{
    /// <summary>
    /// Builds the summary. Returns <c>null</c> when the content project does not exist.
    /// </summary>
    public async Task<TrackingSummary?> GetAsync(
        Guid contentProjectId,
        CancellationToken cancellationToken)
    {
        if (contentProjectId == Guid.Empty)
        {
            throw new ArgumentException(
                "A non-empty GUID is required.",
                nameof(contentProjectId));
        }

        if (await contentProjects.FindByIdAsync(contentProjectId, cancellationToken) is null)
        {
            return null;
        }

        var snapshots = await tracking.ListAllSnapshotsAsync(
            contentProjectId,
            cancellationToken);
        var latestSnapshot = snapshots.Count > 0
            ? snapshots.OrderByDescending(s => s.CapturedAt).ThenBy(s => s.Id).First()
            : await tracking.FindLatestSnapshotAsync(contentProjectId, cancellationToken);
        var timeTotals = await tracking.GetTimeTotalsAsync(
            contentProjectId,
            cancellationToken);
        var costTotals = await tracking.GetCostTotalsAsync(
            contentProjectId,
            cancellationToken);

        return new TrackingSummary(
            contentProjectId,
            latestSnapshot is null ? null : MetricSnapshotView.From(latestSnapshot),
            timeTotals.HumanMinutes,
            timeTotals.MachineMinutes,
            Summarize(costTotals),
            RollupByPublication(snapshots));
    }

    /// <summary>
    /// Groups metric snapshots by PublicationRecordId (including null for unbound observations)
    /// and extracts the latest cumulative observation and snapshot count per group.
    /// Bound publications are sorted deterministically before unbound observations.
    /// </summary>
    public static IReadOnlyList<PublicationTrackingRollup> RollupByPublication(
        IEnumerable<MetricSnapshot> snapshots)
    {
        ArgumentNullException.ThrowIfNull(snapshots);

        return snapshots
            .GroupBy(snapshot => snapshot.PublicationRecordId)
            .Select(group =>
            {
                var latest = group
                    .OrderByDescending(s => s.CapturedAt)
                    .ThenBy(s => s.Id)
                    .First();

                return new PublicationTrackingRollup(
                    group.Key,
                    MetricSnapshotView.From(latest),
                    group.Count());
            })
            .OrderBy(rollup => rollup.PublicationRecordId.HasValue ? 0 : 1)
            .ThenBy(rollup => rollup.PublicationRecordId)
            .ToArray();
    }

    /// <summary>
    /// Groups cost subtotals strictly per currency and keeps actual, estimated, and
    /// refunded distinct. <c>netActual = actual - refunded</c>; when refunds exceed the
    /// recorded actual charges the negative is reported explicitly through
    /// <see cref="CostTotals.RefundExceedsActual"/> rather than being clamped away.
    /// </summary>
    public static IReadOnlyList<CostTotals> Summarize(
        IReadOnlyList<CostKindTotals> totals)
    {
        ArgumentNullException.ThrowIfNull(totals);

        return totals
            .GroupBy(total => total.Currency, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group =>
            {
                var actual = Sum(group, CostEntryKind.Actual);
                var estimated = Sum(group, CostEntryKind.Estimated);
                var refunded = Sum(group, CostEntryKind.Refunded);
                return new CostTotals(
                    group.Key,
                    actual,
                    estimated,
                    refunded,
                    actual - refunded,
                    refunded > actual);
            })
            .ToArray();
    }

    private static decimal Sum(
        IEnumerable<CostKindTotals> totals,
        CostEntryKind kind) =>
        totals
            .Where(total => total.Kind == kind)
            .Sum(total => total.Total);
}
