using AIStudio.Application.Content;
using AIStudio.Domain.Tracking;

namespace AIStudio.Application.Tracking;

/// <summary>
/// Records and reads manually supplied cash entries. Currency is required on every entry,
/// a refund is a positive amount with the <c>refunded</c> kind, and amounts in different
/// currencies are never combined.
/// </summary>
public sealed class CostEntryWorkflow(
    ITrackingRepository tracking,
    IContentProjectReader contentProjects,
    TimeProvider timeProvider)
{
    /// <summary>
    /// Records one entry. Returns <c>null</c> when the content project does not exist and
    /// throws <see cref="TrackingException"/> for an invalid value.
    /// </summary>
    public async Task<CostEntryView?> CreateAsync(
        Guid contentProjectId,
        decimal amount,
        string? currency,
        string? kind,
        string? category,
        DateTimeOffset incurredAt,
        string? notes,
        CancellationToken cancellationToken)
    {
        RequireIdentifier(contentProjectId, nameof(contentProjectId));

        if (!TrackingKinds.TryParseCostEntryKind(kind, out var parsedKind))
        {
            throw Error(
                "cost_entry_kind_invalid",
                "Cost entry kind must be 'actual', 'estimated', or 'refunded'.");
        }

        if (await contentProjects.FindByIdAsync(contentProjectId, cancellationToken) is null)
        {
            return null;
        }

        var entry = Normalize(() => CostEntry.Create(
            contentProjectId,
            amount,
            currency ?? string.Empty,
            parsedKind,
            category ?? string.Empty,
            incurredAt,
            notes,
            timeProvider.GetUtcNow()));

        tracking.Add(entry);
        await tracking.SaveChangesAsync(cancellationToken);
        return CostEntryView.From(entry);
    }

    /// <summary>
    /// Newest-first entries for one project, bounded to 1..100 rows. Returns
    /// <c>null</c> when the content project does not exist.
    /// </summary>
    public async Task<IReadOnlyList<CostEntryView>?> ListAsync(
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

        var entries = await tracking.ListCostEntriesAsync(
            contentProjectId,
            take,
            cancellationToken);
        return entries.Select(CostEntryView.From).ToArray();
    }

    private static CostEntry Normalize(Func<CostEntry> create)
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
            "amount" => "cost_entry_amount_invalid",
            "currency" => "cost_entry_currency_invalid",
            "kind" => "cost_entry_kind_invalid",
            "category" => "cost_entry_category_invalid",
            "incurredAt" => "cost_entry_incurred_at_invalid",
            "notes" => "cost_entry_notes_invalid",
            _ => "cost_entry_invalid_request"
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
