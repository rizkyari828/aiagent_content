using AIStudio.Application.Creative;
using AIStudio.Application.Stories;

namespace AIStudio.Application.Bibles;

/// <summary>
/// Input for one AI-backed bible planning pass: the approved creative direction and
/// the existing story plan. It never includes Script, Storyboard, media, or asset
/// data — stable identity is established from narrative planning alone.
/// </summary>
public sealed record StoryBiblePlanningRequest
{
    public CreativeDirection CreativeDirection { get; init; } = new();

    public StoryPlan StoryPlan { get; init; } = new();
}

/// <summary>
/// A validated proposal, not an applied change: the proposed stable character/world
/// bibles plus an explicit per-beat grounding map. The caller decides to register the
/// bibles and to apply the map through the deterministic <see cref="StoryPlanGrounder"/>.
/// Nothing is registered, persisted, or mutated here.
/// </summary>
public sealed record StoryBiblePlan
{
    public IReadOnlyList<CharacterBible> CharacterBibles { get; init; } = [];

    public IReadOnlyList<WorldBible> WorldBibles { get; init; } = [];

    public IReadOnlyList<StoryBeatGrounding> BeatGroundings { get; init; } = [];
}

/// <summary>
/// Proposes stable character/world bibles and a per-beat grounding map from narrative
/// planning. It is async by design (unlike the legacy synchronous story director) and
/// it never registers, persists, mutates, or executes anything.
/// </summary>
public interface IStoryBiblePlanner
{
    Task<StoryBiblePlan> BuildAsync(
        StoryBiblePlanningRequest request,
        CancellationToken cancellationToken);
}

/// <summary>
/// Raised when a story bible proposal cannot be produced or is not valid. The
/// <see cref="Code"/> is stable and safe to surface; model output is never repaired
/// or executed.
/// </summary>
public sealed class StoryBiblePlanningException : Exception
{
    public StoryBiblePlanningException(string code, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        Code = code;
    }

    public string Code { get; }
}

/// <summary>Stable Story Bible Planning error codes.</summary>
public static class StoryBiblePlanningErrorCodes
{
    public const string RequestInvalid = "story_bible_request_invalid";
    public const string InvalidJson = "story_bible_invalid_json";
    public const string PlanInvalid = "story_bible_invalid";
    public const string DuplicateCharacter = "story_bible_duplicate_character";
    public const string DuplicateWorld = "story_bible_duplicate_world";
    public const string RelationshipUnknown = "story_bible_relationship_unknown";
    public const string BeatUnknown = "story_bible_beat_unknown";
    public const string BeatDuplicate = "story_bible_beat_duplicate";
    public const string CharacterRefUnknown = "story_bible_character_ref_unknown";
    public const string WorldRefUnknown = "story_bible_world_ref_unknown";
}
