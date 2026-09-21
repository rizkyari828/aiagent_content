using AIStudio.Application.Capabilities;

namespace AIStudio.Application.ProductionRecipes;

/// <summary>
/// One capability a recipe needs, wrapping the capability-registry
/// <see cref="CapabilityRequirement"/> so the resolution rules are never
/// duplicated. A requirement is required (blocks producibility when unsatisfied)
/// or optional (reported but non-blocking). It is declarative data only: no
/// provider class, path, script, or workflow can be expressed here.
/// </summary>
public sealed record ProductionRecipeRequirement
{
    public CapabilityRequirement Capability { get; init; } = CapabilityRequirement.For(default);

    public bool IsRequired { get; init; } = true;

    public static ProductionRecipeRequirement Required(
        CapabilityId capabilityId,
        params CapabilityId[] fallbackCapabilityIds) =>
        new()
        {
            Capability = CapabilityRequirement.For(capabilityId, fallbackCapabilityIds),
            IsRequired = true
        };

    public static ProductionRecipeRequirement Optional(
        CapabilityId capabilityId,
        params CapabilityId[] fallbackCapabilityIds) =>
        new()
        {
            Capability = CapabilityRequirement.For(capabilityId, fallbackCapabilityIds),
            IsRequired = false
        };
}
