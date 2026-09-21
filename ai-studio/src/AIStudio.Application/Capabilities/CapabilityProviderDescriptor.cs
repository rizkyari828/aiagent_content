namespace AIStudio.Application.Capabilities;

/// <summary>
/// Trusted, compile-time registration of one provider implementation for a
/// capability. Carries only metadata: no executable path, secret, model path, or
/// runtime handle. Registering a provider never loads or connects to the external
/// runtime; <see cref="Enabled"/> is applied from existing configuration options.
/// </summary>
public sealed record CapabilityProviderDescriptor
{
    /// <summary>Stable provider id, unique across the registry (for example "manim").</summary>
    public string ProviderId { get; init; } = string.Empty;

    public CapabilityId CapabilityId { get; init; }

    /// <summary>
    /// Higher values are preferred when one capability has several providers. Ties
    /// break deterministically by <see cref="ProviderId"/> ordinal order.
    /// </summary>
    public int Priority { get; init; }

    /// <summary>False means registered but not usable; configuration gates use.</summary>
    public bool Enabled { get; init; }

    /// <summary>Coarse runtime category such as local_python_gpu or in_process_cpu.</summary>
    public string RuntimeCategory { get; init; } = string.Empty;

    public CapabilityAvailability Availability =>
        Enabled ? CapabilityAvailability.Enabled : CapabilityAvailability.Registered;
}
