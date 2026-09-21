using AIStudio.Application.Capabilities;

namespace AIStudio.Application.ProductionRecipes;

/// <summary>
/// A deliberately small seed catalog that proves the architecture: a content
/// format is data (id + version + capability requirements), not a C# subclass or
/// an enum value. Only two recipes are seeded on purpose; more formats should be
/// added as data when the Concept Registry arrives, not by adding types here.
/// </summary>
public static class SeedProductionRecipes
{
    public static IReadOnlyList<ProductionRecipe> All { get; } =
    [
        new()
        {
            Id = new ProductionRecipeId("tech-explainer"),
            Version = new ProductionRecipeVersion(1),
            DisplayName = "Tech explainer",
            Requirements =
            [
                ProductionRecipeRequirement.Required(CapabilityIds.SpeechNarration),
                ProductionRecipeRequirement.Required(
                    CapabilityIds.VisualDiagram,
                    CapabilityIds.VisualUiMotion,
                    CapabilityIds.VisualStill),
                ProductionRecipeRequirement.Required(CapabilityIds.MusicInstrumental),
                ProductionRecipeRequirement.Required(CapabilityIds.MediaCompose),
                ProductionRecipeRequirement.Required(CapabilityIds.SubtitleBurned)
            ]
        },
        new()
        {
            Id = new ProductionRecipeId("motion-comic"),
            Version = new ProductionRecipeVersion(1),
            DisplayName = "Motion comic",
            Requirements =
            [
                ProductionRecipeRequirement.Required(CapabilityIds.SpeechNarration),
                ProductionRecipeRequirement.Required(
                    CapabilityIds.VisualAiImage,
                    CapabilityIds.VisualStill),
                ProductionRecipeRequirement.Required(CapabilityIds.MusicInstrumental),
                ProductionRecipeRequirement.Required(CapabilityIds.MediaCompose),
                ProductionRecipeRequirement.Required(CapabilityIds.SubtitleBurned)
            ]
        }
    ];
}
