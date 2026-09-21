using AIStudio.Application.Capabilities;
using AIStudio.Application.ProductionRecipes;

namespace AIStudio.Tests.ProductionRecipes;

internal static class ProductionRecipeTestSupport
{
    public static ProductionRecipe Recipe(
        string id,
        int version,
        params ProductionRecipeRequirement[] requirements) =>
        new()
        {
            Id = new ProductionRecipeId(id),
            Version = new ProductionRecipeVersion(version),
            DisplayName = id,
            Requirements = requirements
        };

    public static ProductionRecipeResolver Resolver(
        bool speechEnabled = true,
        bool musicEnabled = true,
        bool manimEnabled = true,
        bool imageEnabled = true,
        bool threeDEnabled = true) =>
        new(CapabilityRegistry(
            speechEnabled,
            musicEnabled,
            manimEnabled,
            imageEnabled,
            threeDEnabled));

    public static CapabilityRegistry CapabilityRegistry(
        bool speechEnabled = true,
        bool musicEnabled = true,
        bool manimEnabled = true,
        bool imageEnabled = true,
        bool threeDEnabled = true) =>
        new(
            ProductionCapabilityCatalog.Capabilities,
            ProductionCapabilityCatalog.CreateProviders(
                speechEnabled,
                musicEnabled,
                manimEnabled,
                imageEnabled,
                threeDEnabled));
}

/// <summary>
/// Delegating capability registry that records every requested capability. Used to
/// prove the recipe resolver delegates to the capability registry exactly once per
/// requirement instead of re-implementing selection.
/// </summary>
internal sealed class RecordingCapabilityRegistry : ICapabilityRegistry
{
    private readonly ICapabilityRegistry _inner;

    public RecordingCapabilityRegistry(ICapabilityRegistry inner)
    {
        _inner = inner;
    }

    public List<CapabilityId> RequestedCapabilities { get; } = [];

    public IReadOnlyList<CapabilityDescriptor> Capabilities => _inner.Capabilities;

    public bool IsRegistered(CapabilityId capabilityId) => _inner.IsRegistered(capabilityId);

    public IReadOnlyList<CapabilityProviderDescriptor> GetProviders(CapabilityId capabilityId) =>
        _inner.GetProviders(capabilityId);

    public IReadOnlyList<CapabilityProviderDescriptor> GetAvailableProviders(CapabilityId capabilityId) =>
        _inner.GetAvailableProviders(capabilityId);

    public CapabilityResolution Resolve(CapabilityRequirement requirement)
    {
        RequestedCapabilities.Add(requirement.CapabilityId);
        return _inner.Resolve(requirement);
    }

    public CapabilityGap? FindGap(CapabilityRequirement requirement) => _inner.FindGap(requirement);
}
