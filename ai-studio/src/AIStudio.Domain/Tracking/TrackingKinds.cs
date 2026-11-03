namespace AIStudio.Domain.Tracking;

/// <summary>
/// The closed lowercase tokens for the two tracking kinds. Tokens are the transport and
/// documentation vocabulary (<c>human</c>, <c>machine</c>, <c>actual</c>,
/// <c>estimated</c>, <c>refunded</c>); parsing is case-insensitive and unknown tokens
/// are rejected instead of defaulting.
/// </summary>
public static class TrackingKinds
{
    public const string Human = "human";
    public const string Machine = "machine";
    public const string Actual = "actual";
    public const string Estimated = "estimated";
    public const string Refunded = "refunded";

    public static bool TryParseTimeEntryKind(string? value, out TimeEntryKind kind)
    {
        if (string.Equals(value?.Trim(), Human, StringComparison.OrdinalIgnoreCase))
        {
            kind = TimeEntryKind.Human;
            return true;
        }

        if (string.Equals(value?.Trim(), Machine, StringComparison.OrdinalIgnoreCase))
        {
            kind = TimeEntryKind.Machine;
            return true;
        }

        kind = default;
        return false;
    }

    public static bool TryParseCostEntryKind(string? value, out CostEntryKind kind)
    {
        if (string.Equals(value?.Trim(), Actual, StringComparison.OrdinalIgnoreCase))
        {
            kind = CostEntryKind.Actual;
            return true;
        }

        if (string.Equals(value?.Trim(), Estimated, StringComparison.OrdinalIgnoreCase))
        {
            kind = CostEntryKind.Estimated;
            return true;
        }

        if (string.Equals(value?.Trim(), Refunded, StringComparison.OrdinalIgnoreCase))
        {
            kind = CostEntryKind.Refunded;
            return true;
        }

        kind = default;
        return false;
    }

    public static string ToToken(TimeEntryKind kind) =>
        kind switch
        {
            TimeEntryKind.Human => Human,
            TimeEntryKind.Machine => Machine,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown time entry kind.")
        };

    public static string ToToken(CostEntryKind kind) =>
        kind switch
        {
            CostEntryKind.Actual => Actual,
            CostEntryKind.Estimated => Estimated,
            CostEntryKind.Refunded => Refunded,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown cost entry kind.")
        };
}
