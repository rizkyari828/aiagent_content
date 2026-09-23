using AIStudio.Application.Capabilities;
using AIStudio.Application.ProductionRecipes;

namespace AIStudio.Application.Rendering.Visuals;

/// <summary>
/// Small, immutable visual-routing context derived from a production recipe's
/// visual capability requirements. It only tells the director which engine
/// ordinary narrative scenes should prefer; it carries no provider, path, or
/// workflow, so it can cross the Application boundary safely. Provider
/// availability still decides the final engine inside <see cref="SceneVisualRouter"/>.
/// </summary>
public sealed record SceneVisualRoutingProfile(SceneVisualEngine NarrativeEngine)
{
    /// <summary>
    /// Preserves the historical intent-only routing: ordinary narrative scenes
    /// stay on the deterministic CPU motion-graphics engine.
    /// </summary>
    public static readonly SceneVisualRoutingProfile Default =
        new(SceneVisualEngine.AnimatedSvg);

    /// <summary>
    /// Maps a recipe's first visual capability to a narrative engine preference.
    /// A recipe that leads with AI image generation enables the narrative image
    /// preference; every other recipe keeps the deterministic motion default. This
    /// reads capability data, never a recipe id string, so a new AI-image format is
    /// data rather than a special case.
    /// </summary>
    public static SceneVisualRoutingProfile FromRecipe(ProductionRecipe recipe)
    {
        ArgumentNullException.ThrowIfNull(recipe);

        foreach (var requirement in recipe.Requirements)
        {
            var capability = requirement.Capability.CapabilityId;
            if (!capability.Value.StartsWith("visual.", StringComparison.Ordinal))
            {
                continue;
            }

            return capability == CapabilityIds.VisualAiImage
                ? new SceneVisualRoutingProfile(SceneVisualEngine.AiImage)
                : Default;
        }

        return Default;
    }
}
