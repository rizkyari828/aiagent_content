namespace AIStudio.Application.ProductionRecipes;

/// <summary>
/// A concrete, already-selected production recipe identity persisted on a durable
/// job payload: which recipe and which exact version. It is never a "latest"
/// lookup — a job always replays the same version. The full recipe (requirements,
/// capabilities) is never persisted; execution resolves the exact (id, version)
/// from the trusted registry, so a retry cannot drift to a newer revision.
/// </summary>
public sealed record ProductionRecipeReference
{
    public ProductionRecipeId Id { get; init; }

    public ProductionRecipeVersion Version { get; init; }
}
