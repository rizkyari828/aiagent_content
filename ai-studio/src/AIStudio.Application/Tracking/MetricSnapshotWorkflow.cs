using System.Globalization;
using AIStudio.Application.Content;
using AIStudio.Domain.Tracking;

namespace AIStudio.Application.Tracking;

/// <summary>
/// Records and reads manually supplied metric snapshots. A snapshot is an observation of
/// a point in time, so the workflow never aggregates snapshots and never replaces an
/// unknown value with zero.
/// </summary>
public sealed class MetricSnapshotWorkflow(
    ITrackingRepository tracking,
    IContentProjectReader contentProjects,
    IPublicationReferenceLookup publications,
    TimeProvider timeProvider)
{
    /// <summary>
    /// Records one snapshot. Returns <c>null</c> when the content project does not exist
    /// and throws <see cref="TrackingException"/> for an invalid value or an already
    /// recorded capture instant.
    /// </summary>
    public async Task<MetricSnapshotView?> CreateAsync(
        Guid contentProjectId,
        int? views,
        int? impressions,
        decimal? clickThroughRate,
        decimal? averageViewDurationSeconds,
        decimal? watchTimeMinutes,
        int? subscribersGained,
        DateTimeOffset capturedAt,
        string? source,
        string? notes,
        CancellationToken cancellationToken)
    {
        RequireIdentifier(contentProjectId, nameof(contentProjectId));

        if (await contentProjects.FindByIdAsync(contentProjectId, cancellationToken) is null)
        {
            return null;
        }

        var publicationRecordId = await publications.FindPublicationRecordIdAsync(
            contentProjectId,
            cancellationToken);

        var snapshot = Normalize(() => MetricSnapshot.Create(
            contentProjectId,
            publicationRecordId,
            views,
            impressions,
            clickThroughRate,
            averageViewDurationSeconds,
            watchTimeMinutes,
            subscribersGained,
            capturedAt,
            source ?? string.Empty,
            notes,
            timeProvider.GetUtcNow()));

        if (await tracking.FindSnapshotAsync(
            contentProjectId,
            snapshot.CapturedAt,
            cancellationToken) is not null)
        {
            throw Error(
                "metric_snapshot_already_recorded",
                $"A metric snapshot for '{Format(snapshot.CapturedAt)}' already exists for this content project.");
        }

        tracking.Add(snapshot);
        await tracking.SaveChangesAsync(cancellationToken);
        return MetricSnapshotView.From(snapshot);
    }

    /// <summary>
    /// Newest-first snapshots for one project, bounded to 1..100 rows. Returns
    /// <c>null</c> when the content project does not exist.
    /// </summary>
    public async Task<IReadOnlyList<MetricSnapshotView>?> ListAsync(
        Guid contentProjectId,
        int take,
        CancellationToken cancellationToken)
    {
        RequireIdentifier(contentProjectId, nameof(contentProjectId));
        TrackingTake.Validate(take);

        if (await contentProjects.FindByIdAsync(contentProjectId, cancellationToken) is null)
        {
            return null;
        }

        var snapshots = await tracking.ListSnapshotsAsync(
            contentProjectId,
            take,
            cancellationToken);
        return snapshots.Select(MetricSnapshotView.From).ToArray();
    }

    private static MetricSnapshot Normalize(Func<MetricSnapshot> create)
    {
        try
        {
            return create();
        }
        catch (ArgumentException exception)
        {
            throw Error(FieldCode(exception.ParamName), exception.Message, exception);
        }
    }

    private static string FieldCode(string? parameterName) =>
        parameterName switch
        {
            "views" => "metric_snapshot_views_invalid",
            "impressions" => "metric_snapshot_impressions_invalid",
            "clickThroughRate" => "metric_snapshot_click_through_rate_invalid",
            "averageViewDurationSeconds" => "metric_snapshot_average_view_duration_invalid",
            "watchTimeMinutes" => "metric_snapshot_watch_time_invalid",
            "subscribersGained" => "metric_snapshot_subscribers_gained_invalid",
            "capturedAt" => "metric_snapshot_captured_at_invalid",
            "source" => "metric_snapshot_source_invalid",
            "notes" => "metric_snapshot_notes_invalid",
            "metrics" => "metric_snapshot_empty",
            _ => "metric_snapshot_invalid_request"
        };

    private static string Format(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    private static void RequireIdentifier(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("A non-empty GUID is required.", parameterName);
        }
    }

    private static TrackingException Error(
        string errorCode,
        string message,
        Exception? innerException = null) =>
        new(errorCode, message, innerException);
}
