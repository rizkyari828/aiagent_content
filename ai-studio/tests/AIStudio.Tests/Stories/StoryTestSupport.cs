using AIStudio.Application.Concepts;
using AIStudio.Application.Creative;
using AIStudio.Application.ProductionRecipes;
using AIStudio.Application.Stories;

namespace AIStudio.Tests.Stories;

internal static class StoryTestSupport
{
    public static NarrativePatternBeatSlot Slot(
        string role,
        double weight = 1.0,
        bool required = true,
        string purpose = "guidance") =>
        new()
        {
            Role = new StoryBeatRole(role),
            Purpose = purpose,
            DurationWeight = weight,
            IsRequired = required
        };

    public static NarrativePattern Pattern(
        string id = "custom-pattern",
        int version = 1,
        string displayName = "Custom pattern",
        params NarrativePatternBeatSlot[] slots) =>
        new()
        {
            Id = new NarrativePatternId(id),
            Version = new NarrativePatternVersion(version),
            DisplayName = displayName,
            BeatSlots = slots.Length > 0 ? slots : [Slot("hook"), Slot("payoff")]
        };

    public static StoryBeat Beat(
        string id,
        int order,
        string role = "hook",
        int duration = 10,
        string importance = StoryBeat.DefaultImportance,
        string purpose = "narrative purpose",
        IEnumerable<string>? continuityFrom = null,
        IEnumerable<string>? characterRefs = null,
        IEnumerable<string>? worldRefs = null) =>
        new()
        {
            Id = new StoryBeatId(id),
            Order = order,
            Role = new StoryBeatRole(role),
            Importance = importance,
            Purpose = purpose,
            TargetDurationSeconds = duration,
            ContinuityFrom = (continuityFrom ?? []).Select(reference => new StoryBeatId(reference)).ToList(),
            CharacterRefs = characterRefs?.ToList() ?? [],
            WorldRefs = worldRefs?.ToList() ?? []
        };

    public static StoryPlan Plan(
        IReadOnlyList<StoryBeat>? beats = null,
        string id = "run-ai-locally-story",
        int version = 1,
        string sourceConceptId = "run-ai-locally-anime-short",
        string pattern = "problem-solution-short",
        int patternVersion = 1,
        int targetDuration = 60) =>
        new()
        {
            Id = new StoryPlanId(id),
            Version = new StoryPlanVersion(version),
            SourceConceptId = new ConceptId(sourceConceptId),
            NarrativePattern = new NarrativePatternId(pattern),
            NarrativePatternVersion = new NarrativePatternVersion(patternVersion),
            TargetDurationSeconds = targetDuration,
            Beats = beats ?? [Beat("beat-01", 1, duration: targetDuration)]
        };

    public static CreativeDirection Direction(
        string conceptId = "run-ai-locally-anime-short",
        int duration = 60) =>
        new()
        {
            IdeaReference = "idea-123",
            Concept = new ConceptManifest
            {
                Id = new ConceptId(conceptId),
                Version = new ConceptVersion(1),
                Title = "Run AI Locally Without API Fees",
                Description = "Short anime-styled story about running AI locally.",
                Audience = "developers",
                Format = "anime-short",
                Style = "anime-cinematic",
                RecipeId = new ProductionRecipeId("motion-comic"),
                RecipeVersion = new ProductionRecipeVersion(1),
                Duration = duration,
                Tags = ["local-ai"]
            },
            Treatment = new CreativeTreatment
            {
                StoryApproach = "problem-discovery-payoff",
                HookTreatment = "cinematic developer struggling with API cost",
                Pacing = "fast",
                VisualStrategy = "character-led opening, technical clarity, cinematic payoff",
                EndingTreatment = "show local AI running successfully"
            }
        };

    public static StoryDirectorRequest Request(
        CreativeDirection? direction = null,
        string pattern = "problem-solution-short",
        int? patternVersion = null,
        int? targetDuration = null,
        string? planId = null) =>
        new()
        {
            CreativeDirection = direction ?? Direction(),
            NarrativePattern = new NarrativePatternId(pattern),
            NarrativePatternVersion = patternVersion is { } version
                ? new NarrativePatternVersion(version)
                : null,
            TargetDurationSeconds = targetDuration,
            PlanId = planId is null ? null : new StoryPlanId(planId)
        };
}
