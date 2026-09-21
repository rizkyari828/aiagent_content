namespace AIStudio.Application.Capabilities;

/// <summary>
/// Why a requirement could not be resolved. Distinguishes a capability with no
/// registered provider at all from one whose providers exist but are all disabled,
/// because the operator follow-up differs.
/// </summary>
public sealed record CapabilityGap
{
    public CapabilityId CapabilityId { get; init; }

    public CapabilityGapReason Reason { get; init; }

    /// <summary>Registered provider ids for the capability, if any (never executable paths).</summary>
    public IReadOnlyList<string> KnownProviderIds { get; init; } = [];
}

public enum CapabilityGapReason
{
    /// <summary>No provider implementation is registered for the capability.</summary>
    CapabilityNotImplemented = 0,

    /// <summary>Providers are registered but configuration disables all of them.</summary>
    ProviderDisabled = 1
}
