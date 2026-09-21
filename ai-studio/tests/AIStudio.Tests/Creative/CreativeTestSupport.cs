using AIStudio.Application.AI;
using AIStudio.Application.Creative;
using AIStudio.Application.ProductionRecipes;
using AIStudio.Tests.ProductionRecipes;

namespace AIStudio.Tests.Creative;

internal static class CreativeTestSupport
{
    public const string ValidDirectionJson = """
        {
          "ideaReference": "idea-123",
          "concept": {
            "id": "run-ai-locally-anime-short",
            "version": 1,
            "title": "Run AI Locally Without API Fees",
            "description": "Short-form anime-styled story about running AI locally.",
            "audience": "developers",
            "format": "anime-short",
            "style": "anime-cinematic",
            "recipeId": "motion-comic",
            "recipeVersion": 1,
            "duration": 45,
            "tags": ["local-ai", "developer"]
          },
          "treatment": {
            "storyApproach": "problem-discovery-payoff",
            "hookTreatment": "cinematic developer struggling with API cost",
            "pacing": "fast",
            "visualStrategy": "character-led opening, technical clarity in middle, cinematic payoff",
            "endingTreatment": "show local AI running successfully"
          }
        }
        """;

    public static ApprovedIdea Idea(
        string topic = "Run AI locally",
        string angle = "Stop paying API fees",
        string audience = "developers",
        string? objective = "educational",
        string hook = "Your existing PC may already be enough",
        string? reference = "idea-123") =>
        new()
        {
            IdeaReference = reference,
            Topic = topic,
            Angle = angle,
            Audience = audience,
            Objective = objective,
            HookPremise = hook
        };

    public static CreativePlanningContextProvider PlanningContext(
        bool speechEnabled = true,
        bool musicEnabled = true,
        bool manimEnabled = true,
        bool imageEnabled = true,
        bool threeDEnabled = true) =>
        new(
            new ProductionRecipeRegistry(SeedProductionRecipes.All),
            ProductionRecipeTestSupport.Resolver(
                speechEnabled,
                musicEnabled,
                manimEnabled,
                imageEnabled,
                threeDEnabled),
            ProductionRecipeTestSupport.CapabilityRegistry(
                speechEnabled,
                musicEnabled,
                manimEnabled,
                imageEnabled,
                threeDEnabled));

    public static CreativeDirector Director(
        FakeAiTextGenerator aiTextGenerator,
        bool speechEnabled = true,
        bool musicEnabled = true,
        bool manimEnabled = true,
        bool imageEnabled = true,
        bool threeDEnabled = true) =>
        new(
            aiTextGenerator,
            PlanningContext(speechEnabled, musicEnabled, manimEnabled, imageEnabled, threeDEnabled));
}

/// <summary>Records the requests sent to the AI boundary and returns a canned response.</summary>
internal sealed class FakeAiTextGenerator : IAiTextGenerator
{
    public List<AiTextRequest> Requests { get; } = [];

    public AiTextResponse Response { get; set; } =
        new(CreativeTestSupport.ValidDirectionJson, "fake-model", null, null, null);

    public int CallCount => Requests.Count;

    public Task<AiTextResponse> GenerateAsync(
        AiTextRequest request,
        CancellationToken cancellationToken)
    {
        Requests.Add(request);
        return Task.FromResult(Response);
    }
}
