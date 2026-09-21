using AIStudio.Application.ProductionRecipes;

namespace AIStudio.Application.Concepts;

/// <summary>
/// A deliberately small seed catalog that proves a content concept is DATA: it
/// carries only descriptive metadata and a reference to an existing trusted
/// recipe. Only two concepts are seeded on purpose; more concepts (anime, kids,
/// affiliate, ...) should arrive later as data, not as new C# types.
/// </summary>
public static class SeedConcepts
{
    public static IReadOnlyList<ConceptManifest> All { get; } =
    [
        new()
        {
            Id = new ConceptId("local-ai-tech-explainer"),
            Version = new ConceptVersion(1),
            Title = "Run AI Locally Without Paying API Fees",
            Description = "Long-form developer explainer about running local AI without paid APIs.",
            Audience = "developers",
            Format = "youtube-longform",
            Style = "clean-tech",
            RecipeId = new ProductionRecipeId("tech-explainer"),
            RecipeVersion = new ProductionRecipeVersion(1),
            Duration = 600,
            Tags = ["local-ai", "developer"]
        },
        new()
        {
            Id = new ConceptId("local-ai-motion-comic"),
            Version = new ConceptVersion(1),
            Title = "Motion Comic: Local AI Without API Bills",
            Description = "Short motion-comic format about running AI locally.",
            Audience = "developers",
            Format = "youtube-short",
            Style = "motion-comic",
            RecipeId = new ProductionRecipeId("motion-comic"),
            Duration = 45,
            Tags = ["local-ai", "motion-comic"]
        }
    ];
}
