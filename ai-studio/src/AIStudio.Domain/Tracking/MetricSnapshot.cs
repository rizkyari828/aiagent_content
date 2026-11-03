namespace AIStudio.Domain.Tracking;

/// <summary>
/// One manually recorded measurement of a content project at one instant. A snapshot is
/// CUMULATIVE at its capture point: it describes the totals as observed then, so
/// snapshots are never added together. Every value keeps the difference between
/// <c>null</c> (not measured) and <c>0</c> (measured as zero).
/// </summary>
public sealed class MetricSnapshot
{
    private MetricSnapshot()
    {
    }

    private MetricSnapshot(
        Guid id,
        Guid contentProjectId,
        Guid? publicationRecordId,
        int? views,
        int? impressions,
        decimal? clickThroughRate,
        decimal? averageViewDurationSeconds,
        decimal? watchTimeMinutes,
        int? subscribersGained,
        DateTimeOffset capturedAt,
        string source,
        string? notes,
        DateTimeOffset createdAt)
    {
        Id = id;
        ContentProjectId = contentProjectId;
        PublicationRecordId = publicationRecordId;
        Views = views;
        Impressions = impressions;
        ClickThroughRate = clickThroughRate;
        AverageViewDurationSeconds = averageViewDurationSeconds;
        WatchTimeMinutes = watchTimeMinutes;
        SubscribersGained = subscribersGained;
        CapturedAt = capturedAt;
        Source = source;
        Notes = notes;
        CreatedAt = createdAt;
    }

    public Guid Id { get; private set; }

    public Guid ContentProjectId { get; private set; }

    /// <summary>
    /// The publication record that was already recorded for this project when the
    /// snapshot was captured, when one existed. It is captured once and never re-bound.
    /// </summary>
    public Guid? PublicationRecordId { get; private set; }

    public int? Views { get; private set; }

    public int? Impressions { get; private set; }

    public decimal? ClickThroughRate { get; private set; }

    public decimal? AverageViewDurationSeconds { get; private set; }

    public decimal? WatchTimeMinutes { get; private set; }

    public int? SubscribersGained { get; private set; }

    public DateTimeOffset CapturedAt { get; private set; }

    public string Source { get; private set; } = string.Empty;

    public string? Notes { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public static MetricSnapshot Create(
        Guid contentProjectId,
        Guid? publicationRecordId,
        int? views,
        int? impressions,
        decimal? clickThroughRate,
        decimal? averageViewDurationSeconds,
        decimal? watchTimeMinutes,
        int? subscribersGained,
        DateTimeOffset capturedAt,
        string source,
        string? notes,
        DateTimeOffset utcNow,
        Guid? id = null)
    {
        TrackingRules.RequireIdentifier(contentProjectId, nameof(contentProjectId));

        if (publicationRecordId is { } publicationId)
        {
            TrackingRules.RequireIdentifier(publicationId, nameof(publicationRecordId));
        }

        var normalizedViews = TrackingRules.OptionalCount(views, nameof(views));
        var normalizedImpressions = TrackingRules.OptionalCount(
            impressions,
            nameof(impressions));
        var normalizedClickThroughRate = TrackingRules.OptionalClickThroughRate(
            clickThroughRate,
            nameof(clickThroughRate));
        var normalizedAverageViewDuration = TrackingRules.OptionalNonNegativeDecimal(
            averageViewDurationSeconds,
            nameof(averageViewDurationSeconds));
        var normalizedWatchTime = TrackingRules.OptionalNonNegativeDecimal(
            watchTimeMinutes,
            nameof(watchTimeMinutes));
        var normalizedSubscribersGained = TrackingRules.OptionalCount(
            subscribersGained,
            nameof(subscribersGained));

        RequireMetricValue(
            normalizedViews,
            normalizedImpressions,
            normalizedClickThroughRate,
            normalizedAverageViewDuration,
            normalizedWatchTime,
            normalizedSubscribersGained);

        var snapshotId = id ?? Guid.NewGuid();
        TrackingRules.RequireIdentifier(snapshotId, nameof(id));

        return new MetricSnapshot(
            snapshotId,
            contentProjectId,
            publicationRecordId,
            normalizedViews,
            normalizedImpressions,
            normalizedClickThroughRate,
            normalizedAverageViewDuration,
            normalizedWatchTime,
            normalizedSubscribersGained,
            TrackingRules.RequireTimestamp(capturedAt, nameof(capturedAt)),
            TrackingRules.NormalizeToken(source, nameof(source)),
            TrackingRules.OptionalNotes(notes, nameof(notes)),
            TrackingRules.RequireTimestamp(utcNow, nameof(utcNow)));
    }

    private static void RequireMetricValue(
        int? views,
        int? impressions,
        decimal? clickThroughRate,
        decimal? averageViewDurationSeconds,
        decimal? watchTimeMinutes,
        int? subscribersGained)
    {
        if (views is null
            && impressions is null
            && clickThroughRate is null
            && averageViewDurationSeconds is null
            && watchTimeMinutes is null
            && subscribersGained is null)
        {
            throw new ArgumentException(
                "At least one metric value is required; a snapshot of nothing is not recorded.",
                "metrics");
        }
    }
}
