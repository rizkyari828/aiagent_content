namespace AIStudio.Application.Capabilities;

/// <summary>
/// Result of resolving a <see cref="CapabilityRequirement"/>: either a concrete
/// trusted provider, or an explicit <see cref="CapabilityGap"/> when nothing can
/// satisfy it.
/// </summary>
public sealed record CapabilityResolution
{
    public CapabilityResolutionStatus Status { get; init; }

    public CapabilityProviderDescriptor? Provider { get; init; }

    /// <summary>The requested capability, or the fallback capability that resolved.</summary>
    public CapabilityId ResolvedCapabilityId { get; init; }

    public bool UsedFallback { get; init; }

    public CapabilityGap? Gap { get; init; }

    public bool IsResolved => Status == CapabilityResolutionStatus.Resolved;

    public static CapabilityResolution Resolved(
        CapabilityId resolvedCapabilityId,
        CapabilityProviderDescriptor provider,
        bool usedFallback) =>
        new()
        {
            Status = CapabilityResolutionStatus.Resolved,
            ResolvedCapabilityId = resolvedCapabilityId,
            Provider = provider,
            UsedFallback = usedFallback
        };

    public static CapabilityResolution Unresolved(CapabilityGap gap) =>
        new()
        {
            Status = CapabilityResolutionStatus.Unresolved,
            ResolvedCapabilityId = gap.CapabilityId,
            Gap = gap
        };
}

public enum CapabilityResolutionStatus
{
    Unresolved = 0,
    Resolved = 1
}
