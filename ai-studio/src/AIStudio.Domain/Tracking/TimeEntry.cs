namespace AIStudio.Domain.Tracking;

/// <summary>
/// One manually recorded block of work for a content project. Human and machine time
/// share the table but never share an aggregate: <see cref="Kind"/> is always carried
/// into the summary as a separate total.
/// </summary>
public sealed class TimeEntry
{
    private TimeEntry()
    {
    }

    private TimeEntry(
        Guid id,
        Guid contentProjectId,
        TimeEntryKind kind,
        string category,
        decimal minutes,
        DateTimeOffset recordedAt,
        string? notes,
        DateTimeOffset createdAt)
    {
        Id = id;
        ContentProjectId = contentProjectId;
        Kind = kind;
        Category = category;
        Minutes = minutes;
        RecordedAt = recordedAt;
        Notes = notes;
        CreatedAt = createdAt;
    }

    public Guid Id { get; private set; }

    public Guid ContentProjectId { get; private set; }

    public TimeEntryKind Kind { get; private set; }

    public string Category { get; private set; } = string.Empty;

    public decimal Minutes { get; private set; }

    public DateTimeOffset RecordedAt { get; private set; }

    public string? Notes { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public static TimeEntry Create(
        Guid contentProjectId,
        TimeEntryKind kind,
        string category,
        decimal minutes,
        DateTimeOffset recordedAt,
        string? notes,
        DateTimeOffset utcNow,
        Guid? id = null)
    {
        TrackingRules.RequireIdentifier(contentProjectId, nameof(contentProjectId));

        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentException("An unknown time entry kind is not recorded.", "kind");
        }

        var entryId = id ?? Guid.NewGuid();
        TrackingRules.RequireIdentifier(entryId, nameof(id));

        return new TimeEntry(
            entryId,
            contentProjectId,
            kind,
            TrackingRules.NormalizeToken(category, nameof(category)),
            TrackingRules.RequirePositiveMinutes(minutes, nameof(minutes)),
            TrackingRules.RequireTimestamp(recordedAt, nameof(recordedAt)),
            TrackingRules.OptionalNotes(notes, nameof(notes)),
            TrackingRules.RequireTimestamp(utcNow, nameof(utcNow)));
    }
}
