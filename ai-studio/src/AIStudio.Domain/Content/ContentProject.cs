namespace AIStudio.Domain.Content;

public sealed class ContentProject
{
    public const int MaxTitleLength = 200;
    public const int MaxBriefLength = 4_000;

    private ContentProject()
    {
    }

    private ContentProject(
        Guid id,
        string title,
        string? brief,
        DateTimeOffset createdAt)
    {
        Id = id;
        Title = title;
        Brief = brief;
        Status = ContentStatus.Draft;
        CreatedAt = createdAt;
        UpdatedAt = createdAt;
    }

    public Guid Id { get; private set; }

    public string Title { get; private set; } = string.Empty;

    public string? Brief { get; private set; }

    public ContentStatus Status { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public static ContentProject Create(
        string title,
        string? brief,
        DateTimeOffset utcNow,
        Guid? id = null)
    {
        var normalizedTitle = RequireText(title, MaxTitleLength, nameof(title));
        var normalizedBrief = OptionalText(brief, MaxBriefLength, nameof(brief));
        var projectId = id ?? Guid.NewGuid();

        if (projectId == Guid.Empty)
        {
            throw new ArgumentException("Content project ID cannot be empty.", nameof(id));
        }

        return new ContentProject(
            projectId,
            normalizedTitle,
            normalizedBrief,
            utcNow.ToUniversalTime());
    }

    public void TransitionTo(ContentStatus nextStatus, DateTimeOffset utcNow)
    {
        if (!CanTransition(Status, nextStatus))
        {
            throw new InvalidOperationException(
                $"Content project cannot transition from {Status} to {nextStatus}.");
        }

        Status = nextStatus;
        UpdatedAt = NormalizeUpdateTime(utcNow);
    }

    /// <summary>
    /// Updates the operator-editable fields only. Status is never writable through this
    /// path, and an unchanged request leaves <see cref="UpdatedAt"/> untouched so a
    /// no-op update cannot reorder project lists.
    /// </summary>
    /// <returns><c>true</c> when a field actually changed.</returns>
    public bool UpdateDetails(string title, string? brief, DateTimeOffset utcNow)
    {
        var normalizedTitle = RequireText(title, MaxTitleLength, nameof(title));
        var normalizedBrief = OptionalText(brief, MaxBriefLength, nameof(brief));

        if (string.Equals(Title, normalizedTitle, StringComparison.Ordinal)
            && string.Equals(Brief, normalizedBrief, StringComparison.Ordinal))
        {
            return false;
        }

        Title = normalizedTitle;
        Brief = normalizedBrief;
        UpdatedAt = NormalizeUpdateTime(utcNow);
        return true;
    }

    /// <summary>
    /// The single explicit, readiness-guarded publishing transition. It deliberately
    /// does not route through <see cref="TransitionTo"/>: the P1 production path is
    /// evidence-based (a currently valid A3 approval plus passing QA), the intermediate
    /// review statuses are not driven by any production code yet, and no generic status
    /// setter is exposed. Published/Archived projects are terminal for this transition.
    /// </summary>
    public void MarkReadyToPublish(DateTimeOffset utcNow)
    {
        if (Status == ContentStatus.ReadyToPublish)
        {
            return;
        }

        if (Status is ContentStatus.Published or ContentStatus.Archived)
        {
            throw new InvalidOperationException(
                $"Content project cannot become ReadyToPublish from {Status}.");
        }

        Status = ContentStatus.ReadyToPublish;
        UpdatedAt = NormalizeUpdateTime(utcNow);
    }

    /// <summary>
    /// Explicit publishing transition. Only a project in <see cref="ContentStatus.ReadyToPublish"/>
    /// can transition to <see cref="ContentStatus.Published"/>. Re-applying this transition
    /// to an already-published project is an idempotent no-op that leaves <see cref="UpdatedAt"/> untouched.
    /// </summary>
    public void MarkPublished(DateTimeOffset utcNow)
    {
        if (Status == ContentStatus.Published)
        {
            return;
        }

        if (Status != ContentStatus.ReadyToPublish)
        {
            throw new InvalidOperationException(
                $"Content project cannot become Published from {Status}.");
        }

        Status = ContentStatus.Published;
        UpdatedAt = NormalizeUpdateTime(utcNow);
    }

    private static bool CanTransition(ContentStatus current, ContentStatus next) =>
        (current, next) switch
        {
            (ContentStatus.Draft, ContentStatus.Researching) => true,
            (ContentStatus.Researching, ContentStatus.IdeaReview) => true,
            (ContentStatus.IdeaReview, ContentStatus.Researching or ContentStatus.Scripting) => true,
            (ContentStatus.Scripting, ContentStatus.ScriptReview) => true,
            (ContentStatus.ScriptReview, ContentStatus.Scripting or ContentStatus.Producing) => true,
            (ContentStatus.Producing, ContentStatus.FinalReview) => true,
            (ContentStatus.FinalReview, ContentStatus.Producing or ContentStatus.ReadyToPublish) => true,
            (ContentStatus.ReadyToPublish, ContentStatus.FinalReview or ContentStatus.Published) => true,
            (_, ContentStatus.Archived) when current != ContentStatus.Archived => true,
            _ => false
        };

    private DateTimeOffset NormalizeUpdateTime(DateTimeOffset value)
    {
        var utcValue = value.ToUniversalTime();
        return utcValue < CreatedAt
            ? throw new ArgumentOutOfRangeException(
                nameof(value),
                "Update time cannot precede creation.")
            : utcValue;
    }

    private static string RequireText(string value, int maxLength, string parameterName)
    {
        var normalized = value?.Trim()
            ?? throw new ArgumentNullException(parameterName);

        return normalized.Length == 0 || normalized.Length > maxLength
            ? throw new ArgumentException(
                $"Value must contain 1 to {maxLength} characters.",
                parameterName)
            : normalized;
    }

    private static string? OptionalText(string? value, int maxLength, string parameterName) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : RequireText(value, maxLength, parameterName);
}
