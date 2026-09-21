namespace AIStudio.Application.Capabilities;

/// <summary>
/// Application-facing capability discovery/resolution. It answers "what can the
/// studio do?" and "which trusted provider implements it?" from compile-time
/// registrations plus configuration only. It never loads, installs, downloads,
/// resolves by class name, or executes a provider, and it never accepts an
/// executable implementation from data.
/// </summary>
public interface ICapabilityRegistry
{
    /// <summary>All known capabilities, ordered deterministically by id.</summary>
    IReadOnlyList<CapabilityDescriptor> Capabilities { get; }

    bool IsRegistered(CapabilityId capabilityId);

    /// <summary>Registered providers for a capability (enabled and disabled), preferred first.</summary>
    IReadOnlyList<CapabilityProviderDescriptor> GetProviders(CapabilityId capabilityId);

    /// <summary>Enabled providers for a capability, preferred first.</summary>
    IReadOnlyList<CapabilityProviderDescriptor> GetAvailableProviders(CapabilityId capabilityId);

    /// <summary>Resolves the preferred enabled provider, or returns an explicit gap.</summary>
    CapabilityResolution Resolve(CapabilityRequirement requirement);

    /// <summary>Convenience accessor: the gap for an unresolved requirement, otherwise null.</summary>
    CapabilityGap? FindGap(CapabilityRequirement requirement);
}
