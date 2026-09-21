namespace AIStudio.Application.Capabilities;

/// <summary>
/// Deterministic, in-memory capability registry. It is a pure catalog built from
/// trusted descriptors: it performs no I/O, holds no provider instances, and never
/// instantiates or contacts an external runtime. Duplicate or unknown
/// registrations fail fast so a bad catalog cannot silently shadow a provider.
/// </summary>
public sealed class CapabilityRegistry : ICapabilityRegistry
{
    private readonly IReadOnlyList<CapabilityDescriptor> _capabilities;
    private readonly HashSet<CapabilityId> _registeredCapabilities;
    private readonly Dictionary<CapabilityId, IReadOnlyList<CapabilityProviderDescriptor>> _providersByCapability;

    public CapabilityRegistry(
        IEnumerable<CapabilityDescriptor> capabilities,
        IEnumerable<CapabilityProviderDescriptor> providers)
    {
        ArgumentNullException.ThrowIfNull(capabilities);
        ArgumentNullException.ThrowIfNull(providers);

        _capabilities = capabilities
            .OrderBy(capability => capability.Id.Value, StringComparer.Ordinal)
            .ToList();

        _registeredCapabilities = new HashSet<CapabilityId>();
        foreach (var capability in _capabilities)
        {
            if (!_registeredCapabilities.Add(capability.Id))
            {
                throw new InvalidOperationException(
                    $"Duplicate capability registration '{capability.Id}'.");
            }
        }

        var grouped = new Dictionary<CapabilityId, List<CapabilityProviderDescriptor>>();
        var providerIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var provider in providers)
        {
            if (string.IsNullOrWhiteSpace(provider.ProviderId))
            {
                throw new InvalidOperationException("A capability provider id is required.");
            }

            if (!providerIds.Add(provider.ProviderId))
            {
                throw new InvalidOperationException(
                    $"Duplicate provider registration '{provider.ProviderId}'.");
            }

            if (!_registeredCapabilities.Contains(provider.CapabilityId))
            {
                throw new InvalidOperationException(
                    $"Provider '{provider.ProviderId}' registers unknown capability '{provider.CapabilityId}'.");
            }

            if (!grouped.TryGetValue(provider.CapabilityId, out var list))
            {
                grouped[provider.CapabilityId] = list = [];
            }

            list.Add(provider);
        }

        _providersByCapability = grouped.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyList<CapabilityProviderDescriptor>)pair.Value
                .OrderByDescending(provider => provider.Priority)
                .ThenBy(provider => provider.ProviderId, StringComparer.Ordinal)
                .ToList());
    }

    public IReadOnlyList<CapabilityDescriptor> Capabilities => _capabilities;

    public bool IsRegistered(CapabilityId capabilityId) =>
        _registeredCapabilities.Contains(capabilityId);

    public IReadOnlyList<CapabilityProviderDescriptor> GetProviders(CapabilityId capabilityId) =>
        _providersByCapability.TryGetValue(capabilityId, out var providers)
            ? providers
            : [];

    public IReadOnlyList<CapabilityProviderDescriptor> GetAvailableProviders(CapabilityId capabilityId) =>
        GetProviders(capabilityId).Where(provider => provider.Enabled).ToList();

    public CapabilityResolution Resolve(CapabilityRequirement requirement)
    {
        ArgumentNullException.ThrowIfNull(requirement);

        CapabilityGap? gap = null;

        foreach (var candidate in Candidates(requirement))
        {
            if (!_providersByCapability.TryGetValue(candidate, out var providers))
            {
                gap ??= new CapabilityGap
                {
                    CapabilityId = candidate,
                    Reason = CapabilityGapReason.CapabilityNotImplemented
                };

                continue;
            }

            var enabled = providers.FirstOrDefault(provider => provider.Enabled);
            if (enabled is not null)
            {
                return CapabilityResolution.Resolved(
                    candidate,
                    enabled,
                    usedFallback: !candidate.Equals(requirement.CapabilityId));
            }

            gap ??= new CapabilityGap
            {
                CapabilityId = candidate,
                Reason = CapabilityGapReason.ProviderDisabled,
                KnownProviderIds = providers.Select(provider => provider.ProviderId).ToList()
            };
        }

        return CapabilityResolution.Unresolved(
            gap ?? new CapabilityGap
            {
                CapabilityId = requirement.CapabilityId,
                Reason = CapabilityGapReason.CapabilityNotImplemented
            });
    }

    public CapabilityGap? FindGap(CapabilityRequirement requirement) => Resolve(requirement).Gap;

    private static IEnumerable<CapabilityId> Candidates(CapabilityRequirement requirement)
    {
        yield return requirement.CapabilityId;

        if (requirement.FallbackCapabilityIds is null)
        {
            yield break;
        }

        foreach (var fallback in requirement.FallbackCapabilityIds)
        {
            if (!fallback.Equals(requirement.CapabilityId))
            {
                yield return fallback;
            }
        }
    }
}
