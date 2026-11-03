namespace AIStudio.Domain.Tracking;

/// <summary>
/// The cash meaning of a recorded amount. Actual money moves, estimated money is a
/// forecast, and a refunded amount reverses an earlier actual charge.
/// </summary>
public enum CostEntryKind
{
    Actual,
    Estimated,
    Refunded
}
