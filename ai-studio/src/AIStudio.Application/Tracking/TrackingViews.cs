using AIStudio.Domain.Tracking;

namespace AIStudio.Application.Tracking;

/// <summary>Bounded read window shared by the three tracking lists.</summary>
public static class TrackingTake
{
    public const int Minimum = 1;
    public const int Maximum = 100;
    public const int Default = 20;

    public static void Validate(int take)
    {
        if (take is < Minimum or > Maximum)
        {
            throw new ArgumentOutOfRangeException(
                nameof(take),
                $"take must be between {Minimum} and {Maximum}.");
        }
    }
}

/// <summary>
/// One recorded cumulative observation. A null member is "not measured"; zero is a
/// measured zero. The projection never carries a project total because snapshots are
/// cumulative per capture point and must not be summed.
/// </summary>
public sealed record MetricSnapshotView(
    Guid Id,
    Guid ContentProjectId,
    Guid? PublicationRecordId,
    int? Views,
    int? Impressions,
    decimal? ClickThroughRate,
    decimal? AverageViewDurationSeconds,
    decimal? WatchTimeMinutes,
    int? SubscribersGained,
    DateTimeOffset CapturedAt,
    string Source,
    string? Notes,
    DateTimeOffset CreatedAt)
{
    public static MetricSnapshotView From(MetricSnapshot snapshot) =>
        new(
            snapshot.Id,
            snapshot.ContentProjectId,
            snapshot.PublicationRecordId,
            snapshot.Views,
            snapshot.Impressions,
            snapshot.ClickThroughRate,
            snapshot.AverageViewDurationSeconds,
            snapshot.WatchTimeMinutes,
            snapshot.SubscribersGained,
            snapshot.CapturedAt,
            snapshot.Source,
            snapshot.Notes,
            snapshot.CreatedAt);
}

public sealed record TimeEntryView(
    Guid Id,
    Guid ContentProjectId,
    string Kind,
    string Category,
    decimal Minutes,
    DateTimeOffset RecordedAt,
    string? Notes,
    DateTimeOffset CreatedAt)
{
    public static TimeEntryView From(TimeEntry entry) =>
        new(
            entry.Id,
            entry.ContentProjectId,
            TrackingKinds.ToToken(entry.Kind),
            entry.Category,
            entry.Minutes,
            entry.RecordedAt,
            entry.Notes,
            entry.CreatedAt);
}

public sealed record CostEntryView(
    Guid Id,
    Guid ContentProjectId,
    decimal Amount,
    string Currency,
    string Kind,
    string Category,
    DateTimeOffset IncurredAt,
    string? Notes,
    DateTimeOffset CreatedAt)
{
    public static CostEntryView From(CostEntry entry) =>
        new(
            entry.Id,
            entry.ContentProjectId,
            entry.Amount,
            entry.Currency,
            TrackingKinds.ToToken(entry.Kind),
            entry.Category,
            entry.IncurredAt,
            entry.Notes,
            entry.CreatedAt);
}

/// <summary>
/// Per-currency cost summary. The three kinds are reported separately and are never
/// collapsed into one number. <see cref="NetActualTotal"/> is
/// <c>actual - refunded</c>; it may only be negative when refunds exceed recorded
/// actuals, and then <see cref="RefundExceedsActual"/> marks it explicitly instead of
/// silently clamping the data.
/// </summary>
public sealed record CostTotals(
    string Currency,
    decimal ActualTotal,
    decimal EstimatedTotal,
    decimal RefundedTotal,
    decimal NetActualTotal,
    bool RefundExceedsActual);

/// <summary>
/// Project tracking summary. The metric part is the LATEST snapshot — never a sum of
/// snapshots — and human/machine minutes stay separate.
/// </summary>
public sealed record TrackingSummary(
    Guid ContentProjectId,
    MetricSnapshotView? LatestMetricSnapshot,
    decimal HumanMinutesTotal,
    decimal MachineMinutesTotal,
    IReadOnlyList<CostTotals> Costs);
