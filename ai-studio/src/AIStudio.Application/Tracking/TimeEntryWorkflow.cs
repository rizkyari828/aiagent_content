using AIStudio.Application.Content;
using AIStudio.Domain.Tracking;

namespace AIStudio.Application.Tracking;

/// <summary>
/// Records and reads manually supplied time entries. Human and machine work are recorded
/// as separate kinds and are never collapsed into one number.
/// </summary>
public sealed class TimeEntryWorkflow(
    ITrackingRepository tracking,
    IContentProjectReader contentProjects,
    TimeProvider timeProvider)
{
    /// <summary>
    /// Records one entry. Returns <c>null</c> when the content project does not exist and
    /// throws <see cref="TrackingException"/> for an invalid value.
    /// </summary>
    public async Task<TimeEntryView?> CreateAsync(
        Guid contentProjectId,
        string? kind,
        string? category,
        decimal minutes,
        DateTimeOffset recordedAt,
        string? notes,
        CancellationToken cancellationToken)
    {
        RequireIdentifier(contentProjectId, nameof(contentProjectId));

        if (!TrackingKinds.TryParseTimeEntryKind(kind, out var parsedKind))
        {
            throw Error(
                "time_entry_kind_invalid",
                "Time entry kind must be 'human' or 'machine'.");
        }

        if (await contentProjects.FindByIdAsync(contentProjectId, cancellationToken) is null)
        {
            return null;
        }

        var entry = Normalize(() => TimeEntry.Create(
            contentProjectId,
            parsedKind,
            category ?? string.Empty,
            minutes,
            recordedAt,
            notes,
            timeProvider.GetUtcNow()));

        tracking.Add(entry);
        await tracking.SaveChangesAsync(cancellationToken);
        return TimeEntryView.From(entry);
    }

    /// <summary>
    /// Newest-first entries for one project, bounded to 1..100 rows. Returns
    /// <c>null</c> when the content project does not exist.
    /// </summary>
    public async Task<IReadOnlyList<TimeEntryView>?> ListAsync(
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

        var entries = await tracking.ListTimeEntriesAsync(
            contentProjectId,
            take,
            cancellationToken);
        return entries.Select(TimeEntryView.From).ToArray();
    }

    private static TimeEntry Normalize(Func<TimeEntry> create)
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
            "kind" => "time_entry_kind_invalid",
            "category" => "time_entry_category_invalid",
            "minutes" => "time_entry_minutes_invalid",
            "recordedAt" => "time_entry_recorded_at_invalid",
            "notes" => "time_entry_notes_invalid",
            _ => "time_entry_invalid_request"
        };

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
