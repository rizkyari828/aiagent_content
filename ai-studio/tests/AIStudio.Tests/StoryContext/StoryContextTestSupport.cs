using AIStudio.Application.Bibles;
using AIStudio.Application.Concepts;
using AIStudio.Application.Creative;
using AIStudio.Application.ProductionRecipes;
using AIStudio.Application.Stories;
using AIStudio.Application.StoryContext;
using AIStudio.Tests.Bibles;
using AIStudio.Tests.Stories;

namespace AIStudio.Tests.StoryContext;

internal static class StoryContextTestSupport
{
    public static CreativeDirection Direction(
        string conceptId = "local-ai-tech-explainer",
        string audience = "developers",
        string format = "youtube-longform",
        string style = "clean-tech",
        int duration = 60) =>
        new()
        {
            IdeaReference = "idea-1",
            Concept = new ConceptManifest
            {
                Id = new ConceptId(conceptId),
                Version = new ConceptVersion(1),
                Title = "Run AI Locally Without API Fees",
                Description = "A developer explainer about running AI locally.",
                Audience = audience,
                Format = format,
                Style = style,
                RecipeId = new ProductionRecipeId("tech-explainer"),
                RecipeVersion = new ProductionRecipeVersion(1),
                Duration = duration,
                Tags = ["local-ai"]
            },
            Treatment = new CreativeTreatment
            {
                StoryApproach = "problem-discovery-payoff",
                HookTreatment = "cinematic developer struggling with API cost",
                Pacing = "fast",
                VisualStrategy = "character-led opening, technical clarity, payoff",
                EndingTreatment = "show local AI running successfully"
            }
        };

    /// <summary>Three beats referencing rio/hana and bedroom/office; alex and spaceship stay unreferenced.</summary>
    public static StoryPlan ThreeBeatPlan() =>
        StoryTestSupport.Plan(
        [
            StoryTestSupport.Beat("beat-01", 1, role: "hook", duration: 15, purpose: "hook purpose",
                characterRefs: ["rio"], worldRefs: ["bedroom"]),
            StoryTestSupport.Beat("beat-02", 2, role: "problem", duration: 25, purpose: "problem purpose",
                continuityFrom: ["beat-01"], characterRefs: ["rio"], worldRefs: ["office"]),
            StoryTestSupport.Beat("beat-03", 3, role: "payoff", duration: 20, purpose: "payoff purpose",
                continuityFrom: ["beat-02"], characterRefs: ["hana"], worldRefs: ["bedroom"])
        ],
        targetDuration: 60);

    public static CharacterBibleRegistry Characters() =>
        new(
        [
            BibleTestSupport.Character(
                "rio",
                relationships: [BibleTestSupport.Relationship("hana", "sibling"), BibleTestSupport.Relationship("alex", "friend")],
                assetReferences: [BibleTestSupport.Asset("character-rio-front-v1", "visual-reference")]),
            BibleTestSupport.Character("hana", role: "sibling", hair: "long-brown"),
            BibleTestSupport.Character("alex", role: "friend")
        ]);

    public static WorldBibleRegistry Worlds() =>
        new(
        [
            BibleTestSupport.World("bedroom", environmentType: "bedroom", recurringProps: ["desk", "window"],
                assetReferences: [BibleTestSupport.Asset("environment-bedroom-reference", "environment-reference")]),
            BibleTestSupport.World("office", environmentType: "office", recurringProps: ["desk", "monitor"]),
            BibleTestSupport.World("spaceship", environmentType: "spaceship", recurringProps: ["console"])
        ]);

    public static StoryContextBuilder Builder(
        ICharacterBibleRegistry? characters = null,
        IWorldBibleRegistry? worlds = null) =>
        new(characters ?? Characters(), worlds ?? Worlds());

    public static StoryContextRequest Request(
        CreativeDirection? direction = null,
        StoryPlan? plan = null,
        string? beatId = null,
        bool includeAssetReferences = false,
        IReadOnlyList<CharacterState>? characterStates = null,
        IReadOnlyList<WorldState>? worldStates = null) =>
        new()
        {
            CreativeDirection = direction ?? Direction(),
            StoryPlan = plan ?? ThreeBeatPlan(),
            BeatId = beatId is null ? null : new StoryBeatId(beatId),
            IncludeAssetReferences = includeAssetReferences,
            CharacterStates = characterStates ?? [],
            WorldStates = worldStates ?? []
        };
}
