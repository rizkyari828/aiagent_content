namespace AIStudio.Application.Capabilities;

/// <summary>
/// A production request for a capability, with an optional ordered fallback chain
/// of alternative capabilities. The registry resolves the chain deterministically:
/// the first capability with an enabled provider wins, so a disabled optional
/// provider can degrade to a safe always-available capability instead of failing.
/// </summary>
public sealed record CapabilityRequirement
{
    public CapabilityId CapabilityId { get; init; }

    public IReadOnlyList<CapabilityId> FallbackCapabilityIds { get; init; } = [];

    public static CapabilityRequirement For(CapabilityId capabilityId) =>
        new() { CapabilityId = capabilityId };

    public static CapabilityRequirement For(
        CapabilityId capabilityId,
        params CapabilityId[] fallbackCapabilityIds) =>
        new()
        {
            CapabilityId = capabilityId,
            FallbackCapabilityIds = fallbackCapabilityIds
        };
}
