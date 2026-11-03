namespace AIStudio.Domain.Tracking;

/// <summary>
/// One manually recorded cash amount for a content project. <see cref="Amount"/> is
/// always positive and <see cref="Kind"/> carries its meaning, so a refund is recorded
/// as a positive amount with the <c>refunded</c> kind instead of a negative actual.
/// Amounts in different currencies are never combined.
/// </summary>
public sealed class CostEntry
{
    private CostEntry()
    {
    }

    private CostEntry(
        Guid id,
        Guid contentProjectId,
        decimal amount,
        string currency,
        CostEntryKind kind,
        string category,
        DateTimeOffset incurredAt,
        string? notes,
        DateTimeOffset createdAt)
    {
        Id = id;
        ContentProjectId = contentProjectId;
        Amount = amount;
        Currency = currency;
        Kind = kind;
        Category = category;
        IncurredAt = incurredAt;
        Notes = notes;
        CreatedAt = createdAt;
    }

    public Guid Id { get; private set; }

    public Guid ContentProjectId { get; private set; }

    public decimal Amount { get; private set; }

    public string Currency { get; private set; } = string.Empty;

    public CostEntryKind Kind { get; private set; }

    public string Category { get; private set; } = string.Empty;

    public DateTimeOffset IncurredAt { get; private set; }

    public string? Notes { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public static CostEntry Create(
        Guid contentProjectId,
        decimal amount,
        string currency,
        CostEntryKind kind,
        string category,
        DateTimeOffset incurredAt,
        string? notes,
        DateTimeOffset utcNow,
        Guid? id = null)
    {
        TrackingRules.RequireIdentifier(contentProjectId, nameof(contentProjectId));

        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentException("An unknown cost entry kind is not recorded.", "kind");
        }

        var entryId = id ?? Guid.NewGuid();
        TrackingRules.RequireIdentifier(entryId, nameof(id));

        return new CostEntry(
            entryId,
            contentProjectId,
            TrackingRules.RequirePositiveAmount(amount, nameof(amount)),
            TrackingRules.NormalizeCurrency(currency, nameof(currency)),
            kind,
            TrackingRules.NormalizeToken(category, nameof(category)),
            TrackingRules.RequireTimestamp(incurredAt, nameof(incurredAt)),
            TrackingRules.OptionalNotes(notes, nameof(notes)),
            TrackingRules.RequireTimestamp(utcNow, nameof(utcNow)));
    }
}
